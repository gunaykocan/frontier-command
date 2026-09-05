using Game.Application.Features.Commands;
using Microsoft.AspNetCore.SignalR;

namespace Game.Api.Hubs;

public sealed class MatchUpdatePublisher(MatchService matchService, IHubContext<GameHub> hubContext)
{
    public async Task PublishAsync(Guid matchId, CancellationToken cancellationToken)
    {
        var views = await matchService.GetPlayerViewsAsync(matchId, cancellationToken);
        await Task.WhenAll(views.Select(view =>
            hubContext.Clients
                .User(view.PlayerId.ToString("D"))
                .SendAsync(GameHubEvents.MatchUpdated, view.Match, cancellationToken)));
    }
}
