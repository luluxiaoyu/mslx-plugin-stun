using MSLX.Plugin.Stun.Models;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MSLX.Plugin.Stun.Core;

public class StunTunnel
{
    public TunnelConfig Config { get; }
    public bool IsRunning { get; private set; }
    public IPEndPoint? OuterEndPoint { get; private set; }

    private CancellationTokenSource? _tunnelCts;
    private TcpListener? _natterListener;

    private readonly ConcurrentQueue<string> _logHistory = new();
    private const int MaxLogHistory = 200; // 最多保留200行历史日志

    // 统计数据
    private long _totalUploadBytes;
    private long _totalDownloadBytes;
    private long _lastUploadBytes;
    private long _lastDownloadBytes;
    private int _activeConnections;

    private readonly Action<string, string> _logAction;

    private readonly string[] _stunServers = {
            "fwa.lifesizecloud.com",
            "global.turn.twilio.com",
            "turn.cloudflare.com",
            "stun.nextcloud.com",
            "stun.freeswitch.org"
        };

    public StunTunnel(TunnelConfig config, Action<string, string> logAction)
    {
        Config = config;
        _logAction = logAction;
    }

    // 获取历史日志的方法
    public IEnumerable<string> GetLogHistory()
    {
        return _logHistory.ToArray();
    }

    // 日志
    private void Log(string msg)
    {
        string formattedLog = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{Config.Name}] {msg}";

        _logHistory.Enqueue(formattedLog);

        while (_logHistory.Count > MaxLogHistory && _logHistory.TryDequeue(out _)) { }

        _logAction?.Invoke(Config.Id, formattedLog);
    }

    public async Task StartAsync()
    {
        if (IsRunning) return;
        IsRunning = true;
        _tunnelCts = new CancellationTokenSource();

        ResetStats();
        Log("[INFO] 隧道环境初始化...");

        try
        {
            await Task.Run(() => DoNat1SocketForwardWork(_tunnelCts.Token));
        }
        catch (Exception ex)
        {
            Log($"[ERROR] 隧道宿主崩溃: {ex.Message}");
            Stop();
        }
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;

        try { _tunnelCts?.Cancel(); } catch { }
        try { _natterListener?.Stop(); } catch { }

        OuterEndPoint = null;
        Log("[INFO] 隧道服务已被关闭。");
    }

    private void DoNat1SocketForwardWork(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            int allocatedLocalPort = 0;
            OuterEndPoint = null;

            foreach (var server in _stunServers)
            {
                if (token.IsCancellationRequested) return;
                Log($"[INFO] 正在尝试探测: {server}...");
                try
                {
                    OuterEndPoint = GetCleanStunMapping(server, out allocatedLocalPort);
                    if (OuterEndPoint != null) break;
                }
                catch (Exception ex)
                {
                    Log($"[WARN] STUN [{server}] 暂未响应: {ex.Message}");
                }
            }

            if (OuterEndPoint == null)
            {
                Log("[ERROR] 启动失败，无法获取公网 IP。");
                Stop();
                return;
            }

            IPAddress basePublicIP = OuterEndPoint.Address;

            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                Task.Run(() => StartWindowsKeepAlivePump(allocatedLocalPort, linkedCts.Token), linkedCts.Token);

                try
                {
                    _natterListener = new TcpListener(IPAddress.Any, allocatedLocalPort);
                    _natterListener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    _natterListener.Start(5);

                    Log($"[INFO] 服务已就绪！远程地址: tcp://{OuterEndPoint}");

                    var listenTask = Task.Run(() =>
                    {
                        try
                        {
                            while (!linkedCts.Token.IsCancellationRequested)
                            {
                                TcpClient inboundClient = _natterListener.AcceptTcpClient();
                                if (_activeConnections >= Config.MaxConnections)
                                {
                                    Log($"[WARN] 拒绝连接：已达最大并发 {Config.MaxConnections}。");
                                    inboundClient.Close();
                                    continue;
                                }

                                Task.Run(async () =>
                                {
                                    Interlocked.Increment(ref _activeConnections);
                                    await HandleTcpSocketForward(inboundClient, linkedCts.Token);
                                    Interlocked.Decrement(ref _activeConnections);
                                }, linkedCts.Token);
                            }
                        }
                        catch { }
                    }, linkedCts.Token);

                    // IP 变动检测循环
                    int checkCounter = 0;
                    while (!linkedCts.Token.IsCancellationRequested)
                    {
                        try { Task.Delay(15000, linkedCts.Token).Wait(linkedCts.Token); } catch { break; }

                        checkCounter = (checkCounter + 1) % 4;
                        if (checkCounter == 0)
                        {
                            IPEndPoint? currentOuter = null;
                            foreach (var server in _stunServers)
                            {
                                try
                                {
                                    currentOuter = GetCleanStunMapping(server, out int temp);
                                    if (currentOuter != null) break;
                                }
                                catch { }
                            }

                            if (currentOuter != null && !currentOuter.Address.Equals(basePublicIP))
                            {
                                Log($"[WARN] 公网 IP 变动！旧: {basePublicIP} -> 新: {currentOuter.Address}");
                                Log("[INFO] 正在重载隧道服务...");
                                linkedCts.Cancel();
                                _natterListener?.Stop();
                                break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"[ERROR] 底层管道异常: {ex.Message}，10秒后重试...");
                    try { Task.Delay(10000, token).Wait(token); } catch { return; }
                }
                finally
                {
                    if (_natterListener != null)
                    {
                        try { _natterListener.Stop(); } catch { }
                        _natterListener = null;
                    }
                    Thread.Sleep(1000);
                }
            }
        }
    }

    private async Task HandleTcpSocketForward(TcpClient inboundClient, CancellationToken token)
    {
        string remoteEpStr = inboundClient.Client.RemoteEndPoint?.ToString() ?? "未知客户端";
        Log($"[CONN] 收到请求: [{remoteEpStr}]");

        using (inboundClient)
        using (TcpClient localBackendClient = new TcpClient())
        {
            try
            {
                var result = localBackendClient.BeginConnect(Config.LocalIp, Config.LocalPort, null, null);
                if (!result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(3)))
                {
                    Log($"[CONN] 转发失败：无法连接 {Config.LocalIp}:{Config.LocalPort}");
                    return;
                }
                localBackendClient.EndConnect(result);

                using (NetworkStream extStream = inboundClient.GetStream())
                using (NetworkStream localStream = localBackendClient.GetStream())
                {
                    if (Config.EnableProxyProtocolV2 && inboundClient.Client.RemoteEndPoint is IPEndPoint remoteEp && localBackendClient.Client.LocalEndPoint is IPEndPoint localEp)
                    {
                        byte[] proxyHeader = BuildProxyProtocolV2Header(remoteEp, localEp);
                        await localStream.WriteAsync(proxyHeader, 0, proxyHeader.Length, token);
                        await localStream.FlushAsync(token);
                    }

                    Log($"[CONN] [{remoteEpStr}] 已连上隧道。");

                    Task extToLocal = CopySocketStreamAsync(extStream, localStream, false, token);
                    Task localToExt = CopySocketStreamAsync(localStream, extStream, true, token);
                    await Task.WhenAny(extToLocal, localToExt);
                }
            }
            catch (Exception ex)
            {
                Log($"[CONN] [{remoteEpStr}] 异常断开: {ex.Message}");
            }
            finally
            {
                Log($"[CONN] [{remoteEpStr}] 释放连接。");
            }
        }
    }

    private async Task CopySocketStreamAsync(NetworkStream source, NetworkStream dest, bool isUpload, CancellationToken token)
    {
        byte[] buffer = new byte[8192];
        int read;
        try
        {
            while (!token.IsCancellationRequested && (read = await source.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
            {
                await dest.WriteAsync(buffer, 0, read, token);
                await dest.FlushAsync(token);

                if (isUpload) Interlocked.Add(ref _totalUploadBytes, read);
                else Interlocked.Add(ref _totalDownloadBytes, read);
            }
        }
        catch { }
    }

    // 原生 STUN 解析逻辑保留
    private IPEndPoint? GetCleanStunMapping(string stunServer, out int localPort)
    {
        localPort = 0;
        byte[] stunRequest = new byte[20];
        stunRequest[0] = 0x00; stunRequest[1] = 0x01;
        stunRequest[4] = 0x21; stunRequest[5] = 0x12; stunRequest[6] = 0xA4; stunRequest[7] = 0x42;
        Random rand = new Random();
        for (int i = 8; i < 20; i++) stunRequest[i] = (byte)rand.Next(0, 256);

        using (TcpClient client = new TcpClient())
        {
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            var result = client.BeginConnect(stunServer, 3478, null, null);
            if (!result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(3))) throw new TimeoutException();
            client.EndConnect(result);

            localPort = ((IPEndPoint)client.Client.LocalEndPoint).Port;

            using (NetworkStream stream = client.GetStream())
            {
                stream.Write(stunRequest, 0, stunRequest.Length);
                byte[] response = new byte[512];
                int bytesRead = stream.Read(response, 0, response.Length);

                if (bytesRead < 20) return null;
                int payloadLen = (response[2] << 8) | response[3];
                int index = 20;

                while (index < 20 + payloadLen && index < bytesRead)
                {
                    int attrType = (response[index] << 8) | response[index + 1];
                    int attrLen = (response[index + 2] << 8) | response[index + 3];

                    if (attrType == 1 || attrType == 0x0020)
                    {
                        int port = (response[index + 6] << 8) | response[index + 7];
                        if (attrType == 0x0020) port ^= 0x2112;

                        byte[] ipBytes = new byte[4];
                        Array.Copy(response, index + 8, ipBytes, 0, 4);
                        if (attrType == 0x0020)
                        {
                            ipBytes[0] ^= 0x21; ipBytes[1] ^= 0x12;
                            ipBytes[2] ^= 0xA4; ipBytes[3] ^= 0x42;
                        }
                        return new IPEndPoint(new IPAddress(ipBytes), port);
                    }
                    index += 4 + attrLen;
                }
            }
        }
        return null;
    }

    private async Task StartWindowsKeepAlivePump(int boundPort, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using (Socket keepAliveSock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                {
                    keepAliveSock.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    keepAliveSock.Bind(new IPEndPoint(IPAddress.Any, boundPort));

                    IAsyncResult result = keepAliveSock.BeginConnect("www.baidu.com", 80, null, null);
                    if (result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2)))
                    {
                        keepAliveSock.EndConnect(result);
                        string httpReq = "HEAD /natter-keep-alive HTTP/1.1\r\nHost: www.baidu.com\r\nConnection: close\r\n\r\n";
                        keepAliveSock.Send(Encoding.ASCII.GetBytes(httpReq));
                    }
                }
            }
            catch { }
            await Task.Delay(15000, token);
        }
    }

    private byte[] BuildProxyProtocolV2Header(IPEndPoint src, IPEndPoint dst)
    {
        using (MemoryStream ms = new MemoryStream())
        {
            byte[] magic = { 0x0D, 0x0A, 0x0D, 0x0A, 0x00, 0x0D, 0x0A, 0x51, 0x55, 0x49, 0x54, 0x0A };
            ms.Write(magic, 0, magic.Length);
            ms.WriteByte(0x21);
            bool isIPv4 = src.AddressFamily == AddressFamily.InterNetwork;
            ms.WriteByte((byte)(isIPv4 ? 0x11 : 0x21));
            ushort addrLen = (ushort)(isIPv4 ? 12 : 36);
            ms.WriteByte((byte)(addrLen >> 8));
            ms.WriteByte((byte)(addrLen & 0xFF));

            byte[] srcIpBytes = src.Address.GetAddressBytes();
            byte[] dstIpBytes = dst.Address.GetAddressBytes();
            ms.Write(srcIpBytes, 0, srcIpBytes.Length);
            ms.Write(dstIpBytes, 0, dstIpBytes.Length);
            ms.WriteByte((byte)(src.Port >> 8)); ms.WriteByte((byte)(src.Port & 0xFF));
            ms.WriteByte((byte)(dst.Port >> 8)); ms.WriteByte((byte)(dst.Port & 0xFF));

            return ms.ToArray();
        }
    }

    public TunnelStats CollectStats()
    {
        long currentUp = Interlocked.Read(ref _totalUploadBytes);
        long currentDown = Interlocked.Read(ref _totalDownloadBytes);

        long speedUp = currentUp - _lastUploadBytes;
        long speedDown = currentDown - _lastDownloadBytes;

        _lastUploadBytes = currentUp;
        _lastDownloadBytes = currentDown;

        return new TunnelStats
        {
            Id = Config.Id,
            ActiveConnections = _activeConnections,
            SpeedUpload = speedUp,
            SpeedDownload = speedDown,
            TotalUpload = currentUp,
            TotalDownload = currentDown,
            OuterAddress = OuterEndPoint?.ToString(),
            IsRunning = IsRunning
        };
    }

    private void ResetStats()
    {
        _totalUploadBytes = 0; _totalDownloadBytes = 0;
        _lastUploadBytes = 0; _lastDownloadBytes = 0;
        _activeConnections = 0;
    }
}
