using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MSLX.Plugin.Stun.Models;

public class TunnelConfig
{
    [Required(ErrorMessage = "隧道ID不能为空")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [Required(ErrorMessage = "隧道名称不能为空")]
    [StringLength(32, MinimumLength = 1, ErrorMessage = "隧道名称长度必须在 1 到 32 个字符之间")]
    public string Name { get; set; } = "新建 STUN 隧道";

    [Required(ErrorMessage = "本地目标 IP 不能为空")]
    [RegularExpression(@"^((25[0-5]|2[0-4]\d|[01]?\d\d?)\.){3}(25[0-5]|2[0-4]\d|[01]?\d\d?)$",
        ErrorMessage = "请输入有效的本地 IPv4 地址")]
    public string LocalIp { get; set; } = "127.0.0.1";

    [Required(ErrorMessage = "本地端口不能为空")]
    [Range(1, 65535, ErrorMessage = "端口范围必须在 1 到 65535 之间")]
    public int LocalPort { get; set; } = 25565;

    public bool EnableProxyProtocolV2 { get; set; } = false;

    [Required(ErrorMessage = "最大并发连接数不能为空")]
    [Range(1, 1024, ErrorMessage = "最大并发连接数必须在 1 到 1024 之间")]
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