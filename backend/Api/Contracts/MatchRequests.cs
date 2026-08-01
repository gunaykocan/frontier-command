namespace Game.Api.Contracts;

public sealed record CreateMatchRequest(string MatchName, string PlayerName);

public sealed record JoinMatchRequest(string PlayerName);

public sealed record MoveUnitRequest(
    Guid PlayerId,
    Guid UnitId,
    int TargetColumn,
    int TargetRow,
    int ExpectedVersion);
