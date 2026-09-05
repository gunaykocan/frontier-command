namespace Game.Application.Features.Commands;

public sealed record CreateMatchCommand(string MatchName, string PlayerName);

public sealed record JoinMatchCommand(Guid MatchId, string PlayerName);

public sealed record PlaceUnitCommand(
    Guid MatchId,
    Guid PlayerId,
    Guid UnitId,
    int TargetColumn,
    int TargetRow,
    int ExpectedVersion);

public sealed record ReadyPlayerCommand(
    Guid MatchId,
    Guid PlayerId,
    int ExpectedVersion);

public sealed record MoveUnitCommand(
    Guid MatchId,
    Guid PlayerId,
    Guid UnitId,
    int TargetColumn,
    int TargetRow,
    int ExpectedVersion);

public sealed record AttackUnitCommand(
    Guid MatchId,
    Guid PlayerId,
    Guid AttackerUnitId,
    Guid TargetUnitId,
    int ExpectedVersion);

public sealed record EndTurnCommand(
    Guid MatchId,
    Guid PlayerId,
    int ExpectedVersion);

public sealed record RequestRematchCommand(
    Guid MatchId,
    Guid PlayerId,
    int ExpectedVersion);

public sealed record AcceptRematchCommand(
    Guid MatchId,
    Guid PlayerId,
    int ExpectedVersion);

public sealed record EnterRematchCommand(
    Guid MatchId,
    Guid PlayerId);
