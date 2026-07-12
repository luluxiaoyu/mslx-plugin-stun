using MSLX.Plugin.Stun.Hubs;
using MSLX.Plugin.Stun.Managers;
using MSLX.SDK;

namespace MSLX.Plugin.Stun;

public class MSLXPluginEntry : IPlugin
{
    public static MSLXPluginEntry Instance { get; private set; } = null!;
    public string Id => "mslx-plugin-stun";
    public string Name => "STUN 隧道";
    public string Description => "利用 STUN 技术，在 NAT1 环境下获取公网端口，支持多开与流量监控。";
    public string Version => "1.0.2";
    public string Icon => "icon.png";
    public string MinSDKVersion => "1.5.2";
    public string Developer => "xiaoyu";
    public string AuthorUrl => "https://github.com/luluxiaoyu";
    public string PluginUrl => "https://mslx-plugins.mslmc.net/plugins/mslx-plugin-stun";

    public void OnPluginInitialize(IServiceProvider serviceProvider)
    {
        Instance = this;
        SDK.MSLX.Logger.Info("[STUN] 隧道插件开始初始化...");

        string dataDir = this.Config().GetDataPath();
        if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);

        var tunnelManager = serviceProvider.GetRequiredService<StunTunnelManager>();
        tunnelManager.Initialize(dataDir);

        SDK.MSLX.Logger.Info($"[STUN] 插件载入成功，当前已加载 {tunnelManager.GetConfigs().Count} 个隧道配置。");
    }

    /*
    public void OnUnload()
    {
    } */

    public void OnRegisterEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHub<StunHub>("/api/hubs/plugins/mslx-plugin-stun/stun");
    }

    public void OnRegisterServices(IServiceCollection services)
    {
        services.AddSingleton<StunTunnelManager>();
    }
}