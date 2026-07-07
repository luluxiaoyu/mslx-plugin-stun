using System.Text.Json;
using MSLX.Plugin.Stun.Models;
using MSLX.Plugin.Stun.Core;
using MSLX.Plugin.Stun.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace MSLX.Plugin.Stun.Managers;

public class StunTunnelManager
{
    public static StunTunnelManager Instance { get; } = new StunTunnelManager();

    private string _configFilePath = "";
    private readonly Dictionary<string, StunTunnel> _tunnels = new();
    private List<TunnelConfig> _configs = new();

    private Timer? _statsTimer;
    private IHubContext<StunHub>? _hubContext;

    private StunTunnelManager() { }

    public void Initialize(string dataDir)
    {
        _configFilePath = Path.Combine(dataDir, "tunnels.json");
        LoadConfigs();
    }

    // Controller 会在首次请求或注入时把 Hub 上下文传进来
    public void SetHubContext(IHubContext<StunHub> hubContext)
    {
        if (_hubContext == null)
        {
            _hubContext = hubContext;
            _statsTimer = new Timer(BroadcastStats, null, 1000, 1000);
        }
    }

    private async void BroadcastStats(object? state)
    {
        if (_hubContext == null) return;

        foreach (var tunnel in _tunnels.Values.ToList())
        {
            if (tunnel.IsRunning)
            {
                var stats = tunnel.CollectStats();
                await _hubContext.Clients.Group(tunnel.Config.Id).SendAsync("ReceiveStats", stats);
            }
        }
    }

    public IEnumerable<string> GetHistoryLogs(string tunnelId)
    {
        if (_tunnels.TryGetValue(tunnelId, out var tunnel))
        {
            return tunnel.GetLogHistory();
        }
        return Array.Empty<string>();
    }

    private void LogToSignalR(string tunnelId, string formattedLog)
    {
        SDK.MSLX.Logger.Debug(formattedLog);
        _hubContext?.Clients.Group(tunnelId).SendAsync("ReceiveLog", formattedLog);
    }

    public List<TunnelConfig> GetConfigs() => _configs.ToList();

    public TunnelStats? GetStats(string id)
    {
        return _tunnels.TryGetValue(id, out var t) ? t.CollectStats() : null;
    }

    public void AddTunnel(TunnelConfig config)
    {
        _configs.Add(config);
        _tunnels[config.Id] = new StunTunnel(config, LogToSignalR);
        SaveConfigs();
    }

    public void UpdateTunnel(TunnelConfig config)
    {
        var existing = _configs.FirstOrDefault(c => c.Id == config.Id);
        if (existing != null)
        {
            StopTunnel(config.Id);
            _configs.Remove(existing);
            _configs.Add(config);
            _tunnels[config.Id] = new StunTunnel(config, LogToSignalR);
            SaveConfigs();
        }
    }

    public void RemoveTunnel(string id)
    {
        StopTunnel(id);
        _configs.RemoveAll(c => c.Id == id);
        _tunnels.Remove(id);
        SaveConfigs();
    }

    public async Task StartTunnel(string id)
    {
        if (_tunnels.TryGetValue(id, out var tunnel))
        {
            await tunnel.StartAsync();
        }
    }

    public void StopTunnel(string id)
    {
        if (_tunnels.TryGetValue(id, out var tunnel))
        {
            tunnel.Stop();
        }
    }

    public void StopAll()
    {
        foreach (var t in _tunnels.Values) t.Stop();
        _statsTimer?.Dispose();
    }

    private void LoadConfigs()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                string json = File.ReadAllText(_configFilePath);
                _configs = JsonSerializer.Deserialize<List<TunnelConfig>>(json) ?? new List<TunnelConfig>();

                foreach (var cfg in _configs)
                {
                    _tunnels[cfg.Id] = new StunTunnel(cfg, LogToSignalR);
                }
            }
        }
        catch (Exception ex)
        {
            SDK.MSLX.Logger.Error($"[STUN] 读取配置文件失败: {ex.Message}");
        }
    }

    private void SaveConfigs()
    {
        try
        {
            string json = JsonSerializer.Serialize(_configs, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configFilePath, json);
        }
        catch (Exception ex)
        {
            SDK.MSLX.Logger.Error($"[STUN] 保存配置文件失败: {ex.Message}");
        }
    }
}
