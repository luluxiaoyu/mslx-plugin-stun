using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSLX.Plugin.Stun.Managers;
using MSLX.Plugin.Stun.Models;
using MSLX.SDK.Models;

namespace MSLX.Plugin.Stun.Controllers;

[ApiController]
[Authorize(Roles = "admin")]
[Route("api/plugins/mslx-plugin-stun/tunnels")]
public class StunController : ControllerBase
{
    private readonly StunTunnelManager _tunnelManager;

    public StunController(StunTunnelManager tunnelManager)
    {
        _tunnelManager = tunnelManager;
    }

    [HttpGet("list")]
    public IActionResult GetAllTunnels()
    {
        var configs = _tunnelManager.GetConfigs();
        var result = configs.Select(c => new
        {
            Config = c,
            Stats = _tunnelManager.GetStats(c.Id)
        });

        return Ok(new ApiResponse<object> { Code = 200, Message = "获取成功", Data = result });
    }

    [HttpPost("create")]
    public IActionResult CreateTunnel([FromBody] TunnelConfig config)
    {
        if (string.IsNullOrEmpty(config.Id)) config.Id = Guid.NewGuid().ToString("N");
        _tunnelManager.AddTunnel(config);
        return Ok(new ApiResponse<object> { Code = 200, Message = "创建成功", Data = config });
    }

    [HttpPost("update")]
    public IActionResult UpdateTunnel([FromBody] TunnelConfig config)
    {
        if (string.IsNullOrEmpty(config.Id))
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = "更新失败：缺少隧道ID" });
        }

        _tunnelManager.UpdateTunnel(config);
        return Ok(new ApiResponse<object> { Code = 200, Message = "更新成功" });
    }

    [HttpPost("delete")]
    public IActionResult DeleteTunnel([FromQuery] string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = "删除失败：缺少隧道ID" });
        }

        _tunnelManager.RemoveTunnel(id);
        return Ok(new ApiResponse<object> { Code = 200, Message = "删除成功" });
    }

    [HttpPost("start")]
    public async Task<IActionResult> StartTunnel([FromQuery] string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = "启动失败：缺少隧道ID" });
        }

        try
        {
            SDK.MSLX.Logger.Info($"[STUN] 正在启动隧道 {id}...");

            await _tunnelManager.StartTunnel(id);

            SDK.MSLX.Logger.Info($"[STUN] 隧道 {id} 启动成功并已获取最新公网状态。");
        }
        catch (Exception ex)
        {
            SDK.MSLX.Logger.Error($"[STUN] 启动隧道 {id} 异常: {ex.Message}");
            return StatusCode(500, new ApiResponse<object> { Code = 500, Message = $"启动异常: {ex.Message}" });
        }

        return Ok(new ApiResponse<object> { Code = 200, Message = "启动成功" });
    }

    [HttpPost("stop")]
    public IActionResult StopTunnel([FromQuery] string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = "停止失败：缺少隧道ID" });
        }
        SDK.MSLX.Logger.Info($"[STUN] 正在关闭隧道 {id}...");
        _tunnelManager.StopTunnel(id);
        return Ok(new ApiResponse<object> { Code = 200, Message = "已停止" });
    }
}