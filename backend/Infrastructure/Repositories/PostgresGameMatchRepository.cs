using Game.Application.Abstractions;
using Game.Application.Common;
using Game.Domain.Entities;
using Game.Domain.Enums;
using Game.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Game.Infrastructure.Repositories;

public sealed class PostgresGameMatchRepository(GameDbContext dbContext) : IGameMatchRepository
{
    public async Task<IReadOnlyList<GameMatchSummary>> ListAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Matches
            .AsNoTracking()
            .OrderByDescending(match => match.CreatedAtUtc)
            .Select(match => new GameMatchSummary(
                match.Id,
                match.Name,
                match.Status,
                match.TurnNumber,
                match.Version,
                match.Players.Count,
                GameMatch.PlayerCapacity,
                match.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public Task<GameMatch?> GetAsync(Guid matchId, CancellationToken cancellationToken) =>
        dbContext.Matches
            .AsSplitQuery()
            .Include(match => match.Players)
            .Include(match => match.Units)
            .Include(match => match.Events)
            .Include(match => match.SpecialTiles)
            .Include(match => match.EnemySightings)
            .SingleOrDefaultAsync(match => match.Id == matchId, cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListExpiredTurnIdsAsync(
        DateTimeOffset now, int limit, CancellationToken cancellationToken) =>
        await dbContext.Matches
            .AsNoTracking()
            .Where(match => match.Status == MatchStatus.InProgress && match.TurnExpiresAtUtc <= now)
            .OrderBy(match => match.TurnExpiresAtUtc)
            .Select(match => match.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(GameMatch match, CancellationToken cancellationToken)
    {
        await dbContext.Matches.AddAsync(match, cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new MatchConcurrencyException(
                "The match changed while the command was being processed. Refresh and try again.",
                exception);
        }
    }
}
