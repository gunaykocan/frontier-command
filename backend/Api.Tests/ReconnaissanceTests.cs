using System.Text.Json;
using Game.Application.Common;
using Game.Domain.Entities;
using Game.Domain.Enums;
using Game.Domain.Rules;

namespace Game.Api.Tests;

public sealed class ReconnaissanceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-27T12:00:00Z");

    [Theory]
    [InlineData(UnitType.Scout, 3)]
    [InlineData(UnitType.Infantry, 1)]
    [InlineData(UnitType.Armor, 1)]
    public void OnlyScoutsHaveLongRangeVision(UnitType type, int range) =>
        Assert.Equal(range, UnitProfiles.For(type).VisionRange);

    [Fact]
    public void HiddenMovementDoesNotUpdateLastSeenPositionOrExposeHistoricalEvents()
    {
        var (match, host, guest, scout, enemy) = CreateContactBehindHill();
        var hiddenMove = match.Events.Last(item => item.Type == MatchEventType.UnitMoved);
        Assert.DoesNotContain(MatchSnapshot.From(match, host.Id).Units, unit => unit.Id == enemy.Id);
        Assert.DoesNotContain(MatchSnapshot.From(match, host.Id).LastKnownEnemies, item => item.UnitId == enemy.Id);

        End(match, guest);
        Move(match, host, scout, 2, 2);
        Move(match, host, scout, 3, 2);
        var observed = MatchSnapshot.From(match, host.Id);
        Assert.Contains(observed.Units, unit => unit.Id == enemy.Id);
        Assert.DoesNotContain(observed.LastKnownEnemies, item => item.UnitId == enemy.Id);
        Assert.DoesNotContain(observed.Events, item => item.Sequence == hiddenMove.Sequence);

        Move(match, host, scout, 2, 3);
        var remembered = Assert.Single(MatchSnapshot.From(match, host.Id).LastKnownEnemies, item => item.UnitId == enemy.Id);
        Assert.Equal((4, 3, 5), (remembered.Column, remembered.Row, remembered.LastSeenTurnNumber));
        End(match, host);
        Move(match, guest, enemy, 5, 3);
        Move(match, guest, enemy, 6, 3);

        var hiddenAgain = MatchSnapshot.From(match, host.Id);
        Assert.Equal(remembered, Assert.Single(hiddenAgain.LastKnownEnemies, item => item.UnitId == enemy.Id));
        Assert.DoesNotContain(hiddenAgain.Units, unit => unit.Id == enemy.Id);
        Assert.DoesNotContain(hiddenAgain.Events, item => item.UnitId == enemy.Id && item.ToColumn == 6 && item.ToRow == 3);
        Assert.DoesNotContain(MatchSnapshot.From(match, guest.Id).LastKnownEnemies, item => item.UnitId == enemy.Id);

        // Re-scouting the old cell clears the contact without revealing its new location.
        End(match, guest);
        Move(match, host, scout, 2, 2);
        Move(match, host, scout, 3, 2);
        var cleared = MatchSnapshot.From(match, host.Id);
        Assert.Contains(cleared.VisibleCoordinates, tile => tile.Column == 4 && tile.Row == 3);
        Assert.DoesNotContain(cleared.LastKnownEnemies, item => item.UnitId == enemy.Id);
        Assert.DoesNotContain(cleared.Units, unit => unit.Id == enemy.Id);
        Assert.DoesNotContain(match.EnemySightings, item => item.ViewerPlayerId == host.Id && item.EnemyUnitId == enemy.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AttackerRevealsOnlyItsCellUntilOpponentsManualOrTimedTurnEnds(bool timeout)
    {
        var (match, host) = GameMatch.Create("Ateş izi", "Batı", Now);
        var guest = match.Join("Doğu", Now);
        var scout = Unit(match, host, UnitType.Scout);
        var enemyScout = Unit(match, guest, UnitType.Scout);
        var attacker = Unit(match, guest, UnitType.Infantry);
        match.PlaceUnit(guest.Id, enemyScout.Id, 8, 6, match.Version);
        Ready(match, host, guest);
        foreach (var column in new[] { 2, 3, 4 }) Move(match, host, scout, column, 2);
        End(match, host);
        End(match, guest);
        foreach (var column in new[] { 5, 6, 7 }) Move(match, host, scout, column, 2);
        End(match, host);
        match.AttackUnit(guest.Id, attacker.Id, scout.Id, match.Version, Now);

        Assert.DoesNotContain(match.Units, unit => unit.Id == scout.Id);
        Assert.Equal(5, attacker.RevealedUntilTurnNumber);
        var exposed = MatchSnapshot.From(match, host.Id);
        Assert.True(Assert.Single(exposed.Units, unit => unit.Id == attacker.Id).IsRevealedByAttack);
        Assert.Contains(exposed.VisibleCoordinates, tile => tile.Column == 7 && tile.Row == 3);
        Assert.DoesNotContain(exposed.VisibleCoordinates, tile => tile.Column == 7 && tile.Row == 4);
        Assert.DoesNotContain(exposed.Units, unit => unit.Id == enemyScout.Id);
        Assert.True(Assert.Single(MatchSnapshot.From(match, guest.Id).Units, unit => unit.Id == attacker.Id).IsRevealedByAttack);

        End(match, guest);
        Assert.Contains(MatchSnapshot.From(match, host.Id).Units, unit => unit.Id == attacker.Id);
        if (timeout) Assert.True(match.TryExpireTurn(match.TurnExpiresAtUtc!.Value));
        else End(match, host);

        Assert.Equal(6, match.TurnNumber);
        var hidden = MatchSnapshot.From(match, host.Id);
        Assert.DoesNotContain(hidden.Units, unit => unit.Id == attacker.Id);
        var contact = Assert.Single(hidden.LastKnownEnemies, item => item.UnitId == attacker.Id);
        Assert.Equal((7, 3, 5), (contact.Column, contact.Row, contact.LastSeenTurnNumber));
        Assert.False(attacker.IsRevealedByAttack(match.TurnNumber));
    }

    [Fact]
    public void LastKnownContactContainsOnlyHistoricalFieldsAndCannotBeAttackedFromAfar()
    {
        var (match, host, guest, scout, enemy) = CreateContactBehindHill();
        End(match, guest);
        Move(match, host, scout, 2, 2);
        Move(match, host, scout, 3, 2);
        Move(match, host, scout, 2, 3);
        var contact = Assert.Single(MatchSnapshot.From(match, host.Id).LastKnownEnemies, item => item.UnitId == enemy.Id);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(contact, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(new[] { "column", "lastSeenTurnNumber", "row", "unitId", "unitType" },
            json.RootElement.EnumerateObject().Select(item => item.Name).Order().ToArray());
        Assert.Throws<DomainRuleException>(() => match.AttackUnit(host.Id, scout.Id, contact.UnitId, match.Version, Now));
    }

    private static (GameMatch, GamePlayer, GamePlayer, GameUnit, GameUnit) CreateContactBehindHill()
    {
        var (match, host) = GameMatch.Create("Keşif hafızası", "Batı", Now);
        var guest = match.Join("Doğu", Now);
        Assert.Empty(MatchSnapshot.From(match, host.Id).LastKnownEnemies);
        var scout = Unit(match, host, UnitType.Scout);
        var enemy = Unit(match, guest, UnitType.Scout);
        Ready(match, host, guest);
        Move(match, host, scout, 2, 2);
        Move(match, host, scout, 2, 3);
        End(match, host);
        Move(match, guest, enemy, 6, 2);
        Move(match, guest, enemy, 5, 2);
        End(match, guest);
        End(match, host);
        Move(match, guest, enemy, 4, 3);
        return (match, host, guest, scout, enemy);
    }

    private static GameUnit Unit(GameMatch match, GamePlayer player, UnitType type) =>
        match.Units.Single(unit => unit.OwnerPlayerId == player.Id && unit.Type == type
            && unit.Column == (player.Seat == 1 ? 1 : 7));

    private static void Ready(GameMatch match, GamePlayer host, GamePlayer guest)
    {
        match.ReadyPlayer(host.Id, match.Version, Now);
        match.ReadyPlayer(guest.Id, match.Version, Now);
    }

    private static void End(GameMatch match, GamePlayer player) => match.EndTurn(player.Id, match.Version, Now);

    private static void Move(GameMatch match, GamePlayer player, GameUnit unit, int column, int row) =>
        match.MoveUnit(player.Id, unit.Id, column, row, match.Version, Now);
}
