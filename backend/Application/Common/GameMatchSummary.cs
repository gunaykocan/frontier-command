using Game.Domain.Enums;

namespace Game.Application.Common;

public sealed record GameMatchSummary(
    Guid Id,
    string Name,
    MatchStatus Status,
    int TurnNumber,
    int Version,
    int PlayerCount,
    int PlayerCapacity,
    DateTimeOffset CreatedAtUtc);
