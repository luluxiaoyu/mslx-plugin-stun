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
  WifiIcon
} from 'tdesign-icons-vue-next';
import * as signalR from '@microsoft/signalr'; // 根据实际宿主环境调整
import {
  apiGetTunnels, apiCreateTunnel, apiUpdateTunnel,
  apiDeleteTunnel, apiStartTunnel, apiStopTunnel,
  type TunnelConfig, type TunnelItem, type TunnelStats
} from '../api/api.ts';

// --- 状态管理 ---
const loading = ref(false);
const tunnels = ref<TunnelItem[]>([]);

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

// --- 数据加载 ---
const fetchTunnels = async () => {
  try {
    loading.value = true;
    const res: any = await apiGetTunnels();
    if (res?.data) tunnels.value = res.data;
  } catch (err: any) {
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
  const isRunning = item.stats?.isRunning;
  try {
    if (isRunning) {
      await apiStopTunnel(item.config.id!);
      MessagePlugin.success('隧道已停止');
    } else {
      await apiStartTunnel(item.config.id!);
      MessagePlugin.success('隧道启动指令已下发');
    }
    fetchTunnels();
  } catch (err: any) {
    MessagePlugin.error(`操作失败: ${err.message}`);
  }
};

const deleteTunnel = (id: string) => {
  const dialogIns = DialogPlugin.confirm({
    header: '删除确认',
    body: '确定要删除这个隧道吗？删除后配置不可恢复。',
    theme: 'danger',
    onConfirm: async () => {
      try {
        await apiDeleteTunnel(id);
        MessagePlugin.success('删除成功');
        fetchTunnels();
      } catch (err: any) {
        MessagePlugin.error(`删除失败: ${err.message}`);
      } finally {
        dialogIns.destroy();
      }
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
    // 建立 SignalR 连接 (路径请按需修改)
    hubConnection = new signalR.HubConnectionBuilder()
        .withUrl('/api/hubs/stun')
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
  <div class="mx-auto flex flex-col gap-6 text-[var(--td-text-color-primary)] pb-8 pt-6">

    <div class="design-card rounded-2xl glass-card border border-[var(--td-component-border)] shadow-sm p-6 flex flex-col md:flex-row justify-between items-center gap-4">
      <div class="flex items-center gap-4">
        <div class="w-14 h-14 shrink-0 flex items-center justify-center rounded-2xl bg-primary-light text-[var(--color-primary)]">
          <wifi-icon size="28px" />
        </div>
        <div>
          <h1 class="text-xl font-extrabold m-0 mb-1 flex items-center gap-2">
            STUN 隧道管理
            <t-tag theme="success" variant="light-outline" size="small" shape="round">NAT1 穿透</t-tag>
          </h1>
          <p class="text-[var(--td-text-color-secondary)] m-0 text-sm">
            基于 STUN 协议，突破内网限制，获取公网直连端口。
          </p>
        </div>
      </div>
      <t-button theme="primary" shape="round" @click="openCreateDialog">
        <template #icon><add-icon /></template>
        新建隧道
      </t-button>
    </div>

    <t-loading :loading="loading" text="加载隧道列表中..." size="small">
      <div v-if="tunnels.length === 0" class="py-12 text-center text-[var(--td-text-color-secondary)]">
        暂无隧道配置，点击右上角新建。
      </div>
      <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-5">
        <div
            v-for="(item, index) in tunnels" :key="item.config.id"
            class="list-item-anim design-card p-5 rounded-2xl glass-card border border-[var(--td-component-border)] flex flex-col gap-4 transition-all hover:-translate-y-1 hover:shadow-md"
            :style="{ animationDelay: `${index * 0.1}s` }"
        >
          <div class="flex justify-between items-start border-b border-dashed border-[var(--td-border-level-2-color)] pb-3">
            <div class="flex items-center gap-2">
              <div class="w-2.5 h-2.5 rounded-full shadow-sm" :class="item.stats?.isRunning ? 'bg-[var(--color-success)] shadow-[var(--color-success)]' : 'bg-[var(--td-text-color-placeholder)]'"></div>
              <h3 class="font-bold text-base m-0">{{ item.config.name }}</h3>
            </div>
            <div class="flex gap-1">
              <t-button shape="square" variant="text" size="small" @click="openEditDialog(item.config)">
                <edit-icon />
              </t-button>
              <t-button shape="square" variant="text" size="small" theme="danger" @click="deleteTunnel(item.config.id!)">
                <delete-icon />
              </t-button>
            </div>
          </div>

          <div class="flex flex-col gap-2 text-sm text-[var(--td-text-color-secondary)] flex-grow">
            <div class="flex justify-between">
              <span>本地目标:</span>
              <span class="font-mono text-[var(--td-text-color-primary)]">{{ item.config.localIp }}:{{ item.config.localPort }}</span>
            </div>
            <div class="flex justify-between">
              <span>公网地址:</span>
              <span class="font-mono font-bold" :class="item.stats?.isRunning ? 'text-[var(--color-primary)]' : 'text-[var(--td-text-color-placeholder)]'">
                {{ item.stats?.outerAddress || '未分配' }}
              </span>
            </div>
            <div class="flex items-center gap-2 mt-1">
              <t-tag v-if="item.config.enableProxyProtocolV2" theme="warning" variant="light" size="small">Proxy V2</t-tag>
              <t-tag theme="default" variant="light" size="small">最大连接: {{ item.config.maxConnections }}</t-tag>
            </div>
          </div>

          <div class="flex gap-3 pt-2">
            <t-button
                class="flex-1"
                :theme="item.stats?.isRunning ? 'danger' : 'primary'"
                variant="outline"
                @click="toggleTunnelState(item)"
            >
              <template #icon>
                <stop-circle-icon v-if="item.stats?.isRunning" />
                <play-circle-icon v-else />
              </template>
              {{ item.stats?.isRunning ? '停止' : '启动' }}
            </t-button>
            <t-button class="flex-1" theme="default" @click="openConsole(item.config)">
              <template #icon><terminal-icon /></template>
              控制台
            </t-button>
          </div>
        </div>
      </div>
    </t-loading>

    <t-dialog
        v-model:visible="formVisible"
        :header="isEditing ? '编辑 STUN 隧道' : '新建 STUN 隧道'"
        placement="center"
        :confirmBtn="isEditing ? '保存修改' : '确认创建'"
        @confirm="submitForm"
    >
      <div class="pt-4">
        <t-form :data="formData" label-width="110px">
          <t-form-item label="隧道名称" name="name">
            <t-input v-model="formData.name" placeholder="起个好记的名字" />
          </t-form-item>
          <t-form-item label="本地 IP" name="localIp">
            <t-input v-model="formData.localIp" placeholder="例如: 127.0.0.1" />
          </t-form-item>
          <t-form-item label="本地端口" name="localPort">
            <t-input-number v-model="formData.localPort" :min="1" :max="65535" class="w-full" />
          </t-form-item>
          <t-form-item label="并发连接数" name="maxConnections">
            <t-input-number v-model="formData.maxConnections" :min="1" :max="1024" class="w-full" />
          </t-form-item>
          <t-form-item label="Proxy V2 协议" name="enableProxyProtocolV2">
            <t-switch v-model="formData.enableProxyProtocolV2" />
            <div class="ml-3 text-xs text-[var(--td-text-color-secondary)]">向后端传递真实玩家 IP (需服务端支持)</div>
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
          <div class="bg-secondary-light p-3 rounded-xl border border-[var(--td-component-border)]">
            <div class="text-xs text-[var(--td-text-color-secondary)] mb-1">上行速度</div>
            <div class="font-mono font-bold text-[var(--color-primary)]">{{ consoleStats ? formatTraffic(consoleStats.speedUpload) : '0 B' }}/s</div>
          </div>
          <div class="bg-secondary-light p-3 rounded-xl border border-[var(--td-component-border)]">
            <div class="text-xs text-[var(--td-text-color-secondary)] mb-1">下行速度</div>
            <div class="font-mono font-bold text-[var(--color-success)]">{{ consoleStats ? formatTraffic(consoleStats.speedDownload) : '0 B' }}/s</div>
          </div>
          <div class="bg-secondary-light p-3 rounded-xl border border-[var(--td-component-border)]">
            <div class="text-xs text-[var(--td-text-color-secondary)] mb-1">当前连接数</div>
            <div class="font-mono font-bold text-[var(--color-warning)]">{{ consoleStats?.activeConnections || 0 }} / {{ activeTunnel?.maxConnections || 128 }}</div>
          </div>
          <div class="bg-secondary-light p-3 rounded-xl border border-[var(--td-component-border)]">
            <div class="text-xs text-[var(--td-text-color-secondary)] mb-1">总流量</div>
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
            <span class="ml-auto text-xs text-[var(--color-success)] flex items-center gap-1" v-if="consoleStats?.isRunning">
              <div class="w-2 h-2 rounded-full bg-[var(--color-success)] animate-pulse"></div> 运行中
            </span>
          </div>
          <div ref="logContainerRef" class="p-4 overflow-y-auto h-full flex-grow custom-scrollbar">
            <pre class="m-0 text-[13px] font-mono leading-relaxed text-[#d4d4d4] whitespace-pre-wrap"><code v-for="(log, idx) in consoleLogs" :key="idx" class="block">{{ log }}</code></pre>
            <div v-if="consoleLogs.length === 0" class="text-[#56b6c2] text-sm font-mono mt-2">
              > 等待日志输出...
            </div>
          </div>
        </div>
      </div>
    </t-dialog>

  </div>
</template>

<style scoped>
@unocss;
.bg-primary-light { background-color: color-mix(in srgb, var(--td-brand-color) 10%, transparent); }
.bg-success-light { background-color: color-mix(in srgb, var(--td-success-color) 10%, transparent); }
.bg-warning-light { background-color: color-mix(in srgb, var(--td-warning-color) 10%, transparent); }
.bg-secondary-light { background-color: color-mix(in srgb, var(--td-bg-color-secondarycontainer) 50%, transparent); }

/* 动画与滚动条优化 */
.list-item-anim {
  animation: slideUp 0.6s cubic-bezier(0.2, 0.8, 0.2, 1) backwards;
}

@keyframes slideUp {
  from { opacity: 0; transform: translateY(20px); }
  to { opacity: 1; transform: translateY(0); }
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