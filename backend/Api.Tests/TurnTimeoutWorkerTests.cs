using Game.Api.Hubs;
using Game.Api.Services;
using Game.Application;
using Game.Application.Abstractions;
using Game.Application.Common;
using Game.Application.Features.Commands;
using Game.Domain.Entities;
using Game.Domain.Enums;
using Game.Domain.Rules;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Game.Api.Tests;

public sealed class TurnTimeoutWorkerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task WorkerPassesTurnWithoutClientRequestsAndPublishesPrivateViewsOnlyOnce()
    {
        var fixture = CreateFixture();
        await using var provider = fixture.Provider;
        fixture.Clock.UtcNow = Now.AddSeconds(90);

        await fixture.Worker.ProcessExpiredTurnsAsync(CancellationToken.None);
        await fixture.Worker.ProcessExpiredTurnsAsync(CancellationToken.None);

        Assert.Equal(1, fixture.Repository.Saves);
        Assert.Equal(2, fixture.Match.TurnNumber);
        Assert.Equal(2, fixture.Hub.Messages.Count);
        foreach (var (playerId, snapshot) in fixture.Hub.Messages)
        {
            Assert.Equal(Now.AddSeconds(180), snapshot.TurnExpiresAtUtc);
            Assert.Equal(fixture.Clock.UtcNow, snapshot.ServerTimeUtc);
            Assert.Equal(90, snapshot.TurnDurationSeconds);
            Assert.Equal(MatchEventType.TurnTimedOut, snapshot.Events.Last().Type);
            Assert.All(snapshot.Units, unit => Assert.Equal(Guid.Parse(playerId), unit.OwnerPlayerId));
        }
    }

    [Fact]
    public async Task RefreshDoesNotExtendTimeAndLateCommandsAreRejectedBeforeSweep()
    {
        var fixture = CreateFixture();
        await using var provider = fixture.Provider;
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<MatchService>();
        var player = fixture.Match.Players.First(player => player.Seat == 1);
        fixture.Clock.UtcNow = Now.AddSeconds(30);
        var restored = await service.GetAsync(fixture.Match.Id, player.Id, CancellationToken.None);
        Assert.Equal(Now.AddSeconds(90), restored.TurnExpiresAtUtc);
        Assert.Equal(fixture.Clock.UtcNow, restored.ServerTimeUtc);
        await fixture.Worker.ProcessExpiredTurnsAsync(CancellationToken.None);
        Assert.Equal(0, fixture.Repository.Saves);

        fixture.Clock.UtcNow = Now.AddSeconds(90);
        var scout = fixture.Match.Units.First(unit => unit.OwnerPlayerId == player.Id && unit.Type == UnitType.Scout);
        await Assert.ThrowsAsync<DomainRuleException>(() => service.MoveAsync(
            new MoveUnitCommand(fixture.Match.Id, player.Id, scout.Id, 2, 2, fixture.Match.Version), CancellationToken.None));
        Assert.Equal(0, fixture.Repository.Saves);
        await fixture.Worker.ProcessExpiredTurnsAsync(CancellationToken.None);
        Assert.Equal(1, fixture.Repository.Saves);
    }

    [Fact]
    public async Task VersionConflictNeverPublishesAnUncommittedTimeout()
    {
        var fixture = CreateFixture();
        await using var provider = fixture.Provider;
        fixture.Repository.RejectSave = true;
        fixture.Clock.UtcNow = Now.AddSeconds(90);
        await fixture.Worker.ProcessExpiredTurnsAsync(CancellationToken.None);
        Assert.Empty(fixture.Hub.Messages);
        Assert.Equal(0, fixture.Repository.Saves);
    }

    private static Fixture CreateFixture()
    {
        var (match, host) = GameMatch.Create("Saat testi", "Batı", Now);
        var guest = match.Join("Doğu", Now);
        match.ReadyPlayer(host.Id, match.Version, Now);
        match.ReadyPlayer(guest.Id, match.Version, Now);
        var repository = new TestRepository(match);
        var clock = new TestClock { UtcNow = Now };
        var hub = new TestHubContext();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IGameMatchRepository>(repository);
        services.AddSingleton<IHubContext<GameHub>>(hub);
        services.AddApplication();
        services.AddScoped<MatchUpdatePublisher>();
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var worker = new TurnTimeoutWorker(provider.GetRequiredService<IServiceScopeFactory>(), clock, NullLogger<TurnTimeoutWorker>.Instance);
        return new Fixture(provider, worker, match, repository, clock, hub);
    }

    private sealed record Fixture(ServiceProvider Provider, TurnTimeoutWorker Worker, GameMatch Match, TestRepository Repository, TestClock Clock, TestHubContext Hub);

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; }
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class TestRepository(GameMatch match) : IGameMatchRepository
    {
        public int Saves { get; private set; }
        public bool RejectSave { get; set; }
        public Task<GameMatch?> GetAsync(Guid matchId, CancellationToken cancellationToken) => Task.FromResult<GameMatch?>(match.Id == matchId ? match : null);
        public Task<IReadOnlyList<Guid>> ListExpiredTurnIdsAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>(match.Status == MatchStatus.InProgress && match.TurnExpiresAtUtc <= now ? [match.Id] : []);
        public Task<IReadOnlyList<GameMatchSummary>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddAsync(GameMatch newMatch, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            if (RejectSave) throw new MatchConcurrencyException("Concurrent command won.", new InvalidOperationException());
            Saves++;
            return Task.CompletedTask;
        }
    }

    private sealed class TestHubContext : IHubContext<GameHub>
    {
        public List<(string PlayerId, MatchSnapshot Snapshot)> Messages { get; } = [];
        public IHubClients Clients => new TestClients(Messages);
        public IGroupManager Groups => throw new NotSupportedException();
    }

    private sealed class TestClients(List<(string, MatchSnapshot)> messages) : IHubClients
    {
        public IClientProxy User(string userId) => new TestClient(userId, messages);
        public IClientProxy All => throw new NotSupportedException("Do not broadcast private views to all players.");
        public IClientProxy AllExcept(IReadOnlyList<string> ids) => throw new NotSupportedException();
        public IClientProxy Client(string id) => throw new NotSupportedException();
        public IClientProxy Clients(IReadOnlyList<string> ids) => throw new NotSupportedException();
        public IClientProxy Group(string name) => throw new NotSupportedException();
        public IClientProxy GroupExcept(string name, IReadOnlyList<string> ids) => throw new NotSupportedException();
        public IClientProxy Groups(IReadOnlyList<string> names) => throw new NotSupportedException();
        public IClientProxy Users(IReadOnlyList<string> ids) => throw new NotSupportedException();
    }

    private sealed class TestClient(string userId, List<(string, MatchSnapshot)> messages) : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            Assert.Equal(GameHubEvents.MatchUpdated, method);
            messages.Add((userId, Assert.IsType<MatchSnapshot>(Assert.Single(args))));
            return Task.CompletedTask;
        }
    }
}
