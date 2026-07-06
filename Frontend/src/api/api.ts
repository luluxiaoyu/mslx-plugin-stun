import request from 'mslx-request';

// --- 类型定义 ---
export interface TunnelConfig {
    id?: string;
    name: string;
    localIp: string;
    localPort: number;
    enableProxyProtocolV2: boolean;
    maxConnections: number;
}

export interface TunnelStats {
    id: string;
    activeConnections: number;
    speedUpload: number;      // Bytes/s
    speedDownload: number;    // Bytes/s
    totalUpload: number;      // Bytes
    totalDownload: number;    // Bytes
    outerAddress?: string;
    isRunning: boolean;
}

export interface TunnelItem {
    config: TunnelConfig;
    stats: TunnelStats | null;
}

// --- 接口调用 ---
const BASE_URL = '/api/plugins/mslx-plugin-stun/tunnels';

export const apiGetTunnels = () => {
    return request.get({ url: `${BASE_URL}/list` });
};

export const apiCreateTunnel = (data: TunnelConfig) => {
    return request.post({ url: `${BASE_URL}/create`, data });
};

export const apiUpdateTunnel = (data: TunnelConfig) => {
    return request.post({ url: `${BASE_URL}/update`, data });
};

export const apiDeleteTunnel = (id: string) => {
    return request.post({ url: `${BASE_URL}/delete?id=${id}` });
};

export const apiStartTunnel = (id: string) => {
    return request.post({ url: `${BASE_URL}/start?id=${id}` });
};

export const apiStopTunnel = (id: string) => {
    return request.post({ url: `${BASE_URL}/stop?id=${id}` });
};