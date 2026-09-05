using Game.Domain.Entities;
using Game.Domain.Enums;

namespace Game.Domain.Rules;

public readonly record struct BattlefieldCoordinate(int Column, int Row);

public static class BattlefieldVision
{
    public static IReadOnlySet<BattlefieldCoordinate> ForPlayer(GameMatch match, Guid playerId)
    {
        var player = match.Players.SingleOrDefault(player => player.Id == playerId);

        if (player is null)
        {
            throw new DomainRuleException("The player does not belong to this match.");
        }

        if (match.Status is MatchStatus.WaitingForPlayers or MatchStatus.Completed)
        {
            return AllCoordinates();
        }

        var visible = new HashSet<BattlefieldCoordinate>();

        if (match.Status is MatchStatus.Deploying)
        {
            var firstColumn = player.Seat == 1 ? 0 : GameMatch.BoardColumns - GameMatch.DeploymentColumns;

            for (var column = firstColumn; column < firstColumn + GameMatch.DeploymentColumns; column++)
            {
                for (var row = 0; row < GameMatch.BoardRows; row++)
                {
                    visible.Add(new BattlefieldCoordinate(column, row));
                }
            }
        }

        foreach (var unit in match.Units.Where(unit => unit.OwnerPlayerId == playerId))
        {
            for (var row = 0; row < BattlefieldTerrain.Rows; row++)
            {
                for (var column = 0; column < BattlefieldTerrain.Columns; column++)
                {
                    if (HexDistance(unit.Column, unit.Row, column, row) <= unit.VisionRange
                        && HasLineOfSight(unit.Column, unit.Row, column, row))
                    {
                        visible.Add(new BattlefieldCoordinate(column, row));
                    }
                }
            }
        }

        if (match.Status is MatchStatus.InProgress)
        {
            foreach (var enemy in match.Units.Where(unit =>
                unit.OwnerPlayerId != playerId && unit.IsRevealedByAttack(match.TurnNumber)))
            {
                // Gunfire reveals this cell, not a new scouting radius around it.
                visible.Add(new BattlefieldCoordinate(enemy.Column, enemy.Row));
            }
        }

        return visible;
    }

    public static int HexDistance(int fromColumn, int fromRow, int toColumn, int toRow)
    {
        var fromQ = fromColumn - (fromRow - (fromRow & 1)) / 2;
        var toQ = toColumn - (toRow - (toRow & 1)) / 2;
        var deltaQ = fromQ - toQ;
        var deltaR = fromRow - toRow;

        return (Math.Abs(deltaQ) + Math.Abs(deltaQ + deltaR) + Math.Abs(deltaR)) / 2;
    }

    public static bool HasLineOfSight(int fromColumn, int fromRow, int toColumn, int toRow)
    {
        _ = BattlefieldTerrain.At(fromColumn, fromRow);
        _ = BattlefieldTerrain.At(toColumn, toRow);
        var distance = HexDistance(fromColumn, fromRow, toColumn, toRow);

        if (distance <= 1) return true;

        // A line along a shared hex edge is open if either side is clear.
        // Using both nudges also keeps visibility identical in both directions.
        return IsLineClear(fromColumn, fromRow, toColumn, toRow, distance, 0.000001)
            || IsLineClear(fromColumn, fromRow, toColumn, toRow, distance, -0.000001);
    }

    private static bool IsLineClear(
        int fromColumn,
        int fromRow,
        int toColumn,
        int toRow,
        int distance,
        double nudge)
    {
        var fromQ = fromColumn - (fromRow - (fromRow & 1)) / 2;
        var toQ = toColumn - (toRow - (toRow & 1)) / 2;

        // The origin and destination never block themselves; only intervening terrain does.
        for (var step = 1; step < distance; step++)
        {
            var progress = (double)step / distance;
            var x = fromQ + (toQ - fromQ) * progress + nudge;
            var z = fromRow + (toRow - fromRow) * progress - 2 * nudge;
            var y = -fromQ - fromRow + (-toQ - toRow + fromQ + fromRow) * progress + nudge;
            var roundedX = (int)Math.Round(x);
            var roundedY = (int)Math.Round(y);
            var roundedZ = (int)Math.Round(z);
            var xDifference = Math.Abs(roundedX - x);
            var yDifference = Math.Abs(roundedY - y);
            var zDifference = Math.Abs(roundedZ - z);

            if (xDifference > yDifference && xDifference > zDifference)
            {
                roundedX = -roundedY - roundedZ;
            }
            else if (yDifference <= zDifference)
            {
                roundedZ = -roundedX - roundedY;
            }

            var column = roundedX + (roundedZ - (roundedZ & 1)) / 2;
            var row = roundedZ;

            if (column is < 0 or >= BattlefieldTerrain.Columns
                || row is < 0 or >= BattlefieldTerrain.Rows
                || BattlefieldTerrain.At(column, row).BlocksVision)
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlySet<BattlefieldCoordinate> AllCoordinates() =>
        Enumerable.Range(0, BattlefieldTerrain.Rows)
            .SelectMany(row => Enumerable.Range(0, BattlefieldTerrain.Columns)
                .Select(column => new BattlefieldCoordinate(column, row)))
            .ToHashSet();
}
