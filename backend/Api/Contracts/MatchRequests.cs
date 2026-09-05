namespace Game.Api.Contracts;

public sealed record CreateMatchRequest(
    string MatchName,
    string PlayerName,
    bool PlayAgainstBot = false);

public sealed record JoinMatchRequest(string PlayerName);

public sealed record PlaceUnitRequest(
    Guid UnitId,
    int TargetColumn,
    int TargetRow,
    int ExpectedVersion);

public sealed record ReadyPlayerRequest(int ExpectedVersion);

public sealed record MoveUnitRequest(
    Guid UnitId,
    int TargetColumn,
    int TargetRow,
    int ExpectedVersion);

public sealed record AttackUnitRequest(
    Guid AttackerUnitId,
    Guid TargetUnitId,
    int ExpectedVersion);

public sealed record EndTurnRequest(int ExpectedVersion);

public sealed record RematchRequest(int ExpectedVersion);
