namespace Game.Application.Features.Commands;

public sealed record CreateMatchCommand(string MatchName, string PlayerName);

public sealed record JoinMatchCommand(Guid MatchId, string PlayerName);

public sealed record MoveUnitCommand(
    Guid MatchId,
    Guid PlayerId,
    Guid UnitId,
    int TargetColumn,
    int TargetRow,
    int ExpectedVersion);
