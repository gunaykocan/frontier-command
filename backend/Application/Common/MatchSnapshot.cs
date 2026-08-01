using Game.Domain.Entities;
using Game.Domain.Enums;

namespace Game.Application.Common;

public sealed record MatchSnapshot(
    Guid Id,
    string Name,
    MatchStatus Status,
    int TurnNumber,
    int Version,
    Guid? ActivePlayerId,
    Guid? WinnerPlayerId,
    IReadOnlyList<PlayerSnapshot> Players,
    IReadOnlyList<UnitSnapshot> Units,
    DateTimeOffset CreatedAtUtc)
{
    public static MatchSnapshot From(GameMatch match) => new(
        match.Id,
        match.Name,
        match.Status,
        match.TurnNumber,
        match.Version,
        match.ActivePlayerId,
        match.WinnerPlayerId,
        match.Players
            .OrderBy(player => player.Seat)
            .Select(player => new PlayerSnapshot(player.Id, player.Name, player.Seat))
            .ToList(),
        match.Units
            .Select(unit => new UnitSnapshot(
                unit.Id,
                unit.OwnerPlayerId,
                unit.Column,
                unit.Row))
            .ToList(),
        match.CreatedAtUtc);
}

public sealed record PlayerSnapshot(Guid Id, string Name, int Seat);

public sealed record UnitSnapshot(
    Guid Id,
    Guid OwnerPlayerId,
    int Column,
    int Row);

public sealed record PlayerSession(Guid PlayerId, MatchSnapshot Match);
