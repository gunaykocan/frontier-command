using Game.Domain.Enums;

namespace Game.Domain.Rules;

public readonly record struct UnitProfile(
    int MaximumHealth,
    int MovementAllowance,
    int AttackPower,
    int VisionRange);

public static class UnitProfiles
{
    public static UnitProfile For(UnitType type) => type switch
    {
        UnitType.Scout => new UnitProfile(MaximumHealth: 2, MovementAllowance: 3, AttackPower: 1, VisionRange: 3),
        UnitType.Infantry => new UnitProfile(MaximumHealth: 4, MovementAllowance: 2, AttackPower: 2, VisionRange: 1),
        UnitType.Armor => new UnitProfile(MaximumHealth: 6, MovementAllowance: 1, AttackPower: 3, VisionRange: 1),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown unit type.")
    };
}
