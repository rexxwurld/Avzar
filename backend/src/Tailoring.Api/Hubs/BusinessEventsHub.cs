using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Tailoring.Api.Hubs;

/// <summary>Authenticated tenant-scoped realtime channel. Clients cannot choose arbitrary groups.</summary>
[Authorize]
public sealed class BusinessEventsHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var businessId = Context.User?.FindFirstValue("business_id");
        if (!Guid.TryParse(businessId, out var id))
        {
            Context.Abort();
            return;
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, "business:" + id.ToString("N"));
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(userId, out var uid)) await Groups.AddToGroupAsync(Context.ConnectionId, "user:" + uid.ToString("N"));
        await base.OnConnectedAsync();
    }
}
