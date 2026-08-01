using Game.Domain.Entities;
using Game.Domain.Enums;
using Game.Domain.Rules;

namespace Game.Domain.Tests;

public sealed class GameMatchTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void JoiningSecondPlayerStartsMatchAndDeploysUnits()
    {
        var (match, host) = GameMatch.Create("Kuzey Geçidi", "Atlas", Now);

        var guest = match.Join("Poyraz", Now.AddSeconds(1));

        Assert.Equal(MatchStatus.InProgress, match.Status);
        Assert.Equal(host.Id, match.ActivePlayerId);
        Assert.Equal(1, match.Version);
        Assert.Collection(
            match.Units.OrderBy(unit => unit.Column),
            unit =>
            {
                Assert.Equal(host.Id, unit.OwnerPlayerId);
                Assert.Equal((1, 3), (unit.Column, unit.Row));
            },
            unit =>
            {
                Assert.Equal(guest.Id, unit.OwnerPlayerId);
                Assert.Equal((7, 3), (unit.Column, unit.Row));
            });
    }

    [Fact]
    public void ActivePlayerCanMoveOneAdjacentHexAndPassTurn()
    {
        var (match, host) = GameMatch.Create("Kuzey Geçidi", "Atlas", Now);
        var guest = match.Join("Poyraz", Now.AddSeconds(1));
        var hostUnit = match.Units.Single(unit => unit.OwnerPlayerId == host.Id);

        match.MoveUnit(host.Id, hostUnit.Id, targetColumn: 2, targetRow: 3, expectedVersion: 1);

        Assert.Equal((2, 3), (hostUnit.Column, hostUnit.Row));
        Assert.Equal(guest.Id, match.ActivePlayerId);
        Assert.Equal(2, match.TurnNumber);
        Assert.Equal(2, match.Version);
    }

    [Fact]
    public void PlayerCannotMoveOutsideTheirTurn()
    {
        var (match, _) = GameMatch.Create("Kuzey Geçidi", "Atlas", Now);
        var guest = match.Join("Poyraz", Now.AddSeconds(1));
        var guestUnit = match.Units.Single(unit => unit.OwnerPlayerId == guest.Id);

        var exception = Assert.Throws<DomainRuleException>(() =>
            match.MoveUnit(guest.Id, guestUnit.Id, 6, 3, expectedVersion: 1));

        Assert.Equal("It is not this player's turn.", exception.Message);
    }

    [Fact]
    public void StaleVersionIsRejected()
    {
        var (match, host) = GameMatch.Create("Kuzey Geçidi", "Atlas", Now);
        match.Join("Poyraz", Now.AddSeconds(1));
        var hostUnit = match.Units.Single(unit => unit.OwnerPlayerId == host.Id);

        var exception = Assert.Throws<DomainRuleException>(() =>
            match.MoveUnit(host.Id, hostUnit.Id, 2, 3, expectedVersion: 0));

        Assert.Contains("stale", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlayerNamesMustBeUniqueWithinMatch()
    {
        var (match, _) = GameMatch.Create("Kuzey Geçidi", "Atlas", Now);

        var exception = Assert.Throws<DomainRuleException>(() => match.Join("atlas", Now));

        Assert.Contains("unique", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
