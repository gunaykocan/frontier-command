namespace Game.Domain.Entities;

public sealed class GameUnit
{
    private GameUnit()
    {
    }

    private GameUnit(Guid id, Guid matchId, Guid ownerPlayerId, int column, int row)
    {
        Id = id;
        MatchId = matchId;
        OwnerPlayerId = ownerPlayerId;
        Column = column;
        Row = row;
    }

    public Guid Id { get; private set; }

    public Guid MatchId { get; private set; }

    public Guid OwnerPlayerId { get; private set; }

    public int Column { get; private set; }

    public int Row { get; private set; }

    internal static GameUnit Create(Guid matchId, Guid ownerPlayerId, int column, int row) =>
        new(Guid.NewGuid(), matchId, ownerPlayerId, column, row);

    internal void MoveTo(int column, int row)
    {
        Column = column;
        Row = row;
    }
}
