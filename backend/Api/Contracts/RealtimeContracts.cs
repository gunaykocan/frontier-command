namespace Game.Api.Contracts;

public sealed record ServerReadyMessage(
    string ConnectionId,
    DateTimeOffset ConnectedAtUtc,
    string Transport);

public sealed record MatchPresenceMessage(
    Guid MatchId,
    string ConnectionId,
    DateTimeOffset ChangedAtUtc);
