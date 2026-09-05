using Game.Application.Abstractions;
using Game.Application.Common;
using Game.Application.Features.Bots;
using Game.Application.Features.Commands;
using Game.Domain.Entities;
using Game.Domain.Enums;

namespace Game.Api.Tests;

public sealed class BotMatchServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreatingBotMatchAddsAReadyAutomatedOpponent()
    {
        var repository = new InMemoryMatchRepository();
        var service = CreateService(repository);

        var session = await service.CreateAsync(
            new CreateMatchCommand("Tek oyunculu", "İnsan", PlayAgainstBot: true),
            TestContext.Current.CancellationToken);

        Assert.Equal(MatchStatus.Deploying, session.Match.Status);
        Assert.Equal(2, session.Match.Players.Count);
        var bot = Assert.Single(session.Match.Players, player => player.IsBot);
        Assert.True(bot.IsReady);
        Assert.False(session.Match.Players.Single(player => player.Id == session.PlayerId).IsBot);
    }

    [Fact]
    public async Task EndingHumanTurnExecutesAndPersistsEntireBotTurn()
    {
        var repository = new InMemoryMatchRepository();
        var service = CreateService(repository);
        var session = await service.CreateAsync(
            new CreateMatchCommand("Tek oyunculu", "İnsan", PlayAgainstBot: true),
            TestContext.Current.CancellationToken);
        var started = await service.ReadyPlayerAsync(
            new ReadyPlayerCommand(session.Match.Id, session.PlayerId, session.Match.Version),
            TestContext.Current.CancellationToken);

        var afterBot = await service.EndTurnAsync(
            new EndTurnCommand(started.Id, session.PlayerId, started.Version),
            TestContext.Current.CancellationToken);

        var bot = afterBot.Players.Single(player => player.IsBot);
        Assert.Equal(session.PlayerId, afterBot.ActivePlayerId);
        Assert.Contains(repository.Match.Events, item =>
            item.ActorPlayerId == bot.Id && item.Type == MatchEventType.UnitMoved);
        Assert.Equal(3, repository.SaveCount);
    }

    private static MatchService CreateService(InMemoryMatchRepository repository) =>
        new(
            repository,
            new FixedTimeProvider(Now),
            new BotTurnExecutor(new BalancedBotTurnPlanner()));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class InMemoryMatchRepository : IGameMatchRepository
    {
        private GameMatch? match;

        public int SaveCount { get; private set; }

        public GameMatch Match => match ?? throw new InvalidOperationException("A match has not been added.");

        public Task<IReadOnlyList<GameMatchSummary>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GameMatchSummary>>([]);

        public Task<GameMatch?> GetAsync(Guid matchId, CancellationToken cancellationToken) =>
            Task.FromResult(match?.Id == matchId ? match : null);

        public Task<IReadOnlyList<Guid>> ListExpiredTurnIdsAsync(
            DateTimeOffset now,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task AddAsync(GameMatch newMatch, CancellationToken cancellationToken)
        {
            match = newMatch;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
