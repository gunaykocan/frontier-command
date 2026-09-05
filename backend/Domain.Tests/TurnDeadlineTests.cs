using Game.Domain.Entities;
using Game.Domain.Enums;
using Game.Domain.Rules;

namespace Game.Domain.Tests;

public sealed class TurnDeadlineTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ClockStartsOnlyWhenBothPlayersAreReady()
    {
        var (match, host) = GameMatch.Create("Saat", "Batı", Now);
        Assert.Null(match.TurnExpiresAtUtc);
        Assert.False(match.TryExpireTurn(Now.AddDays(1)));
        var guest = match.Join("Doğu", Now);
        match.ReadyPlayer(host.Id, match.Version, Now);
        Assert.Null(match.TurnExpiresAtUtc);
        Assert.False(match.TryExpireTurn(Now.AddDays(1)));

        match.ReadyPlayer(guest.Id, match.Version, Now.AddHours(1));
        Assert.Equal(Now.AddHours(1).AddSeconds(90), match.TurnExpiresAtUtc);
    }

    [Fact]
    public void MovingDoesNotExtendTheDeadlineAndTimeoutKeepsTheMove()
    {
        var (match, host, guest) = StartedMatch();
        var scout = match.Units.Single(unit => unit.OwnerPlayerId == host.Id && unit.Type == UnitType.Scout);
        match.MoveUnit(host.Id, scout.Id, 2, 2, match.Version, Now.AddSeconds(20));
        Assert.Equal(Now.AddSeconds(90), match.TurnExpiresAtUtc);
        var version = match.Version;
        Assert.False(match.TryExpireTurn(Now.AddSeconds(90).AddTicks(-1)));

        Assert.True(match.TryExpireTurn(Now.AddSeconds(90)));
        Assert.Equal((2, 2), (scout.Column, scout.Row));
        Assert.Equal(guest.Id, match.ActivePlayerId);
        Assert.Equal(2, match.TurnNumber);
        Assert.Equal(version + 1, match.Version);
        Assert.Equal(Now.AddSeconds(180), match.TurnExpiresAtUtc);
        Assert.All(match.Units.Where(unit => unit.OwnerPlayerId == host.Id), unit => Assert.Equal(0, unit.RemainingMovement));
        Assert.All(match.Units.Where(unit => unit.OwnerPlayerId == guest.Id), unit =>
        {
            Assert.Equal(unit.MovementAllowance, unit.RemainingMovement);
            Assert.False(unit.HasAttackedThisTurn);
        });
        var timeout = Assert.Single(match.Events, item => item.Type == MatchEventType.TurnTimedOut);
        Assert.Equal(host.Id, timeout.ActorPlayerId);
        Assert.Equal(guest.Id, timeout.RelatedPlayerId);
        Assert.Equal(1, timeout.TurnNumber);
        Assert.False(match.TryExpireTurn(Now.AddSeconds(90)));
        Assert.Equal(version + 1, match.Version);
    }

    [Theory]
    [InlineData("move", 90)]
    [InlineData("attack", 90)]
    [InlineData("end", 90)]
    [InlineData("move", 91)]
    [InlineData("attack", 91)]
    [InlineData("end", 91)]
    public void ExpiredCommandsAreRejectedEvenBeforeTheWorkerRuns(string command, int elapsedSeconds)
    {
        var (match, host, guest) = StartedMatch();
        var scout = match.Units.First(unit => unit.OwnerPlayerId == host.Id);
        var enemy = match.Units.First(unit => unit.OwnerPlayerId == guest.Id);
        var version = match.Version;
        var now = Now.AddSeconds(elapsedSeconds);
        var exception = Assert.Throws<DomainRuleException>(() =>
        {
            if (command == "move") match.MoveUnit(host.Id, scout.Id, 2, 2, version, now);
            else if (command == "attack") match.AttackUnit(host.Id, scout.Id, enemy.Id, version, now);
            else match.EndTurn(host.Id, version, now);
        });
        Assert.Contains("time has expired", exception.Message);
        Assert.Equal(version, match.Version);
        Assert.Equal(host.Id, match.ActivePlayerId);
    }

    [Fact]
    public void ManualTurnEndStartsANewClockAndOldTimeoutDoesNotSkipIt()
    {
        var (match, host, guest) = StartedMatch();
        match.EndTurn(host.Id, match.Version, Now.AddSeconds(89));
        Assert.Equal(guest.Id, match.ActivePlayerId);
        Assert.Equal(Now.AddSeconds(179), match.TurnExpiresAtUtc);
        Assert.Equal(MatchEventType.TurnEnded, match.Events.Last().Type);
        Assert.False(match.TryExpireTurn(Now.AddSeconds(90)));
        Assert.Equal(2, match.TurnNumber);
    }

    [Fact]
    public void RestartAfterLongDowntimeAdvancesOnlyOneTurn()
    {
        var (match, _, guest) = StartedMatch();
        var restartedAt = Now.AddDays(1);
        Assert.True(match.TryExpireTurn(restartedAt));
        Assert.Equal(guest.Id, match.ActivePlayerId);
        Assert.Equal(2, match.TurnNumber);
        Assert.Equal(restartedAt.AddSeconds(90), match.TurnExpiresAtUtc);
        Assert.False(match.TryExpireTurn(restartedAt));
    }

    private static (GameMatch Match, GamePlayer Host, GamePlayer Guest) StartedMatch()
    {
        var (match, host) = GameMatch.Create("Saat", "Batı", Now);
        var guest = match.Join("Doğu", Now);
        match.ReadyPlayer(host.Id, match.Version, Now);
        match.ReadyPlayer(guest.Id, match.Version, Now);
        return (match, host, guest);
    }
}
