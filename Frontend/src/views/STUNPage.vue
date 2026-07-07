<script setup lang="ts">
import { ref, onMounted, onBeforeUnmount, nextTick } from 'vue';
import { MessagePlugin, DialogPlugin } from 'tdesign-vue-next';
import {
  AddIcon,
  PlayCircleIcon,
  StopCircleIcon,
  TerminalIcon,
  DeleteIcon,
  EditIcon,
  RefreshIcon,
  FileCopyIcon,
  InfoCircleIcon
} from 'tdesign-icons-vue-next';
import * as signalR from '@microsoft/signalr';
import {
  apiGetTunnels, apiCreateTunnel, apiUpdateTunnel,
  apiDeleteTunnel, apiStartTunnel, apiStopTunnel,
  type TunnelConfig, type TunnelItem, type TunnelStats
} from '../api/api.ts';

// --- 宿主状态 ---
const stores = (window as any).MSLX_Stores;
const userStore = stores?.getUserStore?.();

// --- 状态管理 ---
const loading = ref(true);
const isError = ref(false);
const tunnels = ref<TunnelItem[]>([]);
const operatingIds = ref<Set<string>>(new Set()); // 追踪正在启动/停止的隧道ID

// --- 表单弹窗状态 ---
const formVisible = ref(false);
const isEditing = ref(false);
const formData = ref<TunnelConfig>({
  name: '我的 STUN 隧道',
  localIp: '127.0.0.1',
  localPort: 25565,
  enableProxyProtocolV2: false,
  maxConnections: 128
});

// --- 控制台弹窗状态 ---
const consoleVisible = ref(false);
const activeTunnel = ref<TunnelConfig | null>(null);
const consoleLogs = ref<string[]>([]);
const consoleStats = ref<TunnelStats | null>(null);
const logContainerRef = ref<HTMLElement | null>(null);
let hubConnection: signalR.HubConnection | null = null;

// --- 工具函数 ---
const formatTraffic = (bytes: number) => {
  if (!bytes) return '0 B';
  if (bytes < 1024) return `${bytes} B`;
  const kb = bytes / 1024;
  if (kb < 1024) return `${kb.toFixed(2)} KB`;
  const mb = kb / 1024;
  if (mb < 1024) return `${mb.toFixed(2)} MB`;
  return `${(mb / 1024).toFixed(2)} GB`;
};

const scrollToBottom = async () => {
  await nextTick();
  if (logContainerRef.value) {
    logContainerRef.value.scrollTop = logContainerRef.value.scrollHeight;
  }
};

const copyAddress = async (address?: string) => {
  if (!address || address === '未分配') return;
  try {
    await navigator.clipboard.writeText(address);
    MessagePlugin.success('公网地址已复制');
  } catch (err) {
    MessagePlugin.error('复制失败，请手动选取复制');
  }
};

// --- 数据加载 ---
const fetchTunnels = async () => {
  try {
    loading.value = true;
    isError.value = false;
    const res: any = await apiGetTunnels();
    if (res?.code === 200 && Array.isArray(res?.data)) {
      tunnels.value = res.data;
    } else if (res?.data?.code === 200 && Array.isArray(res?.data?.data)) {
      tunnels.value = res.data.data;
    } else if (Array.isArray(res)) {
      tunnels.value = res;
    } else {
      tunnels.value = [];
    }
  } catch (err: any) {
    console.error(err);
    isError.value = true;
    MessagePlugin.error(`拉取隧道列表失败: ${err.message}`);
  } finally {
    loading.value = false;
  }
};

// --- 隧道操作 ---
const openCreateDialog = () => {
  isEditing.value = false;
  formData.value = {
    name: '新建隧道',
    localIp: '127.0.0.1',
    localPort: 25565,
    enableProxyProtocolV2: false,
    maxConnections: 128
  };
  formVisible.value = true;
};

const openEditDialog = (item: TunnelConfig) => {
  isEditing.value = true;
  formData.value = { ...item };
  formVisible.value = true;
};

const submitForm = async () => {
  try {
    if (isEditing.value) {
      await apiUpdateTunnel(formData.value);
      MessagePlugin.success('隧道更新成功');
    } else {
      await apiCreateTunnel(formData.value);
      MessagePlugin.success('隧道创建成功');
    }
    formVisible.value = false;
    fetchTunnels();
  } catch (err: any) {
    MessagePlugin.error(`操作失败: ${err.message}`);
  }
};

const toggleTunnelState = async (item: TunnelItem) => {
  if (!item.config.id) return;

  const isRunning = item.stats?.isRunning;
  operatingIds.value.add(item.config.id); // 按钮转圈圈

  try {
    if (isRunning) {
      await apiStopTunnel(item.config.id);
      MessagePlugin.success('隧道已停止');
    } else {
      await apiStartTunnel(item.config.id);
      MessagePlugin.success('隧道启动指令已下发');
    }
    // 延迟 1s 刷新以获取最新状态
    setTimeout(() => {
      fetchTunnels();
      operatingIds.value.delete(item.config.id!);
    }, 1000);
  } catch (err: any) {
    MessagePlugin.error(`操作失败: ${err.message}`);
    operatingIds.value.delete(item.config.id);
  }
};

const deleteTunnel = (id: string) => {
  const dialogIns = DialogPlugin.confirm({
    header: '确认删除隧道?',
    body: '删除后该隧道配置将无法恢复。确定要继续吗？',
    theme: 'danger',
    onConfirm: async () => {
      try {
        await apiDeleteTunnel(id);
        MessagePlugin.success(`隧道删除成功`);
        await fetchTunnels();
        dialogIns.hide();
      } catch (err: any) {
        MessagePlugin.error(`删除失败: ${err.message}`);
      }
    },
    onClose: () => {
      dialogIns.hide();
    }
  });
};

// --- 控制台与 SignalR 逻辑 ---
const openConsole = async (item: TunnelConfig) => {
  activeTunnel.value = item;
  consoleLogs.value = [];
  consoleStats.value = null;
  consoleVisible.value = true;

  try {
    const { baseUrl, token } = userStore || {};

    const hubUrl = new URL('/api/hubs/plugins/mslx-plugin-stun/stun', baseUrl || window.location.origin);
    if (token) {
      hubUrl.searchParams.append('x-user-token', token);
    }

    hubConnection = new signalR.HubConnectionBuilder()
        .withUrl(hubUrl.href, {
          skipNegotiation: true,
          transport: signalR.HttpTransportType.WebSockets
        })
        .withAutomaticReconnect()
        .build();

    hubConnection.on('ReceiveLog', (logMsg: string) => {
      consoleLogs.value.push(logMsg);
      if (consoleLogs.value.length > 500) consoleLogs.value.shift();
      scrollToBottom();
    });

    hubConnection.on('ReceiveStats', (stats: TunnelStats) => {
      consoleStats.value = stats;
    });

    await hubConnection.start();
    await hubConnection.invoke('JoinGroup', item.id);
    consoleLogs.value.push(`// MSLX 控制台：成功连接到隧道 [${item.name}] 日志流...`);
  } catch (err) {
    consoleLogs.value.push(`// 连接控制台失败: ${err}`);
  }
};

const closeConsole = async () => {
  if (hubConnection && activeTunnel.value) {
    try {
      await hubConnection.invoke('LeaveGroup', activeTunnel.value.id);
      await hubConnection.stop();
    } catch (err) {
      console.error('关闭 SignalR 失败', err);
    }
  }
  hubConnection = null;
  activeTunnel.value = null;
  consoleVisible.value = false;
};

onMounted(() => {
  fetchTunnels();
});

onBeforeUnmount(() => {
  if (hubConnection) hubConnection.stop();
});
</script>

<template>
  <div class="mx-auto flex flex-col gap-6 text-[var(--td-text-color-primary)] pb-5">

    <div class="design-card flex flex-col sm:flex-row sm:items-center justify-between gap-4 p-5 bg-[var(--td-bg-color-container)] rounded-2xl border border-[var(--td-component-border)] shadow-sm text-left">
      <div class="flex flex-col gap-1 items-start">
        <h2 class="text-lg font-bold tracking-tight text-[var(--td-text-color-primary)] m-0 flex items-center gap-2">
          STUN 隧道管理
          <t-tag theme="success" variant="light-outline" size="small" shape="round">NAT1 穿透</t-tag>
        </h2>
        <p class="text-sm text-[var(--td-text-color-secondary)] m-0">
          利用 STUN 技术，在 NAT1 环境下获取公网端口，支持多开与流量监控。
        </p>
      </div>

      <div class="flex items-center gap-2 sm:gap-3 flex-wrap">
        <t-button variant="dashed" @click="fetchTunnels" :loading="loading">
          <template #icon><refresh-icon /></template>
          刷新
        </t-button>
        <t-button theme="primary" @click="openCreateDialog">
          <template #icon><add-icon /></template>
          创建隧道
        </t-button>
      </div>
    </div>

    <div class="relative min-h-[400px]">
      <div v-if="loading && tunnels.length === 0" class="flex flex-col items-center justify-center py-24">
        <t-loading size="medium" text="正在获取隧道信息..." />
      </div>

      <div v-else-if="isError" class="flex flex-col items-center justify-center py-16 design-card rounded-2xl border border-[var(--td-error-color)]">
        <div class="text-[var(--td-error-color)] font-bold mb-3">数据获取失败</div>
        <p class="text-sm text-[var(--td-text-color-secondary)] mb-4">无法连接到服务器，请检查网络</p>
        <t-button theme="primary" @click="fetchTunnels">重试</t-button>
      </div>

      <div v-else-if="tunnels.length === 0" class="flex flex-col items-center justify-center py-24 design-card bg-[var(--td-bg-color-container)] rounded-2xl border-2 border-dashed border-[var(--td-component-border)]">
        <div class="text-[var(--td-text-color-primary)] font-bold mb-2">暂无隧道</div>
        <p class="text-sm text-[var(--td-text-color-secondary)] mb-4">当前配置列表为空，快去创建一个吧</p>
        <t-button theme="primary" @click="openCreateDialog">立即创建</t-button>
      </div>

      <div v-else class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 2xl:grid-cols-4 gap-4">
        <div
            v-for="(item, index) in tunnels"
            :key="item.config.id"
            :style="{ animationDelay: `${index * 0.05}s` }"
            class="list-item-anim design-card group flex flex-col bg-[var(--td-bg-color-container)] rounded-2xl border border-[var(--td-component-border)] shadow-sm hover:shadow-md hover:border-[var(--color-primary)] transition-all duration-300 p-5 gap-5"
        >
          <div class="flex items-center justify-between gap-3">
            <div class="flex items-center gap-2.5 min-w-0">
              <div class="relative flex items-center justify-center shrink-0">
                <span
                    v-if="item.stats?.isRunning"
                    class="absolute w-2.5 h-2.5 bg-[#34c759] rounded-full animate-ping"
                ></span>
                <span
                    :class="item.stats?.isRunning ? 'bg-[#34c759]' : 'bg-[var(--td-text-color-placeholder)]'"
                    class="relative w-2 h-2 rounded-full"
                ></span>
              </div>
              <h4 class="text-base font-bold text-[var(--td-text-color-primary)] truncate tracking-tight">
                {{ item.config.name }}
              </h4>
            </div>

            <div class="flex items-center gap-1 shrink-0">
              <t-button shape="circle" variant="text" size="small" @click="openEditDialog(item.config)">
                <template #icon><edit-icon size="16" /></template>
              </t-button>
              <t-button shape="circle" theme="danger" variant="text" size="small" @click.stop="deleteTunnel(item.config.id!)">
                <template #icon><delete-icon size="16" /></template>
              </t-button>
            </div>
          </div>

          <div class="flex flex-col gap-3 text-sm flex-grow">
            <div class="flex flex-col gap-1">
              <span class="text-[10px] text-[var(--td-text-color-secondary)] uppercase tracking-widest font-black">本地目标映射</span>
              <span class="font-mono text-[var(--td-text-color-primary)] font-bold bg-[var(--td-bg-color-secondarycontainer)] px-2 py-1 rounded inline-block w-fit">
                {{ item.config.localIp }}:{{ item.config.localPort }}
              </span>
            </div>

            <div class="flex flex-col gap-1">
              <span class="text-[10px] text-[var(--td-text-color-secondary)] uppercase tracking-widest font-black">公网访问地址</span>
              <div
                  class="flex items-center gap-2 font-mono font-bold"
                  :class="item.stats?.isRunning ? 'text-[var(--color-primary)] cursor-pointer' : 'text-[var(--td-text-color-placeholder)]'"
                  @click="copyAddress(item.stats?.outerAddress)"
                  title="点击复制地址"
              >
                {{ item.stats?.outerAddress || '隧道未启动' }}
                <file-copy-icon v-if="item.stats?.isRunning" size="14px" class="opacity-70 hover:opacity-100" />
              </div>
            </div>

            <div class="flex items-center gap-2 mt-2">
              <t-tag v-if="item.config.enableProxyProtocolV2" theme="warning" variant="light-outline" size="small" class="!text-[10px] font-black italic tracking-tighter">PROXY V2</t-tag>
              <t-tag theme="default" variant="light-outline" size="small" class="!text-[10px] font-black tracking-tighter">最大连接数: {{ item.config.maxConnections }}</t-tag>
            </div>
          </div>

          <div class="flex items-center justify-between pt-4 mt-auto border-t border-dashed border-[var(--td-border-level-2-color)] gap-3">
            <t-button
                class="flex-1"
                :theme="item.stats?.isRunning ? 'danger' : 'primary'"
                variant="outline"
                :loading="operatingIds.has(item.config.id!)"
                @click="toggleTunnelState(item)"
            >
              <template #icon v-if="!operatingIds.has(item.config.id!)">
                <stop-circle-icon v-if="item.stats?.isRunning" />
                <play-circle-icon v-else />
              </template>
              {{ item.stats?.isRunning ? '停止运行' : '启动隧道' }}
            </t-button>
            <t-button theme="default" variant="dashed" @click="openConsole(item.config)" class="shrink-0 px-3">
              <template #icon><terminal-icon /></template>
            </t-button>
          </div>
        </div>
      </div>
    </div>

    <t-dialog
        v-model:visible="formVisible"
        :header="isEditing ? '编辑 STUN 隧道' : '新建 STUN 隧道'"
        placement="center"
        width="480px"
        :confirmBtn="isEditing ? '保存修改' : '确认创建'"
        @confirm="submitForm"
    >
      <div class="pt-4 pb-2">
        <t-form :data="formData" label-width="110px" label-align="left">
          <t-form-item label="隧道名称" name="name">
            <t-input v-model="formData.name" placeholder="例如: Minecraft 生存服" />
          </t-form-item>
          <t-form-item label="本地 IP" name="localIp">
            <t-input v-model="formData.localIp" placeholder="例如: 127.0.0.1" />
          </t-form-item>
          <t-form-item label="本地端口" name="localPort">
            <t-input-number v-model="formData.localPort" :min="1" :max="65535" theme="normal" class="w-full" />
          </t-form-item>
          <t-form-item label="最大连接数" name="maxConnections">
            <t-input-number v-model="formData.maxConnections" :min="1" :max="1024" theme="normal" class="w-full" />
          </t-form-item>

          <t-form-item label="透传玩家IP" name="enableProxyProtocolV2">
            <div class="flex items-center gap-2">
              <t-switch v-model="formData.enableProxyProtocolV2" />
              <t-popup placement="top" content="传递玩家真实IP。仅限后端服务端(如 Velocity / Paper / 插件或模组)支持并开启 Proxy Protocol 时使用，否则会导致玩家无法连接！">
                <info-circle-icon class="text-[var(--td-text-color-placeholder)] cursor-help hover:text-[var(--td-brand-color)] transition-colors" />
              </t-popup>
              请确认服务端支持，否则会无法连接游戏。
            </div>
          </t-form-item>
        </t-form>
      </div>
    </t-dialog>

    <t-dialog
        v-model:visible="consoleVisible"
        :header="`终端 - ${activeTunnel?.name || '未知'}`"
        placement="center"
        width="800px"
        :footer="false"
        @close="closeConsole"
    >
      <div class="flex flex-col h-[500px] mt-2">
        <div class="grid grid-cols-4 gap-3 mb-4">
          <div class="bg-[var(--td-bg-color-secondarycontainer)] p-3 rounded-xl border border-[var(--td-component-border)]">
            <div class="text-xs text-[var(--td-text-color-secondary)] mb-1 font-bold">上行速度</div>
            <div class="font-mono font-bold text-[var(--color-primary)]">{{ consoleStats ? formatTraffic(consoleStats.speedUpload) : '0 B' }}/s</div>
          </div>
          <div class="bg-[var(--td-bg-color-secondarycontainer)] p-3 rounded-xl border border-[var(--td-component-border)]">
            <div class="text-xs text-[var(--td-text-color-secondary)] mb-1 font-bold">下行速度</div>
            <div class="font-mono font-bold text-[var(--color-success)]">{{ consoleStats ? formatTraffic(consoleStats.speedDownload) : '0 B' }}/s</div>
          </div>
          <div class="bg-[var(--td-bg-color-secondarycontainer)] p-3 rounded-xl border border-[var(--td-component-border)]">
            <div class="text-xs text-[var(--td-text-color-secondary)] mb-1 font-bold">活跃连接</div>
            <div class="font-mono font-bold text-[var(--color-warning)]">{{ consoleStats?.activeConnections || 0 }} / {{ activeTunnel?.maxConnections || 128 }}</div>
          </div>
          <div class="bg-[var(--td-bg-color-secondarycontainer)] p-3 rounded-xl border border-[var(--td-component-border)]">
            <div class="text-xs text-[var(--td-text-color-secondary)] mb-1 font-bold">总流量</div>
            <div class="font-mono font-bold text-[var(--td-text-color-primary)]">{{ consoleStats ? formatTraffic(consoleStats.totalUpload + consoleStats.totalDownload) : '0 B' }}</div>
          </div>
        </div>

        <div class="relative w-full flex-grow rounded-xl border border-[var(--td-component-border)] bg-[#1e1e1e] overflow-hidden shadow-inner flex flex-col">
          <div class="flex items-center px-4 py-2.5 bg-[#2d2d2d] border-b border-[#404040]">
            <div class="flex gap-1.5">
              <div class="w-3 h-3 rounded-full bg-[#ff5f56]"></div>
              <div class="w-3 h-3 rounded-full bg-[#ffbd2e]"></div>
              <div class="w-3 h-3 rounded-full bg-[#27c93f]"></div>
            </div>
            <span class="ml-4 text-xs font-mono text-[#858585]">mslx-stun-daemon</span>
            <span class="ml-auto text-xs text-[#34c759] flex items-center gap-1 font-bold" v-if="consoleStats?.isRunning">
              <div class="w-2 h-2 rounded-full bg-[#34c759] animate-pulse"></div> 运行中
            </span>
          </div>
          <div ref="logContainerRef" class="p-4 overflow-y-auto h-full flex-grow custom-scrollbar">
            <pre class="m-0 text-[13px] font-mono leading-relaxed text-[#d4d4d4] whitespace-pre-wrap"><code v-for="(log, idx) in consoleLogs" :key="idx" class="block">{{ log }}</code></pre>
            <div v-if="consoleLogs.length === 0" class="text-[#56b6c2] text-sm font-mono mt-2">
              > 正在挂起连接，等待输出...
            </div>
          </div>
        </div>
      </div>
    </t-dialog>

  </div>
</template>

<style scoped>
@unocss;

.list-item-anim {
  animation: slideUp 0.4s cubic-bezier(0.2, 0.8, 0.2, 1) backwards;
  will-change: transform, opacity;
}

@keyframes slideUp {
  from {
    opacity: 0;
    transform: translateY(16px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

:deep(.t-dialog) {
  @apply !rounded-2xl shadow-2xl;
}

.custom-scrollbar::-webkit-scrollbar {
  width: 8px;
  height: 8px;
}
.custom-scrollbar::-webkit-scrollbar-track {
  background: rgba(0,0,0,0.1);
  border-radius: 4px;
}
.custom-scrollbar::-webkit-scrollbar-thumb {
  background: #4a4a4a;
  border-radius: 4px;
}
.custom-scrollbar::-webkit-scrollbar-thumb:hover {
  background: #666;
}
</style>