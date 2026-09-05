using Game.Domain.Enums;

namespace Game.Domain.Rules;

public readonly record struct TerrainTile(
    int Column,
    int Row,
    TerrainType Type,
    int MovementCost,
    int DefenseBonus,
    bool BlocksVision);

public static class BattlefieldTerrain
{
    public const int Columns = 9;
    public const int Rows = 7;

    private static readonly string[] Layout =
    [
        "PPHFMFHPP",
        "PFPHPHPFP",
        "PPPPPPPPP",
        "PPPHMHPPP",
        "PPPPPPPPP",
        "PFPHPHPFP",
        "PPHFMFHPP"
    ];

    private static readonly TerrainTile[] AllTiles = BuildTiles();

    public static IReadOnlyList<TerrainTile> Tiles => AllTiles;

    public static TerrainTile At(int column, int row)
    {
        if (column is < 0 or >= Columns || row is < 0 or >= Rows)
        {
            throw new ArgumentOutOfRangeException(nameof(column), "Terrain coordinates must be inside the battlefield.");
        }

        return AllTiles[(row * Columns) + column];
    }

    public static int MovementCostFor(TerrainType type) => type switch
    {
        TerrainType.Plain => 1,
        TerrainType.Forest => 2,
        TerrainType.Hill => 2,
        TerrainType.Marsh => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown terrain type.")
    };

    public static int DefenseBonusFor(TerrainType type) => type switch
    {
        TerrainType.Forest => 1,
        TerrainType.Hill => 1,
        TerrainType.Plain => 0,
        TerrainType.Marsh => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown terrain type.")
    };

    public static bool BlocksVisionFor(TerrainType type) => type switch
    {
        TerrainType.Forest or TerrainType.Hill => true,
        TerrainType.Plain or TerrainType.Marsh => false,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown terrain type.")
    };

    private static TerrainTile[] BuildTiles()
    {
        var tiles = new TerrainTile[Columns * Rows];

        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                var type = Parse(Layout[row][column]);
                tiles[(row * Columns) + column] = new TerrainTile(
                    column,
                    row,
                    type,
                    MovementCostFor(type),
                    DefenseBonusFor(type),
                    BlocksVisionFor(type));
            }
        }

        return tiles;
    }

    private static TerrainType Parse(char value) => value switch
    {
        'P' => TerrainType.Plain,
        'F' => TerrainType.Forest,
        'H' => TerrainType.Hill,
        'M' => TerrainType.Marsh,
        _ => throw new InvalidOperationException($"Unknown terrain map marker '{value}'.")
    };
}
