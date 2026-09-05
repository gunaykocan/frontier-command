using Game.Domain.Entities;
using Game.Domain.Rules;

namespace Game.Application.Features.Bots;

public sealed class BalancedBotTurnPlanner : IBotTurnPlanner
{
    public BotAction? PlanNextAction(GameMatch match, Guid botPlayerId)
    {
        var bot = match.Players.Single(player => player.Id == botPlayerId && player.IsBot);
        var botUnits = match.Units.Where(unit => unit.OwnerPlayerId == botPlayerId).ToArray();
        var visible = BattlefieldVision.ForPlayer(match, botPlayerId);
        var visibleEnemies = match.Units
            .Where(unit => unit.OwnerPlayerId != botPlayerId)
            .Where(unit => visible.Contains(new BattlefieldCoordinate(unit.Column, unit.Row)))
            .ToArray();

        var attack = botUnits
            .Where(unit => !unit.HasAttackedThisTurn)
            .SelectMany(attacker => visibleEnemies
                .Where(target => BattlefieldVision.HexDistance(
                    attacker.Column,
                    attacker.Row,
                    target.Column,
                    target.Row) == 1)
                .Select(target => new
                {
                    Attacker = attacker,
                    Target = target,
                    Damage = Math.Max(
                        1,
                        attacker.AttackPower - BattlefieldTerrain.At(target.Column, target.Row).DefenseBonus)
                }))
            .OrderByDescending(candidate => candidate.Damage >= candidate.Target.Health)
            .ThenBy(candidate => candidate.Target.Health)
            .ThenByDescending(candidate => candidate.Damage)
            .ThenBy(candidate => candidate.Attacker.Id)
            .FirstOrDefault();

        if (attack is not null)
        {
            return new BotAttackAction(attack.Attacker.Id, attack.Target.Id);
        }

        var occupied = match.Units
            .Select(unit => new BattlefieldCoordinate(unit.Column, unit.Row))
            .ToHashSet();
        var enemyEdge = bot.Seat == 1 ? GameMatch.BoardColumns - 1 : 0;

        var move = botUnits
            .Where(unit => unit.RemainingMovement > 0)
            .SelectMany(unit => BattlefieldTerrain.Tiles
                .Where(tile => BattlefieldVision.HexDistance(
                    unit.Column,
                    unit.Row,
                    tile.Column,
                    tile.Row) == 1)
                .Where(tile => tile.MovementCost <= unit.RemainingMovement)
                .Where(tile => !occupied.Contains(new BattlefieldCoordinate(tile.Column, tile.Row)))
                .Select(tile => new
                {
                    Unit = unit,
                    Tile = tile,
                    CurrentDistance = DistanceToObjective(unit.Column, unit.Row, enemyEdge, visibleEnemies),
                    NextDistance = DistanceToObjective(tile.Column, tile.Row, enemyEdge, visibleEnemies)
                }))
            .Where(candidate => candidate.NextDistance < candidate.CurrentDistance)
            .OrderByDescending(candidate => candidate.CurrentDistance - candidate.NextDistance)
            .ThenByDescending(candidate => candidate.Tile.DefenseBonus)
            .ThenBy(candidate => candidate.Tile.MovementCost)
            .ThenBy(candidate => candidate.Unit.Id)
            .FirstOrDefault();

        return move is null
            ? null
            : new BotMoveAction(move.Unit.Id, move.Tile.Column, move.Tile.Row);
    }

    private static int DistanceToObjective(
        int column,
        int row,
        int enemyEdge,
        IReadOnlyCollection<GameUnit> visibleEnemies)
    {
        if (visibleEnemies.Count > 0)
        {
            return visibleEnemies.Min(enemy => BattlefieldVision.HexDistance(
                column,
                row,
                enemy.Column,
                enemy.Row));
        }

        return BattlefieldVision.HexDistance(column, row, enemyEdge, row);
    }
}
