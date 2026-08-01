using Game.Application.Common;
using Game.Domain.Entities;

namespace Game.Application.Abstractions;

public interface IGameMatchRepository
{
    Task<IReadOnlyList<GameMatchSummary>> ListAsync(CancellationToken cancellationToken);

    Task<GameMatch?> GetAsync(Guid matchId, CancellationToken cancellationToken);

    Task AddAsync(GameMatch match, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
