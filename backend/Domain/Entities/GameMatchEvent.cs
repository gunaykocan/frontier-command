using Game.Domain.Enums;

namespace Game.Domain.Entities;

public sealed class GameMatchEvent
{
    private GameMatchEvent()
    {
    }

    private GameMatchEvent(
        Guid id,
        Guid matchId,
        int sequence,
        MatchEventType type,
        int turnNumber,
        Guid? actorPlayerId,
        Guid? relatedPlayerId,
        Guid? unitId,
        UnitType? unitType,
        Guid? targetUnitId,
        UnitType? targetUnitType,
        int? fromColumn,
        int? fromRow,
        int? toColumn,
        int? toRow,
        int? amount,
        bool wasDestroyed)
    {
        Id = id;
        MatchId = matchId;
        Sequence = sequence;
        Type = type;
        TurnNumber = turnNumber;
        ActorPlayerId = actorPlayerId;
        RelatedPlayerId = relatedPlayerId;
        UnitId = unitId;
        UnitType = unitType;
        TargetUnitId = targetUnitId;
        TargetUnitType = targetUnitType;
        FromColumn = fromColumn;
        FromRow = fromRow;
        ToColumn = toColumn;
        ToRow = toRow;
        Amount = amount;
        WasDestroyed = wasDestroyed;
    }

    public Guid Id { get; private set; }

    public Guid MatchId { get; private set; }

    public int Sequence { get; private set; }

    public MatchEventType Type { get; private set; }

    public int TurnNumber { get; private set; }

    public Guid? ActorPlayerId { get; private set; }

    public Guid? RelatedPlayerId { get; private set; }

    public Guid? UnitId { get; private set; }

    public UnitType? UnitType { get; private set; }

    public Guid? TargetUnitId { get; private set; }

    public UnitType? TargetUnitType { get; private set; }

    public int? FromColumn { get; private set; }

    public int? FromRow { get; private set; }

    public int? ToColumn { get; private set; }

    public int? ToRow { get; private set; }

    public int? Amount { get; private set; }

    public bool WasDestroyed { get; private set; }

    public int FromVisibleToSeats { get; private set; }

    public int ToVisibleToSeats { get; private set; }

    public bool WasFromVisibleTo(int seat) => seat is 1 or 2 && (FromVisibleToSeats & (1 << (seat - 1))) != 0;

    public bool WasToVisibleTo(int seat) => seat is 1 or 2 && (ToVisibleToSeats & (1 << (seat - 1))) != 0;

    internal void RecordVisibility(int seat, bool fromVisible, bool toVisible)
    {
        // Two bits store who could see each endpoint when the event actually happened.
        var seatMask = 1 << (seat - 1);
        if (fromVisible) FromVisibleToSeats |= seatMask;
        if (toVisible) ToVisibleToSeats |= seatMask;
    }

    internal static GameMatchEvent Create(
        Guid matchId,
        int sequence,
        MatchEventType type,
        int turnNumber,
        Guid? actorPlayerId = null,
        Guid? relatedPlayerId = null,
        Guid? unitId = null,
        UnitType? unitType = null,
        Guid? targetUnitId = null,
        UnitType? targetUnitType = null,
        int? fromColumn = null,
        int? fromRow = null,
        int? toColumn = null,
        int? toRow = null,
        int? amount = null,
        bool wasDestroyed = false) =>
        new(
            Guid.NewGuid(),
            matchId,
            sequence,
            type,
            turnNumber,
            actorPlayerId,
            relatedPlayerId,
            unitId,
            unitType,
            targetUnitId,
            targetUnitType,
            fromColumn,
            fromRow,
            toColumn,
            toRow,
            amount,
            wasDestroyed);
}
