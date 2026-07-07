using MSLX.Plugin.Stun.Hubs;
using MSLX.Plugin.Stun.Managers;
using MSLX.SDK;
using MSLX.SDK.Interfaces;

namespace MSLX.Plugin.Stun;

public class MSLXPluginEntry : IPlugin
{
    public static MSLXPluginEntry Instance { get; private set; }
    public string Id => "mslx-plugin-stun";
    public string Name => "STUN 隧道";
    public string Description => "利用 STUN 技术，在 NAT1 环境下获取公网端口，支持多开与流量监控。";
    public string Version => "1.0.1";
    public string Icon => "icon.png";
    public string MinSDKVersion => "1.4.9";
    public string Developer => "xiaoyu";
    public string AuthorUrl => "https://github.com/luluxiaoyu";
    public string PluginUrl => "https://github.com/luluxiaoyu/mslx-plugin-stun";

    public void OnLoad()
    {
        Instance = this;
        SDK.MSLX.Logger.Info("[STUN] 隧道插件开始初始化...");

        // 初始化数据目录和配置
        string dataDir = this.Config().GetDataPath();
        if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);

        StunTunnelManager.Instance.Initialize(dataDir);
        SDK.MSLX.Logger.Info($"[STUN] 插件载入成功，当前已加载 {StunTunnelManager.Instance.GetConfigs().Count} 个隧道配置。");
    }

    public void OnUnload()
    {
        StunTunnelManager.Instance.StopAll();
        SDK.MSLX.Logger.Info("[STUN] 插件卸载，所有隧道已释放。");
    }

    public void OnRegisterEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHub<StunHub>("/api/hubs/plugins/mslx-plugin-stun/stun");
    }
}
