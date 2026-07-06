using System.Text.Json.Serialization;

namespace MSLX.Plugin.Stun.Models;

public class TunnelConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新建 STUN 隧道";
    public string LocalIp { get; set; } = "127.0.0.1";
    public int LocalPort { get; set; } = 25565;
    public bool EnableProxyProtocolV2 { get; set; } = false;
    public int MaxConnections { get; set; } = 128;
}

public class TunnelStats
{
    public string Id { get; set; } = string.Empty;
    public int ActiveConnections { get; set; }
    public long SpeedUpload { get; set; }     // Bytes/s
    public long SpeedDownload { get; set; }   // Bytes/s
    public long TotalUpload { get; set; }     // Bytes
    public long TotalDownload { get; set; }   // Bytes
    public string? OuterAddress { get; set; }
    public bool IsRunning { get; set; }
}