using Game.Api.Hubs;
using Game.Application.Abstractions;
using Game.Application.Common;
using Game.Application.Features.Commands;

namespace Game.Api.Services;

public sealed class TurnTimeoutWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<TurnTimeoutWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), timeProvider);

        try
        {
            do
            {
                try
                {
                    await ProcessExpiredTurnsAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Could not scan expired turns; retrying on the next tick.");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown; deadlines remain in the database for the next startup.
        }
    }

    public async Task ProcessExpiredTurnsAsync(CancellationToken cancellationToken)
    {
        await using var queryScope = scopeFactory.CreateAsyncScope();
        var repository = queryScope.ServiceProvider.GetRequiredService<IGameMatchRepository>();
        var matchIds = await repository.ListExpiredTurnIdsAsync(timeProvider.GetUtcNow(), 100, cancellationToken);

        foreach (var matchId in matchIds)
        {
            // Never reuse tracked entities after a concurrent player command or another worker wins.
            await using var scope = scopeFactory.CreateAsyncScope();
            try
            {
                var service = scope.ServiceProvider.GetRequiredService<MatchService>();
                if (await service.ExpireTurnAsync(matchId, cancellationToken))
                {
                    await scope.ServiceProvider.GetRequiredService<MatchUpdatePublisher>()
                        .PublishAsync(matchId, cancellationToken);
                }
            }
            catch (MatchConcurrencyException)
            {
                // A committed command won the version check. Re-read on the next scan.
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not finish expired turn for match {MatchId}.", matchId);
            }
        }
    }
}
