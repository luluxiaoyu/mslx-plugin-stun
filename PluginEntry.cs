using MSLX.SDK;

namespace MSLX.Plugin.Demo;

public class PluginEntry : IPlugin
{
    public string Id => "mslx-plugin-demo"; 
    public string Name => "MSLX 官方示例插件";
    public string Description => "MSLX 官方示例插件";
    public string Version => "1.0.1";
    public string MinLoaderVersion => "1.3.8";
    public string Developer => "xiaoyu";
    public string AuthorUrl => "https://github.com/MSLTeam";
    public string PluginUrl => "https://github.com/MSLTeam/mslx-plugin-demo";
}