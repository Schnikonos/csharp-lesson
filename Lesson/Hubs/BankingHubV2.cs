using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Lesson.Hubs;

/// <summary>
/// Lesson 24-B — Extended hub with:
///   • [Authorize] on hub methods (individual method authorization)
///   • IUserIdProvider for user-to-connection mapping
///   • User-specific push via Clients.User(userId)
///   • Reconnection hint via OnConnectedAsync lifecycle override
///
/// Java parallel:
///   @MessageMapping + Spring Security @PreAuthorize  →  [Authorize]
///   SimpUserRegistry / UserDestinationResolver       →  IUserIdProvider
///   messagingTemplate.convertAndSendToUser(...)      →  Clients.User(userId)
/// </summary>
public class BankingHubV2 : Hub<IBankingClient>
{
    /// <summary>Subscribe to group; requires authenticated caller.</summary>
    [Authorize]
    public async Task Subscribe(int accountId) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, BankingHub.GroupName(accountId));

    /// <summary>Unsubscribe from group.</summary>
    [Authorize]
    public async Task Unsubscribe(int accountId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, BankingHub.GroupName(accountId));

    /// <summary>
    /// Lifecycle override — log reconnects and resend missed events in a real app.
    /// Java parallel: @EventListener(SessionConnectedEvent.class)
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        // In a real app: replay missed balance events for the authenticated user here.
        await base.OnConnectedAsync();
    }
}

/// <summary>
/// Lesson 24-B — Custom IUserIdProvider.
///
/// Maps an incoming connection to a stable user identity string (e.g. JWT sub claim).
/// Enables Clients.User(userId) targeted delivery.
///
/// Java parallel:
///   UserDestinationResolver + SimpUserRegistry in Spring WebSocket
/// </summary>
public class JwtUserIdProvider : IUserIdProvider
{
    // In a full JWT setup this would read the "sub" or "nameidentifier" claim.
    // For the lesson we use the NameIdentifier claim (set by Bearer auth middleware).
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
}
