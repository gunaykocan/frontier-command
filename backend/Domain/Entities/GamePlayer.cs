namespace Game.Domain.Entities;

public sealed class GamePlayer
{
    private GamePlayer()
    {
    }

    private GamePlayer(Guid id, Guid matchId, string name, int seat, DateTimeOffset joinedAtUtc)
    {
        Id = id;
        MatchId = matchId;
        Name = name;
        Seat = seat;
        JoinedAtUtc = joinedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid MatchId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public int Seat { get; private set; }

    public bool IsReady { get; private set; }

    public DateTimeOffset JoinedAtUtc { get; private set; }

    internal static GamePlayer Create(Guid matchId, string name, int seat, DateTimeOffset joinedAtUtc) =>
        new(Guid.NewGuid(), matchId, name, seat, joinedAtUtc);

    internal void MarkReady()
    {
        IsReady = true;
    }
}
