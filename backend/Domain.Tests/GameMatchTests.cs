using Game.Domain.Entities;
using Game.Domain.Enums;
using Game.Domain.Rules;

namespace Game.Domain.Tests;

public sealed class GameMatchTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void JoiningSecondPlayerStartsDeploymentAndDeploysUnits()
    {
        var (match, host) = GameMatch.Create("Kuzey Geçidi", "Atlas", Now);

        var guest = match.Join("Poyraz", Now.AddSeconds(1));

        Assert.Equal(MatchStatus.Deploying, match.Status);
        Assert.Null(match.ActivePlayerId);
        Assert.Equal(1, match.Version);
        Assert.Equal(10, match.Units.Count);
        AssertStartingArmies(match);
        Assert.All(match.Players, player => Assert.False(player.IsReady));
        Assert.Equal(4, match.SpecialTiles.Count);
        Assert.Equal(2, match.SpecialTiles.Count(tile => tile.Type == SpecialTileType.Reinforcement));
        Assert.Equal(2, match.SpecialTiles.Count(tile => tile.Type == SpecialTileType.Mine));
        Assert.All(match.SpecialTiles, tile =>
        {
            Assert.InRange(tile.Column, GameMatch.DeploymentColumns, GameMatch.BoardColumns - GameMatch.DeploymentColumns - 1);
            Assert.False(tile.IsRevealed);
        });
        Assert.Equal(
            match.SpecialTiles.Count,
            match.SpecialTiles.Select(tile => (tile.Column, tile.Row)).Distinct().Count());
        var deploymentEvent = Assert.Single(match.Events);
        Assert.Equal(MatchEventType.DeploymentStarted, deploymentEvent.Type);
        Assert.Equal(1, deploymentEvent.Sequence);

        var hostUnits = match.Units
            .Where(unit => unit.OwnerPlayerId == host.Id && unit.Column == 1)
            .ToDictionary(unit => unit.Type);
        var guestUnits = match.Units
            .Where(unit => unit.OwnerPlayerId == guest.Id && unit.Column == 7)
            .ToDictionary(unit => unit.Type);

        Assert.Equal((1, 2, 0, 2, 1), Describe(hostUnits[UnitType.Scout]));
        Assert.Equal((1, 3, 0, 4, 2), Describe(hostUnits[UnitType.Infantry]));
        Assert.Equal((1, 4, 0, 6, 3), Describe(hostUnits[UnitType.Armor]));
        Assert.All(guestUnits.Values, unit => Assert.Equal(0, unit.RemainingMovement));
        Assert.Equal([2, 3, 4], guestUnits.Values.Select(unit => unit.Row).Order().ToArray());
    }

    [Fact]
    public void JoiningBotMarksOnlyTheAutomatedPlayer()
    {
        var (match, host) = GameMatch.Create("Tek oyunculu", "Atlas", Now);

        var bot = match.JoinBot("Komuta Botu", Now);

        Assert.False(host.IsBot);
        Assert.True(bot.IsBot);
        Assert.Equal(2, bot.Seat);
        Assert.Equal(MatchStatus.Deploying, match.Status);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void AdditionalInfantryCanBePlacedAndMovedIndependently(int seat)
    {
        var (match, host) = GameMatch.Create("Beş birlik", "Batı", Now);
        var guest = match.Join("Doğu", Now);
        var player = seat == 1 ? host : guest;
        var reserveColumn = seat == 1 ? 0 : 8;
        var reserves = match.Units.Where(unit => unit.OwnerPlayerId == player.Id && unit.Column == reserveColumn)
            .OrderBy(unit => unit.Row).ToArray();
        Assert.Equal(2, reserves.Length);
        match.PlaceUnit(player.Id, reserves[0].Id, reserveColumn, 1, match.Version);
        match.ReadyPlayer(host.Id, match.Version, Now);
        match.ReadyPlayer(guest.Id, match.Version, Now);
        if (seat == 2) match.EndTurn(host.Id, match.Version, Now);

        match.MoveUnit(player.Id, reserves[0].Id, reserveColumn, 2, match.Version, Now);

        Assert.Equal((reserveColumn, 2, 1), (reserves[0].Column, reserves[0].Row, reserves[0].RemainingMovement));
        Assert.Equal((reserveColumn, 4, 2), (reserves[1].Column, reserves[1].Row, reserves[1].RemainingMovement));
        Assert.Equal(5, match.Units.Count(unit => unit.OwnerPlayerId == player.Id));
        Assert.Equal(Now.AddSeconds(90), match.TurnExpiresAtUtc);
    }

    [Fact]
    public void PlayersCanPlaceUnitsInsideTheirOwnDeploymentZone()
    {
        var (match, host) = GameMatch.Create("Kuzey Geçidi", "Atlas", Now);
        var guest = match.Join("Poyraz", Now.AddSeconds(1));
        var hostInfantry = FindUnit(match, host.Id, UnitType.Infantry);
        var guestInfantry = FindUnit(match, guest.Id, UnitType.Infantry);

        match.PlaceUnit(host.Id, hostInfantry.Id, targetColumn: 0, targetRow: 0, expectedVersion: 1);
        match.PlaceUnit(guest.Id, guestInfantry.Id, targetColumn: 8, targetRow: 0, expectedVersion: 2);

        Assert.Equal((0, 0), (hostInfantry.Column, hostInfantry.Row));
        Assert.Equal((8, 0), (guestInfantry.Column, guestInfantry.Row));
        Assert.Equal(3, match.Version);
        var placementEvents = match.Events.Where(item => item.Type == MatchEventType.UnitPlaced).ToList();
        Assert.Equal(2, placementEvents.Count);
        Assert.Equal((1, 3, 0, 0), (
            placementEvents[0].FromColumn,
            placementEvents[0].FromRow,
            placementEvents[0].ToColumn,
            placementEvents[0].ToRow));

        var exception = Assert.Throws<DomainRuleException>(() =>
            match.PlaceUnit(host.Id, hostInfantry.Id, targetColumn: 2, targetRow: 0, expectedVersion: 3));
        Assert.Contains("deployment zone", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BattleStartsAfterBothPlayersAreReadyAndDeploymentLocks()
    {
        var (match, host) = GameMatch.Create("Kuzey Geçidi", "Atlas", Now);
        var guest = match.Join("Poyraz", Now.AddSeconds(1));
        var hostScout = FindUnit(match, host.Id, UnitType.Scout);

        match.ReadyPlayer(host.Id, expectedVersion: 1);

        Assert.True(match.Players.Single(player => player.Id == host.Id).IsReady);
        Assert.Equal(MatchStatus.Deploying, match.Status);
        Assert.Null(match.ActivePlayerId);

        var exception = Assert.Throws<DomainRuleException>(() =>
            match.PlaceUnit(host.Id, hostScout.Id, targetColumn: 0, targetRow: 0, expectedVersion: 2));
        Assert.Contains("ready player", exception.Message, StringComparison.OrdinalIgnoreCase);

        match.ReadyPlayer(guest.Id, expectedVersion: 2);

        Assert.Equal(MatchStatus.InProgress, match.Status);
        Assert.Equal(host.Id, match.ActivePlayerId);
        Assert.Equal(3, match.Version);
        Assert.Equal(hostScout.MovementAllowance, hostScout.RemainingMovement);
        Assert.All(
            match.Units.Where(unit => unit.OwnerPlayerId == guest.Id),
            unit => Assert.Equal(0, unit.RemainingMovement));
        Assert.Equal(
            [
                MatchEventType.DeploymentStarted,
                MatchEventType.PlayerReady,
                MatchEventType.PlayerReady,
                MatchEventType.BattleStarted
            ],
            match.Events.OrderBy(item => item.Sequence).Select(item => item.Type).ToArray());
    }

    [Fact]
    public void ActivePlayerCanMoveAndExplicitlyPassTurn()
    {
        var (match, host, guest) = CreateStartedMatch();
        var hostUnit = FindUnit(match, host.Id, UnitType.Infantry);
        var guestUnit = FindUnit(match, guest.Id, UnitType.Infantry);

        match.MoveUnit(host.Id, hostUnit.Id, targetColumn: 2, targetRow: 3, expectedVersion: 3);

        Assert.Equal((2, 3), (hostUnit.Column, hostUnit.Row));
        Assert.Equal(1, hostUnit.RemainingMovement);
        Assert.Equal(host.Id, match.ActivePlayerId);
        Assert.Equal(1, match.TurnNumber);
        Assert.Equal(4, match.Version);
        var movementEvent = match.Events.Last();
        Assert.Equal(MatchEventType.UnitMoved, movementEvent.Type);
        Assert.Equal((1, 3, 2, 3), (
            movementEvent.FromColumn,
            movementEvent.FromRow,
            movementEvent.ToColumn,
            movementEvent.ToRow));

        match.EndTurn(host.Id, expectedVersion: 4);

        Assert.Equal(guest.Id, match.ActivePlayerId);
        Assert.Equal(2, match.TurnNumber);
        Assert.Equal(5, match.Version);
        Assert.Equal(0, hostUnit.RemainingMovement);
        Assert.Equal(guestUnit.MovementAllowance, guestUnit.RemainingMovement);
        var turnEvent = match.Events.Last();
        Assert.Equal(MatchEventType.TurnEnded, turnEvent.Type);
        Assert.Equal(host.Id, turnEvent.ActorPlayerId);
        Assert.Equal(guest.Id, turnEvent.RelatedPlayerId);
    }

    [Fact]
    public void PlayerCannotMoveOutsideTheirTurn()
    {
        var (match, _, guest) = CreateStartedMatch();
        var guestUnit = FindUnit(match, guest.Id, UnitType.Infantry);

        var exception = Assert.Throws<DomainRuleException>(() =>
            match.MoveUnit(guest.Id, guestUnit.Id, 6, 3, expectedVersion: 3));

        Assert.Equal("It is not this player's turn.", exception.Message);
    }

    [Fact]
    public void StaleVersionIsRejected()
    {
        var (match, host, _) = CreateStartedMatch();
        var hostUnit = FindUnit(match, host.Id, UnitType.Infantry);

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

    [Fact]
    public void UnitCannotMoveAfterSpendingItsMovementAllowance()
    {
        var (match, host, _) = CreateStartedMatch();
        var scout = FindUnit(match, host.Id, UnitType.Scout);

        match.MoveUnit(host.Id, scout.Id, 2, 2, expectedVersion: 3);
        match.MoveUnit(host.Id, scout.Id, 3, 2, expectedVersion: 4);
        match.MoveUnit(host.Id, scout.Id, 4, 2, expectedVersion: 5);

        var exception = Assert.Throws<DomainRuleException>(() =>
            match.MoveUnit(host.Id, scout.Id, 5, 2, expectedVersion: 6));

        Assert.Contains("no movement points", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RoughTerrainConsumesItsPublishedMovementCost()
    {
        var (match, host, _) = CreateStartedMatch();
        var scout = FindUnit(match, host.Id, UnitType.Scout);

        var forest = BattlefieldTerrain.At(column: 1, row: 1);
        match.MoveUnit(host.Id, scout.Id, forest.Column, forest.Row, expectedVersion: 3);

        Assert.Equal(TerrainType.Forest, forest.Type);
        Assert.Equal(2, forest.MovementCost);
        Assert.Equal(1, scout.RemainingMovement);
    }

    [Fact]
    public void UnitCannotEnterTerrainThatCostsMoreThanItsRemainingMovement()
    {
        var (match, host, _) = CreateStartedMatch();
        var armor = FindUnit(match, host.Id, UnitType.Armor);

        var exception = Assert.Throws<DomainRuleException>(() =>
            match.MoveUnit(host.Id, armor.Id, targetColumn: 1, targetRow: 5, expectedVersion: 3));

        Assert.Contains("needs 2 movement points", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal((1, 4), (armor.Column, armor.Row));
        Assert.Equal(1, armor.RemainingMovement);
        Assert.Equal(3, match.Version);
    }

    [Theory]
    [InlineData(TerrainType.Plain, 1)]
    [InlineData(TerrainType.Forest, 2)]
    [InlineData(TerrainType.Hill, 2)]
    [InlineData(TerrainType.Marsh, 3)]
    public void TerrainTypesPublishStableMovementCosts(TerrainType terrain, int expectedCost)
    {
        Assert.Equal(expectedCost, BattlefieldTerrain.MovementCostFor(terrain));
    }

    [Theory]
    [InlineData(TerrainType.Plain, 0)]
    [InlineData(TerrainType.Forest, 1)]
    [InlineData(TerrainType.Hill, 1)]
    [InlineData(TerrainType.Marsh, 0)]
    public void TerrainTypesPublishStableDefenseBonuses(TerrainType terrain, int expectedBonus)
    {
        Assert.Equal(expectedBonus, BattlefieldTerrain.DefenseBonusFor(terrain));
    }

    [Fact]
    public void UnitsRevealEnemyPositionsOnlyInsideTheirCombinedVisionRange()
    {
        var (match, host, guest) = CreateStartedMatch();
        var hostScout = FindUnit(match, host.Id, UnitType.Scout);
        var guestScout = FindUnit(match, guest.Id, UnitType.Scout);

        var initialVision = BattlefieldVision.ForPlayer(match, host.Id);

        Assert.Contains(new BattlefieldCoordinate(4, 2), initialVision);
        Assert.DoesNotContain(new BattlefieldCoordinate(5, 2), initialVision);
        Assert.DoesNotContain(new BattlefieldCoordinate(guestScout.Column, guestScout.Row), initialVision);
        Assert.Equal(3, hostScout.VisionRange);

        match.MoveUnit(host.Id, hostScout.Id, 2, 2, match.Version);
        match.MoveUnit(host.Id, hostScout.Id, 3, 2, match.Version);
        match.MoveUnit(host.Id, hostScout.Id, 4, 2, match.Version);

        var advancedVision = BattlefieldVision.ForPlayer(match, host.Id);

        Assert.Contains(new BattlefieldCoordinate(guestScout.Column, guestScout.Row), advancedVision);
    }

    [Fact]
    public void ForestDefenseReducesIncomingDamageToAMinimumOfOne()
    {
        var (match, host) = GameMatch.Create("Kuzey Geçidi", "Atlas", Now);
        var guest = match.Join("Poyraz", Now.AddSeconds(1));
        var hostScout = FindUnit(match, host.Id, UnitType.Scout);
        var hostInfantry = FindUnit(match, host.Id, UnitType.Infantry);
        var guestScout = FindUnit(match, guest.Id, UnitType.Scout);
        var guestInfantry = FindUnit(match, guest.Id, UnitType.Infantry);

        match.PlaceUnit(host.Id, hostScout.Id, 0, 0, match.Version);
        match.PlaceUnit(host.Id, hostInfantry.Id, 1, 2, match.Version);
        match.PlaceUnit(guest.Id, guestScout.Id, 8, 6, match.Version);
        match.PlaceUnit(guest.Id, guestInfantry.Id, 7, 1, match.Version);
        match.ReadyPlayer(host.Id, match.Version);
        match.ReadyPlayer(guest.Id, match.Version);

        foreach (var column in Enumerable.Range(2, 6))
        {
            if (hostInfantry.RemainingMovement == 0)
            {
                match.EndTurn(host.Id, match.Version);
                match.EndTurn(guest.Id, match.Version);
            }

            match.MoveUnit(host.Id, hostInfantry.Id, column, 2, match.Version);
        }

        match.AttackUnit(host.Id, hostInfantry.Id, guestInfantry.Id, match.Version);

        Assert.Equal(TerrainType.Forest, BattlefieldTerrain.At(7, 1).Type);
        Assert.Equal(1, BattlefieldTerrain.At(7, 1).DefenseBonus);
        Assert.Equal(3, guestInfantry.Health);
        Assert.Equal(1, match.Events.Last().Amount);
    }

    [Fact]
    public void AdjacentEnemyTakesDamageAndDestroyedUnitIsRemoved()
    {
        var (match, host, guest) = CreateStartedMatch();
        var hostScout = FindUnit(match, host.Id, UnitType.Scout);
        var guestScout = FindUnit(match, guest.Id, UnitType.Scout);

        match.MoveUnit(host.Id, hostScout.Id, 2, 2, expectedVersion: 3);
        match.MoveUnit(host.Id, hostScout.Id, 3, 2, expectedVersion: 4);
        match.MoveUnit(host.Id, hostScout.Id, 4, 2, expectedVersion: 5);
        match.EndTurn(host.Id, expectedVersion: 6);
        match.MoveUnit(guest.Id, guestScout.Id, 6, 2, expectedVersion: 7);
        match.MoveUnit(guest.Id, guestScout.Id, 5, 2, expectedVersion: 8);

        match.AttackUnit(guest.Id, guestScout.Id, hostScout.Id, expectedVersion: 9);

        Assert.Equal(1, hostScout.Health);
        Assert.True(guestScout.HasAttackedThisTurn);
        Assert.Equal(0, guestScout.RemainingMovement);
        var attackEvent = match.Events.Last();
        Assert.Equal(MatchEventType.UnitAttacked, attackEvent.Type);
        Assert.Equal(1, attackEvent.Amount);
        Assert.False(attackEvent.WasDestroyed);
        Assert.Equal(UnitType.Scout, attackEvent.TargetUnitType);

        var repeatException = Assert.Throws<DomainRuleException>(() =>
            match.AttackUnit(guest.Id, guestScout.Id, hostScout.Id, expectedVersion: 10));
        Assert.Contains("already attacked", repeatException.Message, StringComparison.OrdinalIgnoreCase);

        match.EndTurn(guest.Id, expectedVersion: 10);
        match.EndTurn(host.Id, expectedVersion: 11);
        match.AttackUnit(guest.Id, guestScout.Id, hostScout.Id, expectedVersion: 12);

        Assert.DoesNotContain(match.Units, unit => unit.Id == hostScout.Id);
        Assert.Equal(MatchStatus.InProgress, match.Status);
        Assert.True(match.Events.Last().WasDestroyed);
    }

    [Fact]
    public void HiddenTilesRevealOnceAndApplyReinforcementAndMineEffects()
    {
        var (match, host, guest) = CreateStartedMatch();
        var scout = FindUnit(match, host.Id, UnitType.Scout);
        var reinforcementTile = match.SpecialTiles
            .Where(tile => tile.Type == SpecialTileType.Reinforcement)
            .OrderBy(tile => tile.Column)
            .ThenBy(tile => tile.Row)
            .First();
        var initialUnitCount = match.Units.Count;

        MoveUnitToSpecialTile(match, host, guest, scout, reinforcementTile);

        Assert.True(reinforcementTile.IsRevealed);
        Assert.Equal(initialUnitCount + 1, match.Units.Count);
        var reinforcementEvent = match.Events.Last();
        Assert.Equal(MatchEventType.ReinforcementTriggered, reinforcementEvent.Type);
        var reinforcement = match.Units.Single(unit => unit.Id == reinforcementEvent.TargetUnitId);
        Assert.Equal(UnitType.Scout, reinforcement.Type);
        Assert.Equal(0, reinforcement.RemainingMovement);
        Assert.True(reinforcement.HasAttackedThisTurn);

        var mineTile = match.SpecialTiles.First(tile => tile.Type == SpecialTileType.Mine);
        MoveUnitToSpecialTile(match, host, guest, scout, mineTile);

        Assert.True(mineTile.IsRevealed);
        Assert.Equal(1, scout.Health);
        var mineEvent = match.Events.Last();
        Assert.Equal(MatchEventType.MineTriggered, mineEvent.Type);
        Assert.Equal(1, mineEvent.Amount);
        Assert.False(mineEvent.WasDestroyed);
        Assert.Equal(2, match.SpecialTiles.Count(tile => tile.IsRevealed));
    }

    [Fact]
    public void RematchRequiresBothPlayersAndSwapsTheirDeploymentSides()
    {
        var (match, host, guest) = CreateCompletedMatch();
        var completedAtUtc = Now.AddMinutes(5);

        Assert.Equal(MatchStatus.Completed, match.Status);
        Assert.Equal(host.Id, match.WinnerPlayerId);
        Assert.Equal(completedAtUtc, match.CompletedAtUtc);
        Assert.Null(match.TurnExpiresAtUtc);
        Assert.False(match.TryExpireTurn(completedAtUtc.AddDays(1)));
        Assert.Equal(63, BattlefieldVision.ForPlayer(match, host.Id).Count);
        Assert.Equal(63, BattlefieldVision.ForPlayer(match, guest.Id).Count);

        match.RequestRematch(host.Id, match.Version);

        Assert.Equal(host.Id, match.RematchRequestedByPlayerId);
        Assert.Equal(MatchEventType.RematchRequested, match.Events.Last().Type);
        var requesterException = Assert.Throws<DomainRuleException>(() =>
            match.AcceptRematch(host.Id, match.Version, Now.AddMinutes(6)));
        Assert.Contains("cannot accept", requesterException.Message, StringComparison.OrdinalIgnoreCase);

        var rematch = match.AcceptRematch(guest.Id, match.Version, Now.AddMinutes(6));

        Assert.Equal(rematch.Id, match.RematchMatchId);
        Assert.Equal(MatchEventType.RematchAccepted, match.Events.Last().Type);
        Assert.Equal(MatchStatus.Deploying, rematch.Status);
        Assert.Null(rematch.TurnExpiresAtUtc);
        Assert.Equal(1, rematch.Version);
        Assert.Equal(10, rematch.Units.Count);
        AssertStartingArmies(rematch);
        Assert.Equal(4, rematch.SpecialTiles.Count);
        Assert.Equal("Poyraz", rematch.Players.Single(player => player.Seat == 1).Name);
        Assert.Equal("Atlas", rematch.Players.Single(player => player.Seat == 2).Name);
    }

    private static GameUnit FindUnit(GameMatch match, Guid playerId, UnitType type) =>
        match.Units.Single(unit => unit.OwnerPlayerId == playerId && unit.Type == type
            && unit.Column == (match.Players.Single(player => player.Id == playerId).Seat == 1 ? 1 : 7));

    private static void AssertStartingArmies(GameMatch match)
    {
        Assert.Equal(10, match.Units.Select(unit => unit.Id).Distinct().Count());
        Assert.Equal(10, match.Units.Select(unit => (unit.Column, unit.Row)).Distinct().Count());
        foreach (var player in match.Players)
        {
            var army = match.Units.Where(unit => unit.OwnerPlayerId == player.Id).ToArray();
            Assert.Equal(5, army.Length);
            Assert.Single(army, unit => unit.Type == UnitType.Scout);
            Assert.Equal(3, army.Count(unit => unit.Type == UnitType.Infantry));
            Assert.Single(army, unit => unit.Type == UnitType.Armor);
            Assert.All(army, unit =>
            {
                Assert.InRange(unit.Column, player.Seat == 1 ? 0 : 7, player.Seat == 1 ? 1 : 8);
                Assert.InRange(unit.Row, 0, GameMatch.BoardRows - 1);
                Assert.Equal(unit.MaximumHealth, unit.Health);
                Assert.Equal(0, unit.RemainingMovement);
            });
            var reserveColumn = player.Seat == 1 ? 0 : 8;
            Assert.Equal(new[] { 3, 4 }, army.Where(unit => unit.Column == reserveColumn).Select(unit => unit.Row).Order().ToArray());
        }
    }

    private static (GameMatch Match, GamePlayer Host, GamePlayer Guest) CreateStartedMatch()
    {
        var (match, host) = GameMatch.Create("Kuzey Geçidi", "Atlas", Now);
        var guest = match.Join("Poyraz", Now.AddSeconds(1));
        match.ReadyPlayer(host.Id, expectedVersion: 1);
        match.ReadyPlayer(guest.Id, expectedVersion: 2);
        return (match, host, guest);
    }

    private static (GameMatch Match, GamePlayer Host, GamePlayer Guest) CreateCompletedMatch()
    {
        var (match, host, guest) = CreateStartedMatch();
        var scout = FindUnit(match, host.Id, UnitType.Scout);
        var path = new (int Column, int Row)[]
        {
            (2, 2), (3, 2), (4, 2), (5, 2), (6, 2), (6, 1), (7, 1), (8, 1)
        };

        foreach (var position in path)
        {
            var movementCost = BattlefieldTerrain.At(position.Column, position.Row).MovementCost;

            if (scout.RemainingMovement < movementCost)
            {
                match.EndTurn(host.Id, match.Version);
                match.EndTurn(guest.Id, match.Version);
            }

            match.MoveUnit(
                host.Id,
                scout.Id,
                position.Column,
                position.Row,
                match.Version,
                Now.AddMinutes(5));
        }

        return (match, host, guest);
    }

    private static void MoveUnitToSpecialTile(
        GameMatch match,
        GamePlayer host,
        GamePlayer guest,
        GameUnit unit,
        GameSpecialTile destination)
    {
        while (unit.Column != destination.Column || unit.Row != destination.Row)
        {
            var path = FindPath(match, unit, destination);
            var next = path[1];
            var movementCost = BattlefieldTerrain.At(next.Column, next.Row).MovementCost;

            if (unit.RemainingMovement < movementCost)
            {
                match.EndTurn(host.Id, match.Version);
                match.EndTurn(guest.Id, match.Version);
            }

            match.MoveUnit(host.Id, unit.Id, next.Column, next.Row, match.Version);
        }
    }

    private static IReadOnlyList<(int Column, int Row)> FindPath(
        GameMatch match,
        GameUnit unit,
        GameSpecialTile destination)
    {
        var start = (unit.Column, unit.Row);
        var target = (destination.Column, destination.Row);
        var queue = new Queue<(int Column, int Row)>();
        var previous = new Dictionary<(int Column, int Row), (int Column, int Row)>();
        var visited = new HashSet<(int Column, int Row)> { start };
        var occupied = match.Units
            .Where(candidate => candidate.Id != unit.Id)
            .Select(candidate => (candidate.Column, candidate.Row))
            .ToHashSet();
        var hiddenSpecialTiles = match.SpecialTiles
            .Where(tile => !tile.IsRevealed && tile.Id != destination.Id)
            .Select(tile => (tile.Column, tile.Row))
            .ToHashSet();
        queue.Enqueue(start);

        while (queue.TryDequeue(out var current))
        {
            if (current == target) break;

            foreach (var next in AdjacentPositions(current.Column, current.Row))
            {
                if (occupied.Contains(next) || hiddenSpecialTiles.Contains(next) || !visited.Add(next)) continue;
                previous[next] = current;
                queue.Enqueue(next);
            }
        }

        Assert.Contains(target, visited);
        var path = new List<(int Column, int Row)> { target };
        var step = target;

        while (step != start)
        {
            step = previous[step];
            path.Add(step);
        }

        path.Reverse();
        return path;
    }

    private static IEnumerable<(int Column, int Row)> AdjacentPositions(int column, int row)
    {
        var directions = row % 2 == 0
            ? new (int Column, int Row)[] { (-1, -1), (0, -1), (-1, 0), (1, 0), (-1, 1), (0, 1) }
            : [(0, -1), (1, -1), (-1, 0), (1, 0), (0, 1), (1, 1)];

        return directions
            .Select(direction => (Column: column + direction.Column, Row: row + direction.Row))
            .Where(position =>
                position.Column >= 0 && position.Column < GameMatch.BoardColumns
                && position.Row >= 0 && position.Row < GameMatch.BoardRows);
    }

    private static (int Column, int Row, int Movement, int Health, int Attack) Describe(GameUnit unit) =>
        (unit.Column, unit.Row, unit.RemainingMovement, unit.Health, unit.AttackPower);
}
