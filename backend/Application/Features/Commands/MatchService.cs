using Game.Application.Abstractions;
using Game.Application.Common;
using Game.Domain.Entities;

namespace Game.Application.Features.Commands;

public sealed class MatchService(IGameMatchRepository repository, TimeProvider timeProvider)
{
    public async Task<PlayerSession> CreateAsync(
        CreateMatchCommand command,
        CancellationToken cancellationToken)
    {
        var (match, host) = GameMatch.Create(command.MatchName, command.PlayerName, timeProvider.GetUtcNow());

        await repository.AddAsync(match, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return new PlayerSession(host.Id, SnapshotFor(match, host.Id));
    }

    public async Task<PlayerSession> JoinAsync(
        JoinMatchCommand command,
        CancellationToken cancellationToken)
    {
        var match = await GetMatchAsync(command.MatchId, cancellationToken);
        var player = match.Join(command.PlayerName, timeProvider.GetUtcNow());

        await repository.SaveChangesAsync(cancellationToken);

        return new PlayerSession(player.Id, SnapshotFor(match, player.Id));
    }

    public async Task<MatchSnapshot> MoveAsync(
        MoveUnitCommand command,
        CancellationToken cancellationToken)
    {
        var match = await GetMatchAsync(command.MatchId, cancellationToken);

        match.MoveUnit(
            command.PlayerId,
            command.UnitId,
            command.TargetColumn,
            command.TargetRow,
            command.ExpectedVersion,
            timeProvider.GetUtcNow());

        await repository.SaveChangesAsync(cancellationToken);

        return SnapshotFor(match, command.PlayerId);
    }

    public async Task<MatchSnapshot> PlaceUnitAsync(
        PlaceUnitCommand command,
        CancellationToken cancellationToken)
    {
        var match = await GetMatchAsync(command.MatchId, cancellationToken);

        match.PlaceUnit(
            command.PlayerId,
            command.UnitId,
            command.TargetColumn,
            command.TargetRow,
            command.ExpectedVersion);

        await repository.SaveChangesAsync(cancellationToken);
        return SnapshotFor(match, command.PlayerId);
    }

    public async Task<MatchSnapshot> ReadyPlayerAsync(
        ReadyPlayerCommand command,
        CancellationToken cancellationToken)
    {
        var match = await GetMatchAsync(command.MatchId, cancellationToken);
        match.ReadyPlayer(command.PlayerId, command.ExpectedVersion, timeProvider.GetUtcNow());

        await repository.SaveChangesAsync(cancellationToken);
        return SnapshotFor(match, command.PlayerId);
    }

    public async Task<MatchSnapshot> AttackAsync(
        AttackUnitCommand command,
        CancellationToken cancellationToken)
    {
        var match = await GetMatchAsync(command.MatchId, cancellationToken);

        match.AttackUnit(
            command.PlayerId,
            command.AttackerUnitId,
            command.TargetUnitId,
            command.ExpectedVersion,
            timeProvider.GetUtcNow());

        await repository.SaveChangesAsync(cancellationToken);

        return SnapshotFor(match, command.PlayerId);
    }

    public async Task<MatchSnapshot> EndTurnAsync(
        EndTurnCommand command,
        CancellationToken cancellationToken)
    {
        var match = await GetMatchAsync(command.MatchId, cancellationToken);
        match.EndTurn(command.PlayerId, command.ExpectedVersion, timeProvider.GetUtcNow());

        await repository.SaveChangesAsync(cancellationToken);

        return SnapshotFor(match, command.PlayerId);
    }

    public async Task<MatchSnapshot> RequestRematchAsync(
        RequestRematchCommand command,
        CancellationToken cancellationToken)
    {
        var match = await GetMatchAsync(command.MatchId, cancellationToken);
        match.RequestRematch(command.PlayerId, command.ExpectedVersion);

        await repository.SaveChangesAsync(cancellationToken);
        return SnapshotFor(match, command.PlayerId);
    }

    public async Task<RematchAcceptedResult> AcceptRematchAsync(
        AcceptRematchCommand command,
        CancellationToken cancellationToken)
    {
        var match = await GetMatchAsync(command.MatchId, cancellationToken);
        var originalPlayer = match.Players.Single(player => player.Id == command.PlayerId);
        var rematch = match.AcceptRematch(
            command.PlayerId,
            command.ExpectedVersion,
            timeProvider.GetUtcNow());
        var rematchPlayer = rematch.Players.Single(player =>
            string.Equals(player.Name, originalPlayer.Name, StringComparison.Ordinal));

        await repository.AddAsync(rematch, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return new RematchAcceptedResult(
            SnapshotFor(match, command.PlayerId),
            new PlayerSession(rematchPlayer.Id, SnapshotFor(rematch, rematchPlayer.Id)));
    }

    public async Task<PlayerSession> EnterRematchAsync(
        EnterRematchCommand command,
        CancellationToken cancellationToken)
    {
        var originalMatch = await GetMatchAsync(command.MatchId, cancellationToken);
        var originalPlayer = originalMatch.Players.Single(player => player.Id == command.PlayerId);
        var rematchId = originalMatch.RematchMatchId
            ?? throw new InvalidOperationException("The rematch has not been created yet.");
        var rematch = await GetMatchAsync(rematchId, cancellationToken);
        var rematchPlayer = rematch.Players.Single(player =>
            string.Equals(player.Name, originalPlayer.Name, StringComparison.Ordinal));

        return new PlayerSession(rematchPlayer.Id, SnapshotFor(rematch, rematchPlayer.Id));
    }

    public async Task<MatchSnapshot> GetAsync(
        Guid matchId,
        Guid viewerPlayerId,
        CancellationToken cancellationToken)
    {
        var match = await GetMatchAsync(matchId, cancellationToken);
        return SnapshotFor(match, viewerPlayerId);
    }

    public async Task<IReadOnlyList<PlayerMatchView>> GetPlayerViewsAsync(
        Guid matchId,
        CancellationToken cancellationToken)
    {
        var match = await GetMatchAsync(matchId, cancellationToken);

        return match.Players
            .Select(player => new PlayerMatchView(player.Id, SnapshotFor(match, player.Id)))
            .ToList();
    }

    public async Task<bool> ExpireTurnAsync(Guid matchId, CancellationToken cancellationToken)
    {
        var match = await GetMatchAsync(matchId, cancellationToken);
        if (!match.TryExpireTurn(timeProvider.GetUtcNow())) return false;

        await repository.SaveChangesAsync(cancellationToken);
        return true;
    }

    private MatchSnapshot SnapshotFor(GameMatch match, Guid playerId) =>
        MatchSnapshot.From(match, playerId, timeProvider.GetUtcNow());

    private async Task<GameMatch> GetMatchAsync(Guid matchId, CancellationToken cancellationToken) =>
        await repository.GetAsync(matchId, cancellationToken)
        ?? throw new KeyNotFoundException($"Match '{matchId}' was not found.");
}

public sealed record RematchAcceptedResult(
    MatchSnapshot OriginalMatch,
    PlayerSession Session);

public sealed record PlayerMatchView(Guid PlayerId, MatchSnapshot Match);
