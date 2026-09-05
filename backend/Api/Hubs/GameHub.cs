using Game.Api.Contracts;
using Game.Api.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Game.Api.Hubs;

public sealed class GameHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync(
            GameHubEvents.ServerReady,
            new ServerReadyMessage(Context.ConnectionId, DateTimeOffset.UtcNow, "signalr"),
            Context.ConnectionAborted);

        await base.OnConnectedAsync();
    }

    [Authorize]
    public async Task JoinMatch(Guid matchId)
    {
        EnsureValidMatchId(matchId);
        EnsurePlayerCanJoin(matchId);

        var groupName = GetGroupName(matchId);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName, Context.ConnectionAborted);
        await Clients.Group(groupName).SendAsync(
            GameHubEvents.PlayerJoined,
            new MatchPresenceMessage(matchId, Context.ConnectionId, DateTimeOffset.UtcNow),
            Context.ConnectionAborted);
    }

    [Authorize]
    public async Task LeaveMatch(Guid matchId)
    {
        EnsureValidMatchId(matchId);
        EnsurePlayerCanJoin(matchId);

        var groupName = GetGroupName(matchId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName, Context.ConnectionAborted);
        await Clients.Group(groupName).SendAsync(
            GameHubEvents.PlayerLeft,
            new MatchPresenceMessage(matchId, Context.ConnectionId, DateTimeOffset.UtcNow),
            Context.ConnectionAborted);
    }

    public static string GetGroupName(Guid matchId) => $"match:{matchId:N}";

    private static void EnsureValidMatchId(Guid matchId)
    {
        if (matchId == Guid.Empty)
        {
            throw new HubException("A valid match id is required.");
        }
    }

    private void EnsurePlayerCanJoin(Guid matchId)
    {
        if (Context.User is null
            || !Context.User.TryGetPlayerSession(out var identity)
            || identity.MatchId != matchId)
        {
            throw new HubException("The player session does not belong to this match.");
        }
    }
}
