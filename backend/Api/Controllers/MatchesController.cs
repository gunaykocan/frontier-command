using Game.Api.Contracts;
using Game.Api.Hubs;
using Game.Application.Common;
using Game.Application.Features.Commands;
using Game.Application.Features.Queries;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace Game.Api.Controllers;

[ApiController]
[Route("api/matches")]
public sealed class MatchesController(
    GameCatalog catalog,
    MatchService matchService,
    IHubContext<GameHub> hubContext) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<GameMatchSummary>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<GameMatchSummary>>> List(CancellationToken cancellationToken)
    {
        var matches = await catalog.ListAsync(cancellationToken);
        return Ok(matches);
    }

    [HttpGet("{matchId:guid}")]
    [ProducesResponseType<MatchSnapshot>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MatchSnapshot>> Get(
        Guid matchId,
        CancellationToken cancellationToken)
    {
        var match = await matchService.GetAsync(matchId, cancellationToken);
        return Ok(match);
    }

    [HttpPost]
    [ProducesResponseType<PlayerSession>(StatusCodes.Status201Created)]
    public async Task<ActionResult<PlayerSession>> Create(
        CreateMatchRequest request,
        CancellationToken cancellationToken)
    {
        var session = await matchService.CreateAsync(
            new CreateMatchCommand(request.MatchName, request.PlayerName),
            cancellationToken);

        return CreatedAtAction(nameof(Get), new { matchId = session.Match.Id }, session);
    }

    [HttpPost("{matchId:guid}/players")]
    [ProducesResponseType<PlayerSession>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PlayerSession>> Join(
        Guid matchId,
        JoinMatchRequest request,
        CancellationToken cancellationToken)
    {
        var session = await matchService.JoinAsync(
            new JoinMatchCommand(matchId, request.PlayerName),
            cancellationToken);

        await BroadcastMatchAsync(session.Match, cancellationToken);
        return Ok(session);
    }

    [HttpPost("{matchId:guid}/moves")]
    [ProducesResponseType<MatchSnapshot>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MatchSnapshot>> Move(
        Guid matchId,
        MoveUnitRequest request,
        CancellationToken cancellationToken)
    {
        var match = await matchService.MoveAsync(
            new MoveUnitCommand(
                matchId,
                request.PlayerId,
                request.UnitId,
                request.TargetColumn,
                request.TargetRow,
                request.ExpectedVersion),
            cancellationToken);

        await BroadcastMatchAsync(match, cancellationToken);
        return Ok(match);
    }

    private Task BroadcastMatchAsync(MatchSnapshot match, CancellationToken cancellationToken) =>
        hubContext.Clients
            .Group(GameHub.GetGroupName(match.Id))
            .SendAsync(GameHubEvents.MatchUpdated, match, cancellationToken);
}
