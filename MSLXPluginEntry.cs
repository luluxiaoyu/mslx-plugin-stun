using MSLX.SDK;
using MSLX.SDK.Interfaces;
using Newtonsoft.Json.Linq;

namespace MSLX.Plugin.Demo;

public class MSLXPluginEntry : IPlugin
{
    public static MSLXPluginEntry Instance { get; private set; }
    public string Id => "mslx-plugin-stun"; 
    public string Name => "STUN 隧道";
    public string Description => "利用STUN技术，在NAT1环境下获取一个公网端口用于游戏/联机。";
    public string Version => "1.0.0";
    public string Icon => "icon.png";
    public string MinSDKVersion => "1.4.3";
    public string Developer => "luluxiaoyu";
    public string AuthorUrl => "https://github.com/luluxiaoyu";
    public string PluginUrl => "https://github.com/luluxiaoyu/mslx-plugin-stun";

    public async void OnLoad()
    {
        Instance = this;
        
        SDK.MSLX.Logger.Info("mslx-plugin-demo 载入成功~");
        SDK.MSLX.Logger.Info("当前存在实例数量：" + SDK.MSLX.Config.Servers.GetServerList().Count.ToString());
        
        // ===== 配置读写示例 =====
        string dataDir = MSLXPluginEntry.Instance.Config().GetDataPath();
        SDK.MSLX.Logger.Info("使用的数据目录："+dataDir);
        
        MSLXPluginEntry.Instance.Config().WriteConfigKey("author", "xiaoyu");
        MSLXPluginEntry.Instance.Config().WriteConfigKey("magicNumber", 1027);
        
        int count = (int?)MSLXPluginEntry.Instance.Config().ReadConfigKey("magicNumber") ?? 0;
        SDK.MSLX.Logger.Info("从配置文件读取magicNumber：" + count.ToString());
        
        var allConfig = MSLXPluginEntry.Instance.Config().ReadConfig();
        
        
        // get请求示例
        var response = await SDK.MSLX.Http.GetAsync("https://api.mslmc.cn/v3/query/notice?query=id");
        
        if (response.IsSuccessStatusCode)
        {
            JObject jobj = JObject.Parse(response.Content ?? "{}");
            string content = jobj["data"]?["noticeID"]?.ToString() ?? "";
            
            SDK.MSLX.Logger.Info($"获取到的MSL公告编号: {content}");
        }

        // post
        /***
        var postResponse = await SDK.MSLX.Http.PostAsync(
            "https://example.cn/post-api",
            PluginHttpContentType.Json,
            new { username = "admin", action = "start" }
        ); ***/
        
        
        // ===== 下载器调用示例 ===== 
        SDK.MSLX.Logger.Info("准备下载文件...");
        string targetPath = Path.Combine(MSLXPluginEntry.Instance.Config().GetDataPath(), "server.jar");
        if (File.Exists(targetPath))
        {
            return;
        }

        var result = await SDK.MSLX.Downloader.DownloadFileAsync(
            "https://bmclapi2.bangbang93.com/forge/download?mcversion=26.1.2&version=64.0.8&category=installer&format=jar", 
            targetPath,
            (progress, speed) => 
            {
                SDK.MSLX.Logger.Debug($"\r下载中: {progress:0.0}% [{speed}]"); 
            });

        if (result.Success)
        {
            SDK.MSLX.Logger.Info("下载完成，可以开始搞事情了！");
        }
        else
        {
            SDK.MSLX.Logger.Error($"下载失败: {result.ErrorMessage}");
        }
    }

    public void OnUnload() {
        SDK.MSLX.Logger.Info("mslx-plugin-demo 卸载成功~");
    }
}