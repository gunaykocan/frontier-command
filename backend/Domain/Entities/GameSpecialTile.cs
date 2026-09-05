using Game.Domain.Enums;

namespace Game.Domain.Entities;

public sealed class GameSpecialTile
{
    private GameSpecialTile()
    {
    }

    private GameSpecialTile(
        Guid id,
        Guid matchId,
        SpecialTileType type,
        int column,
        int row)
    {
        Id = id;
        MatchId = matchId;
        Type = type;
        Column = column;
        Row = row;
    }

    public Guid Id { get; private set; }

    public Guid MatchId { get; private set; }

    public SpecialTileType Type { get; private set; }

    public int Column { get; private set; }

    public int Row { get; private set; }

    public bool IsRevealed { get; private set; }

    public Guid? RevealedByUnitId { get; private set; }

    internal static GameSpecialTile Create(
        Guid matchId,
        SpecialTileType type,
        int column,
        int row) =>
        new(Guid.NewGuid(), matchId, type, column, row);

    internal void Reveal(Guid unitId)
    {
        if (IsRevealed) return;

        IsRevealed = true;
        RevealedByUnitId = unitId;
    }
}
