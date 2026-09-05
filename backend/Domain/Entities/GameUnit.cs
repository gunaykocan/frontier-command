using Game.Domain.Enums;
using Game.Domain.Rules;

namespace Game.Domain.Entities;

public sealed class GameUnit
{
    private GameUnit()
    {
    }

    private GameUnit(
        Guid id,
        Guid matchId,
        Guid ownerPlayerId,
        UnitType type,
        int column,
        int row)
    {
        Id = id;
        MatchId = matchId;
        OwnerPlayerId = ownerPlayerId;
        Type = type;
        Health = MaximumHealth;
        Column = column;
        Row = row;
    }

    public Guid Id { get; private set; }

    public Guid MatchId { get; private set; }

    public Guid OwnerPlayerId { get; private set; }

    public UnitType Type { get; private set; }

    public int Health { get; private set; }

    public int RemainingMovement { get; private set; }

    public bool HasAttackedThisTurn { get; private set; }

    public int? RevealedUntilTurnNumber { get; private set; }

    public bool IsRevealedByAttack(int turnNumber) => RevealedUntilTurnNumber >= turnNumber;

    public int Column { get; private set; }

    public int Row { get; private set; }

    public int MaximumHealth => UnitProfiles.For(Type).MaximumHealth;

    public int MovementAllowance => UnitProfiles.For(Type).MovementAllowance;

    public int AttackPower => UnitProfiles.For(Type).AttackPower;

    public int VisionRange => UnitProfiles.For(Type).VisionRange;

    public bool IsDestroyed => Health <= 0;

    internal static GameUnit Create(
        Guid matchId,
        Guid ownerPlayerId,
        UnitType type,
        int column,
        int row) =>
        new(Guid.NewGuid(), matchId, ownerPlayerId, type, column, row);

    internal void BeginTurn()
    {
        RemainingMovement = MovementAllowance;
        HasAttackedThisTurn = false;
    }

    internal void EndTurn()
    {
        RemainingMovement = 0;
    }

    internal void ExhaustForCurrentTurn()
    {
        RemainingMovement = 0;
        HasAttackedThisTurn = true;
    }

    internal void MoveTo(int column, int row, int movementCost)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(movementCost);

        Column = column;
        Row = row;
        RemainingMovement -= movementCost;
    }

    internal void PlaceAt(int column, int row)
    {
        Column = column;
        Row = row;
    }

    internal void MarkAttackUsed()
    {
        HasAttackedThisTurn = true;
        RemainingMovement = 0;
    }

    internal void RevealByAttack(int currentTurnNumber)
    {
        // Include the opponent's next turn; hide again when the attacker's next turn begins.
        RevealedUntilTurnNumber = currentTurnNumber + 1;
    }

    internal void ReceiveDamage(int damage)
    {
        Health = Math.Max(0, Health - damage);
    }
}
