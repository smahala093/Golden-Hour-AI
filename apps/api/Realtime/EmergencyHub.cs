using System.Security.Claims;
using GoldenHour.Api.Application;
using GoldenHour.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace GoldenHour.Api.Realtime;

[Authorize]
public sealed class EmergencyHub(GoldenHourDbContext dbContext) : Hub
{
    public async Task JoinSession(Guid sessionId)
    {
        var userId = GetUserId();
        var permitted = await dbContext.EmergencySessions.AnyAsync(
            session => session.Id == sessionId
                && (session.OwnerId == userId || session.Participants.Any(participant => participant.UserId == userId)),
            Context.ConnectionAborted);
        if (!permitted)
        {
            throw new HubException("Emergency session access denied.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(sessionId), Context.ConnectionAborted);
        await Clients.Caller.SendAsync("AuthoritativeStateRequired", new { sessionId }, Context.ConnectionAborted);
    }

    public Task LeaveSession(Guid sessionId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(sessionId), Context.ConnectionAborted);

    internal static string GroupName(Guid sessionId) => $"emergency-session-{sessionId:N}";

    private Guid GetUserId()
    {
        var value = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : throw new HubException("Authentication required.");
    }
}

public sealed class SignalRNotifier(IHubContext<EmergencyHub> hubContext) : IRealtimeNotifier
{
    public Task NotifySessionAsync(Guid sessionId, string eventName, object payload, CancellationToken cancellationToken) =>
        hubContext.Clients.Group(EmergencyHub.GroupName(sessionId)).SendAsync(eventName, payload, cancellationToken);
}
