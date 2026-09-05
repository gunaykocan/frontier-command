namespace Game.Application.Features.Bots;

public abstract record BotAction;

public sealed record BotMoveAction(
    Guid UnitId,
    int TargetColumn,
    int TargetRow) : BotAction;

public sealed record BotAttackAction(
    Guid AttackerUnitId,
    Guid TargetUnitId) : BotAction;
