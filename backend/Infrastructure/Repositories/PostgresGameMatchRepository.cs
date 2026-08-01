using Game.Application.Abstractions;
using Game.Application.Common;
using Game.Domain.Entities;
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
            .Include(match => match.Players)
            .Include(match => match.Units)
            .SingleOrDefaultAsync(match => match.Id == matchId, cancellationToken);

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
