using Game.Api.Authentication;
using Game.Api.Contracts;
using Game.Api.Hubs;
using Game.Application.Common;
using Game.Application.Features.Commands;
using Game.Application.Features.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Game.Api.Controllers;

[ApiController]
[Route("api/matches")]
public sealed class MatchesController(
    GameCatalog catalog,
    MatchService matchService,
    PlayerSessionCookieService cookieService,
    MatchUpdatePublisher matchPublisher) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<GameMatchSummary>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<GameMatchSummary>>> List(CancellationToken cancellationToken)
    {
        var matches = await catalog.ListAsync(cancellationToken);
        return Ok(matches);
    }

    [HttpGet("{matchId:guid}")]
    [Authorize]
    [ProducesResponseType<MatchSnapshot>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MatchSnapshot>> Get(
        Guid matchId,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetPlayerSession(out var identity) || identity.MatchId != matchId)
        {
            return Forbid();
        }

        var match = await matchService.GetAsync(matchId, identity.PlayerId, cancellationToken);
        return Ok(match);
    }

    [HttpPost]
    [ProducesResponseType<PlayerSession>(StatusCodes.Status201Created)]
    public async Task<ActionResult<PlayerSession>> Create(
        CreateMatchRequest request,
        CancellationToken cancellationToken)
    {
        var session = await matchService.CreateAsync(
            new CreateMatchCommand(request.MatchName, request.PlayerName, request.PlayAgainstBot),
            cancellationToken);

        cookieService.Issue(Response, Request.IsHttps, session.PlayerId, session.Match.Id);
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

        cookieService.Issue(Response, Request.IsHttps, session.PlayerId, session.Match.Id);
        await BroadcastMatchAsync(session.Match.Id, cancellationToken);
        return Ok(session);
    }

    [Authorize]
    [HttpPost("{matchId:guid}/deployment/placements")]
    [ProducesResponseType<MatchSnapshot>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MatchSnapshot>> PlaceUnit(
        Guid matchId,
        PlaceUnitRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetPlayerSession(out var identity) || identity.MatchId != matchId)
        {
            return Forbid();
        }

        var match = await matchService.PlaceUnitAsync(
            new PlaceUnitCommand(
                matchId,
                identity.PlayerId,
                request.UnitId,
                request.TargetColumn,
                request.TargetRow,
                request.ExpectedVersion),
            cancellationToken);

        await BroadcastMatchAsync(match.Id, cancellationToken);
        return Ok(match);
    }

    [Authorize]
    [HttpPost("{matchId:guid}/deployment/ready")]
    [ProducesResponseType<MatchSnapshot>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MatchSnapshot>> ReadyPlayer(
        Guid matchId,
        ReadyPlayerRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetPlayerSession(out var identity) || identity.MatchId != matchId)
        {
            return Forbid();
        }

        var match = await matchService.ReadyPlayerAsync(
            new ReadyPlayerCommand(matchId, identity.PlayerId, request.ExpectedVersion),
            cancellationToken);

        await BroadcastMatchAsync(match.Id, cancellationToken);
        return Ok(match);
    }

    [Authorize]
    [HttpPost("{matchId:guid}/moves")]
    [ProducesResponseType<MatchSnapshot>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MatchSnapshot>> Move(
        Guid matchId,
        MoveUnitRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetPlayerSession(out var identity) || identity.MatchId != matchId)
        {
            return Forbid();
        }

        var match = await matchService.MoveAsync(
            new MoveUnitCommand(
                matchId,
                identity.PlayerId,
                request.UnitId,
                request.TargetColumn,
                request.TargetRow,
                request.ExpectedVersion),
            cancellationToken);

        await BroadcastMatchAsync(match.Id, cancellationToken);
        return Ok(match);
    }

    [Authorize]
    [HttpPost("{matchId:guid}/attacks")]
    [ProducesResponseType<MatchSnapshot>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MatchSnapshot>> Attack(
        Guid matchId,
        AttackUnitRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetPlayerSession(out var identity) || identity.MatchId != matchId)
        {
            return Forbid();
        }

        var match = await matchService.AttackAsync(
            new AttackUnitCommand(
                matchId,
                identity.PlayerId,
                request.AttackerUnitId,
                request.TargetUnitId,
                request.ExpectedVersion),
            cancellationToken);

        await BroadcastMatchAsync(match.Id, cancellationToken);
        return Ok(match);
    }

    [Authorize]
    [HttpPost("{matchId:guid}/turns/end")]
    [ProducesResponseType<MatchSnapshot>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MatchSnapshot>> EndTurn(
        Guid matchId,
        EndTurnRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetPlayerSession(out var identity) || identity.MatchId != matchId)
        {
            return Forbid();
        }

        var match = await matchService.EndTurnAsync(
            new EndTurnCommand(matchId, identity.PlayerId, request.ExpectedVersion),
            cancellationToken);

        await BroadcastMatchAsync(match.Id, cancellationToken);
        return Ok(match);
    }

    [Authorize]
    [HttpPost("{matchId:guid}/rematch/request")]
    [ProducesResponseType<MatchSnapshot>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MatchSnapshot>> RequestRematch(
        Guid matchId,
        RematchRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetPlayerSession(out var identity) || identity.MatchId != matchId)
        {
            return Forbid();
        }

        var match = await matchService.RequestRematchAsync(
            new RequestRematchCommand(matchId, identity.PlayerId, request.ExpectedVersion),
            cancellationToken);

        await BroadcastMatchAsync(match.Id, cancellationToken);
        return Ok(match);
    }

    [Authorize]
    [HttpPost("{matchId:guid}/rematch/accept")]
    [ProducesResponseType<PlayerSession>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PlayerSession>> AcceptRematch(
        Guid matchId,
        RematchRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetPlayerSession(out var identity) || identity.MatchId != matchId)
        {
            return Forbid();
        }

        var result = await matchService.AcceptRematchAsync(
            new AcceptRematchCommand(matchId, identity.PlayerId, request.ExpectedVersion),
            cancellationToken);

        cookieService.Issue(
            Response,
            Request.IsHttps,
            result.Session.PlayerId,
            result.Session.Match.Id);
        await BroadcastMatchAsync(result.OriginalMatch.Id, cancellationToken);
        return Ok(result.Session);
    }

    [Authorize]
    [HttpPost("{matchId:guid}/rematch/enter")]
    [ProducesResponseType<PlayerSession>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PlayerSession>> EnterRematch(
        Guid matchId,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetPlayerSession(out var identity) || identity.MatchId != matchId)
        {
            return Forbid();
        }

        var session = await matchService.EnterRematchAsync(
            new EnterRematchCommand(matchId, identity.PlayerId),
            cancellationToken);
        cookieService.Issue(Response, Request.IsHttps, session.PlayerId, session.Match.Id);
        return Ok(session);
    }

    private Task BroadcastMatchAsync(Guid matchId, CancellationToken cancellationToken) =>
        matchPublisher.PublishAsync(matchId, cancellationToken);
}
