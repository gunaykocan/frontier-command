using Game.Domain.Entities;
using Game.Domain.Enums;
using Game.Domain.Rules;

namespace Game.Domain.Tests;

public sealed class BattlefieldVisionTests
{
    [Theory]
    [InlineData(TerrainType.Plain, false)]
    [InlineData(TerrainType.Forest, true)]
    [InlineData(TerrainType.Hill, true)]
    [InlineData(TerrainType.Marsh, false)]
    public void TerrainPublishesItsSightBlockingRule(TerrainType terrain, bool expected)
    {
        Assert.Equal(expected, BattlefieldTerrain.BlocksVisionFor(terrain));
    }

    [Theory]
    [InlineData(0, 1, 2, 1, false)] // Forest between origin and destination.
    [InlineData(2, 1, 4, 1, false)] // Hill between origin and destination.
    [InlineData(0, 1, 1, 1, true)]  // The blocking tile itself remains visible.
    [InlineData(1, 1, 3, 1, true)]  // Origin and destination do not block themselves.
    [InlineData(3, 3, 5, 3, true)]  // Marsh does not block vision.
    [InlineData(1, 2, 4, 2, true)]  // Open plain corridor.
    [InlineData(1, 2, 2, 1, true)]  // One side of a shared hex edge is open.
    [InlineData(2, 1, 4, 0, false)] // Both sides of the shared edge are blocked.
    public void LineOfSightRespectsInterveningTerrain(
        int fromColumn, int fromRow, int toColumn, int toRow, bool expected)
    {
        Assert.Equal(expected, BattlefieldVision.HasLineOfSight(fromColumn, fromRow, toColumn, toRow));
    }

    [Fact]
    public void VisibilityIsSymmetricAndEveryAdjacentCellIsVisible()
    {
        foreach (var from in BattlefieldTerrain.Tiles)
        {
            foreach (var to in BattlefieldTerrain.Tiles)
            {
                var forward = BattlefieldVision.HasLineOfSight(from.Column, from.Row, to.Column, to.Row);
                var reverse = BattlefieldVision.HasLineOfSight(to.Column, to.Row, from.Column, from.Row);
                Assert.Equal(forward, reverse);

                if (BattlefieldVision.HexDistance(from.Column, from.Row, to.Column, to.Row) <= 1)
                {
                    Assert.True(forward);
                }
            }
        }
    }

    [Fact]
    public void BothPlayersCanSeeTheirEntireDeploymentZoneDespiteTerrain()
    {
        var now = DateTimeOffset.UtcNow;
        var (match, _) = GameMatch.Create("Görüş testi", "Batı", now);
        match.Join("Doğu", now);

        foreach (var player in match.Players)
        {
            var visible = BattlefieldVision.ForPlayer(match, player.Id);
            var firstColumn = player.Seat == 1 ? 0 : 7;

            foreach (var column in Enumerable.Range(firstColumn, 2))
            {
                foreach (var row in Enumerable.Range(0, GameMatch.BoardRows))
                {
                    Assert.Contains(new BattlefieldCoordinate(column, row), visible);
                }
            }
        }
    }
}
