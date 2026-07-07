using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using MSLX.SDK.Models;
using MSLX.Plugin.Stun.Models;
using MSLX.Plugin.Stun.Managers;
using MSLX.Plugin.Stun.Hubs;

namespace MSLX.Plugin.Stun.Controllers;

[ApiController]
[Route("api/plugins/mslx-plugin-stun/tunnels")]
public class StunController : ControllerBase
{
    public StunController(IHubContext<StunHub> hubContext)
    {
        // 将 Hub 注入管理器中以实现后台推送
        StunTunnelManager.Instance.SetHubContext(hubContext);
    }

    [HttpGet("list")]
    public IActionResult GetAllTunnels()
    {
        var configs = StunTunnelManager.Instance.GetConfigs();
        var result = configs.Select(c => new
        {
            Config = c,
            Stats = StunTunnelManager.Instance.GetStats(c.Id)
        });

        return Ok(new ApiResponse<object>
        {
            Code = 200,
            Message = "获取成功",
            Data = result
        });
    }

    [HttpPost("create")]
    public IActionResult CreateTunnel([FromBody] TunnelConfig config)
    {
        if (string.IsNullOrEmpty(config.Id)) config.Id = Guid.NewGuid().ToString("N");
        StunTunnelManager.Instance.AddTunnel(config);
        return Ok(new ApiResponse<object> { Code = 200, Message = "创建成功", Data = config });
    }

    [HttpPost("update")]
    public IActionResult UpdateTunnel([FromBody] TunnelConfig config)
    {
        if (string.IsNullOrEmpty(config.Id))
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = "更新失败：缺少隧道ID" });
        }

        StunTunnelManager.Instance.UpdateTunnel(config);
        return Ok(new ApiResponse<object> { Code = 200, Message = "更新成功" });
    }

    [HttpPost("delete")]
    public IActionResult DeleteTunnel([FromQuery] string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = "删除失败：缺少隧道ID" });
        }

        StunTunnelManager.Instance.RemoveTunnel(id);
        return Ok(new ApiResponse<object> { Code = 200, Message = "删除成功" });
    }

    [HttpPost("start")]
    public IActionResult StartTunnel([FromQuery] string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = "启动失败：缺少隧道ID" });
        }

        _ = Task.Run(async () =>
        {
            try
            {
                SDK.MSLX.Logger.Info($"[STUN] 正在启动隧道 {id}...");
                await StunTunnelManager.Instance.StartTunnel(id);
            }
            catch (Exception ex)
            {
                SDK.MSLX.Logger.Error($"[STUN] 后台启动隧道 {id} 异常: {ex.Message}");
            }
        });

        return Ok(new ApiResponse<object> { Code = 200, Message = "启动指令已下发" });
    }

    [HttpPost("stop")]
    public IActionResult StopTunnel([FromQuery] string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = "停止失败：缺少隧道ID" });
        }

        StunTunnelManager.Instance.StopTunnel(id);
        return Ok(new ApiResponse<object> { Code = 200, Message = "已停止" });
    }
}