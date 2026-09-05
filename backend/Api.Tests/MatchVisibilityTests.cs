using Game.Application.Common;
using Game.Domain.Entities;
using Game.Domain.Enums;
using Game.Domain.Rules;

namespace Game.Api.Tests;

public sealed class MatchVisibilityTests
{
    [Fact]
    public void EnemyBehindHillIsAbsentFromSnapshotUntilScoutFlanksTheHill()
    {
        var now = DateTimeOffset.UtcNow;
        var (match, host) = GameMatch.Create("Görüş hattı", "Batı", now);
        var guest = match.Join("Doğu", now);
        var hostScout = match.Units.Single(unit => unit.OwnerPlayerId == host.Id && unit.Type == UnitType.Scout);
        var guestScout = match.Units.Single(unit => unit.OwnerPlayerId == guest.Id && unit.Type == UnitType.Scout);
        match.ReadyPlayer(host.Id, match.Version);
        match.ReadyPlayer(guest.Id, match.Version);

        match.MoveUnit(host.Id, hostScout.Id, 2, 2, match.Version);
        match.MoveUnit(host.Id, hostScout.Id, 2, 3, match.Version);
        match.EndTurn(host.Id, match.Version);
        match.MoveUnit(guest.Id, guestScout.Id, 6, 2, match.Version);
        match.MoveUnit(guest.Id, guestScout.Id, 5, 2, match.Version);
        match.EndTurn(guest.Id, match.Version);
        match.EndTurn(host.Id, match.Version);
        match.MoveUnit(guest.Id, guestScout.Id, 4, 3, match.Version);

        Assert.Equal(2, BattlefieldVision.HexDistance(hostScout.Column, hostScout.Row, 4, 3));
        var blockedView = MatchSnapshot.From(match, host.Id);
        var ownerView = MatchSnapshot.From(match, guest.Id);
        Assert.DoesNotContain(blockedView.Units, unit => unit.Id == guestScout.Id);
        Assert.DoesNotContain(blockedView.VisibleCoordinates, tile => tile.Column == 4 && tile.Row == 3);
        Assert.DoesNotContain(blockedView.Events, item =>
            item.UnitId == guestScout.Id && item.ToColumn == 4 && item.ToRow == 3);
        Assert.Contains(ownerView.Units, unit => unit.Id == guestScout.Id);
        Assert.True(blockedView.TerrainTiles.Single(tile => tile.Column == 3 && tile.Row == 3).BlocksVision);

        match.EndTurn(guest.Id, match.Version);
        match.MoveUnit(host.Id, hostScout.Id, 2, 2, match.Version);
        match.MoveUnit(host.Id, hostScout.Id, 3, 2, match.Version);

        var flankedView = MatchSnapshot.From(match, host.Id);
        Assert.Contains(flankedView.Units, unit => unit.Id == guestScout.Id);
        Assert.Contains(flankedView.VisibleCoordinates, tile => tile.Column == 4 && tile.Row == 3);

        match.MoveUnit(host.Id, hostScout.Id, 2, 3, match.Version);
        Assert.DoesNotContain(MatchSnapshot.From(match, host.Id).Units, unit => unit.Id == guestScout.Id);
    }
}
