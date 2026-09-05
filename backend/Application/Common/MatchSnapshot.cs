using Game.Domain.Entities;
using Game.Domain.Enums;
using Game.Domain.Rules;

namespace Game.Application.Common;

public sealed record MatchSnapshot(
    Guid Id,
    string Name,
    MatchStatus Status,
    int TurnNumber,
    int Version,
    Guid? ActivePlayerId,
    DateTimeOffset? TurnExpiresAtUtc,
    int TurnDurationSeconds,
    DateTimeOffset ServerTimeUtc,
    Guid? WinnerPlayerId,
    DateTimeOffset? CompletedAtUtc,
    Guid? RematchRequestedByPlayerId,
    Guid? RematchMatchId,
    IReadOnlyList<PlayerSnapshot> Players,
    IReadOnlyList<TerrainTileSnapshot> TerrainTiles,
    IReadOnlyList<VisibleCoordinateSnapshot> VisibleCoordinates,
    IReadOnlyList<UnitSnapshot> Units,
    IReadOnlyList<LastKnownEnemySnapshot> LastKnownEnemies,
    IReadOnlyList<SpecialTileSnapshot> RevealedSpecialTiles,
    IReadOnlyList<MatchEventSnapshot> Events,
    DateTimeOffset CreatedAtUtc)
{
    public static MatchSnapshot From(GameMatch match, Guid? viewerPlayerId = null, DateTimeOffset? serverTimeUtc = null)
    {
        var visibleCoordinates = viewerPlayerId is null
            ? AllCoordinates()
            : BattlefieldVision.ForPlayer(match, viewerPlayerId.Value);
        var viewerUnitIds = ViewerUnitIds(match, viewerPlayerId);
        var viewerSeat = match.Players.SingleOrDefault(player => player.Id == viewerPlayerId)?.Seat ?? 0;
        var units = match.Units
            .Where(unit => viewerPlayerId is null
                || unit.OwnerPlayerId == viewerPlayerId
                || visibleCoordinates.Contains(new BattlefieldCoordinate(unit.Column, unit.Row)))
            .Select(unit => new UnitSnapshot(
                unit.Id,
                unit.OwnerPlayerId,
                unit.Type,
                unit.Health,
                unit.MaximumHealth,
                unit.RemainingMovement,
                unit.MovementAllowance,
                unit.AttackPower,
                unit.VisionRange,
                unit.HasAttackedThisTurn,
                match.Status is MatchStatus.InProgress && unit.IsRevealedByAttack(match.TurnNumber),
                unit.Column,
                unit.Row))
            .ToList();
        var events = match.Events
            .OrderBy(item => item.Sequence)
            .Where(item => IsEventVisible(item, match.Status, viewerPlayerId, viewerSeat, viewerUnitIds))
            .Select(item => ToEventSnapshot(item, match.Status, viewerPlayerId, viewerSeat, viewerUnitIds))
            .ToList();
        var visibleUnitIds = units.Select(unit => unit.Id).ToHashSet();
        var lastKnownEnemies = match.EnemySightings
            .Where(sighting => match.Status is MatchStatus.InProgress
                && sighting.ViewerPlayerId == viewerPlayerId
                && !visibleUnitIds.Contains(sighting.EnemyUnitId)
                && !visibleCoordinates.Contains(new BattlefieldCoordinate(sighting.Column, sighting.Row)))
            .OrderBy(sighting => sighting.EnemyUnitId)
            .Select(sighting => new LastKnownEnemySnapshot(
                sighting.EnemyUnitId, sighting.UnitType, sighting.Column, sighting.Row, sighting.LastSeenTurnNumber))
            .ToList();

        return new MatchSnapshot(
        match.Id,
        match.Name,
        match.Status,
        match.TurnNumber,
        match.Version,
        match.ActivePlayerId,
        match.TurnExpiresAtUtc,
        GameMatch.TurnDurationSeconds,
        serverTimeUtc ?? DateTimeOffset.UtcNow,
        match.WinnerPlayerId,
        match.CompletedAtUtc,
        match.RematchRequestedByPlayerId,
        match.RematchMatchId,
        match.Players
            .OrderBy(player => player.Seat)
            .Select(player => new PlayerSnapshot(
                player.Id,
                player.Name,
                player.Seat,
                player.IsReady,
                player.IsBot))
            .ToList(),
        BattlefieldTerrain.Tiles
            .Select(tile => new TerrainTileSnapshot(
                tile.Column,
                tile.Row,
                tile.Type,
                tile.MovementCost,
                tile.DefenseBonus,
                tile.BlocksVision))
            .ToList(),
        visibleCoordinates
            .OrderBy(coordinate => coordinate.Row)
            .ThenBy(coordinate => coordinate.Column)
            .Select(coordinate => new VisibleCoordinateSnapshot(coordinate.Column, coordinate.Row))
            .ToList(),
        units,
        lastKnownEnemies,
        match.SpecialTiles
            .Where(tile => tile.IsRevealed && (
                viewerPlayerId is null
                || visibleCoordinates.Contains(new BattlefieldCoordinate(tile.Column, tile.Row))))
            .Select(tile => new SpecialTileSnapshot(
                tile.Type,
                tile.Column,
                tile.Row))
            .ToList(),
        events,
        match.CreatedAtUtc);

    }

    private static IReadOnlySet<BattlefieldCoordinate> AllCoordinates() =>
        BattlefieldTerrain.Tiles
            .Select(tile => new BattlefieldCoordinate(tile.Column, tile.Row))
            .ToHashSet();

    private static IReadOnlySet<Guid> ViewerUnitIds(GameMatch match, Guid? viewerPlayerId)
    {
        if (viewerPlayerId is null) return new HashSet<Guid>();

        var unitIds = match.Units
            .Where(unit => unit.OwnerPlayerId == viewerPlayerId)
            .Select(unit => unit.Id)
            .ToHashSet();

        foreach (var item in match.Events.Where(item => item.ActorPlayerId == viewerPlayerId))
        {
            if (item.UnitId is Guid unitId) unitIds.Add(unitId);

            if (item.Type is MatchEventType.ReinforcementTriggered && item.TargetUnitId is Guid targetUnitId)
            {
                unitIds.Add(targetUnitId);
            }
        }

        return unitIds;
    }

    private static bool IsEventVisible(
        GameMatchEvent item,
        MatchStatus status,
        Guid? viewerPlayerId,
        int viewerSeat,
        IReadOnlySet<Guid> viewerUnitIds)
    {
        if (viewerPlayerId is null || status is MatchStatus.Completed) return true;

        if (item.Type is MatchEventType.DeploymentStarted
            or MatchEventType.PlayerReady
            or MatchEventType.BattleStarted
            or MatchEventType.TurnEnded
            or MatchEventType.TurnTimedOut
            or MatchEventType.MatchCompleted
            or MatchEventType.RematchRequested
            or MatchEventType.RematchAccepted)
        {
            return true;
        }

        if (item.ActorPlayerId == viewerPlayerId || item.RelatedPlayerId == viewerPlayerId)
        {
            return true;
        }

        if ((item.UnitId is Guid unitId && viewerUnitIds.Contains(unitId))
            || (item.TargetUnitId is Guid targetUnitId && viewerUnitIds.Contains(targetUnitId)))
        {
            return true;
        }

        return item.WasFromVisibleTo(viewerSeat) || item.WasToVisibleTo(viewerSeat);
    }

    private static MatchEventSnapshot ToEventSnapshot(
        GameMatchEvent item,
        MatchStatus status,
        Guid? viewerPlayerId,
        int viewerSeat,
        IReadOnlySet<Guid> viewerUnitIds)
    {
        var revealEntireEvent = viewerPlayerId is null
            || status is MatchStatus.Completed
            || item.ActorPlayerId == viewerPlayerId
            || item.RelatedPlayerId == viewerPlayerId
            || (item.UnitId is Guid unitId && viewerUnitIds.Contains(unitId))
            || (item.TargetUnitId is Guid targetUnitId && viewerUnitIds.Contains(targetUnitId));
        var revealFrom = revealEntireEvent || item.WasFromVisibleTo(viewerSeat);
        var revealTo = revealEntireEvent || item.WasToVisibleTo(viewerSeat);
        var defenseBonus = item.Type is MatchEventType.UnitAttacked
            && item.ToColumn is int toColumn
            && item.ToRow is int toRow
                ? BattlefieldTerrain.At(toColumn, toRow).DefenseBonus
                : 0;

        return new MatchEventSnapshot(
            item.Sequence,
            item.Type,
            item.TurnNumber,
            item.ActorPlayerId,
            item.RelatedPlayerId,
            item.UnitId,
            item.UnitType,
            item.TargetUnitId,
            item.TargetUnitType,
            revealFrom ? item.FromColumn : null,
            revealFrom ? item.FromRow : null,
            revealTo ? item.ToColumn : null,
            revealTo ? item.ToRow : null,
            item.Amount,
            defenseBonus,
            item.WasDestroyed);
    }

}

public sealed record PlayerSnapshot(Guid Id, string Name, int Seat, bool IsReady, bool IsBot);

public sealed record LastKnownEnemySnapshot(Guid UnitId, UnitType UnitType, int Column, int Row, int LastSeenTurnNumber);

public sealed record TerrainTileSnapshot(
    int Column,
    int Row,
    TerrainType Type,
    int MovementCost,
    int DefenseBonus,
    bool BlocksVision);

public sealed record VisibleCoordinateSnapshot(int Column, int Row);

public sealed record UnitSnapshot(
    Guid Id,
    Guid OwnerPlayerId,
    UnitType Type,
    int Health,
    int MaximumHealth,
    int RemainingMovement,
    int MovementAllowance,
    int AttackPower,
    int VisionRange,
    bool HasAttackedThisTurn,
    bool IsRevealedByAttack,
    int Column,
    int Row);

public sealed record SpecialTileSnapshot(
    SpecialTileType Type,
    int Column,
    int Row);

public sealed record MatchEventSnapshot(
    int Sequence,
    MatchEventType Type,
    int TurnNumber,
    Guid? ActorPlayerId,
    Guid? RelatedPlayerId,
    Guid? UnitId,
    UnitType? UnitType,
    Guid? TargetUnitId,
    UnitType? TargetUnitType,
    int? FromColumn,
    int? FromRow,
    int? ToColumn,
    int? ToRow,
    int? Amount,
    int DefenseBonus,
    bool WasDestroyed);

public sealed record PlayerSession(Guid PlayerId, MatchSnapshot Match);
