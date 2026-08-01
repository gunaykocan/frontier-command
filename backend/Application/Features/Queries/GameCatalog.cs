using Game.Application.Abstractions;
using Game.Application.Common;

namespace Game.Application.Features.Queries;

public sealed class GameCatalog(IGameMatchRepository repository)
{
    public Task<IReadOnlyList<GameMatchSummary>> ListAsync(CancellationToken cancellationToken) =>
        repository.ListAsync(cancellationToken);
}
