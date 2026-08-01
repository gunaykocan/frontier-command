using Game.Application.Abstractions;
using Game.Application.Common;
using Game.Domain.Entities;

namespace Game.Application.Features.Commands;

public sealed class MatchService(IGameMatchRepository repository)
{
    public async Task<PlayerSession> CreateAsync(
        CreateMatchCommand command,
        CancellationToken cancellationToken)
    {
        var (match, host) = GameMatch.Create(command.MatchName, command.PlayerName, DateTimeOffset.UtcNow);

        await repository.AddAsync(match, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return new PlayerSession(host.Id, MatchSnapshot.From(match));
    }

    public async Task<PlayerSession> JoinAsync(
        JoinMatchCommand command,
        CancellationToken cancellationToken)
    {
        var match = await GetMatchAsync(command.MatchId, cancellationToken);
        var player = match.Join(command.PlayerName, DateTimeOffset.UtcNow);

        await repository.SaveChangesAsync(cancellationToken);

        return new PlayerSession(player.Id, MatchSnapshot.From(match));
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
            command.ExpectedVersion);

        await repository.SaveChangesAsync(cancellationToken);

        return MatchSnapshot.From(match);
    }

    public async Task<MatchSnapshot> GetAsync(Guid matchId, CancellationToken cancellationToken)
    {
        var match = await GetMatchAsync(matchId, cancellationToken);
        return MatchSnapshot.From(match);
    }

    private async Task<GameMatch> GetMatchAsync(Guid matchId, CancellationToken cancellationToken) =>
        await repository.GetAsync(matchId, cancellationToken)
        ?? throw new KeyNotFoundException($"Match '{matchId}' was not found.");
}
