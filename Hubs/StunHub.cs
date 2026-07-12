using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;
using MSLX.Plugin.Stun.Managers;

namespace MSLX.Plugin.Stun.Hubs;

[Authorize]
public class StunHub : Hub
{
    private readonly StunTunnelManager _tunnelManager;

    public StunHub(StunTunnelManager tunnelManager)
    {
        _tunnelManager = tunnelManager;
    }

    private bool HasPermission(string tunnelId)
    {
        var user = Context.User;
        if (user == null) return false;

        var role = user.FindFirst(ClaimTypes.Role)?.Value
                   ?? user.FindFirst("Role")?.Value
                   ?? user.FindFirst("role")?.Value;

        if (role == "admin") return true;

        var userId = user.FindFirst("UserId")?.Value ?? "";
        if (string.IsNullOrEmpty(userId)) return false;

        return false;
    }

    public async Task JoinGroup(string tunnelId)
    {
        if (!HasPermission(tunnelId))
        {
            throw new HubException("无权查看该 STUN 隧道或资源不存在");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, tunnelId);

        var historyLogs = _tunnelManager.GetHistoryLogs(tunnelId);

        if (historyLogs.Any())
        {
            foreach (var log in historyLogs)
            {
                await Clients.Caller.SendAsync("ReceiveLog", log);
            }
        }
    }

    public async Task LeaveGroup(string tunnelId)
    {
        if (!HasPermission(tunnelId))
        {
            throw new HubException("无权操作该资源");
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, tunnelId);
    }
}