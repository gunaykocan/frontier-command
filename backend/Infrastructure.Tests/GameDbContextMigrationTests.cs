using Game.Domain.Entities;
using Game.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Game.Infrastructure.Tests;

public sealed class GameDbContextMigrationTests
{
    [Fact]
    public void InitialMigrationIsRegistered()
    {
        using var dbContext = CreateDbContext();

        var migrations = dbContext.Database.GetMigrations();

        Assert.Contains(migrations, migration => migration.EndsWith("_InitialCreate", StringComparison.Ordinal));
    }

    [Fact]
    public void UnitCombatMigrationAndPropertiesAreRegistered()
    {
        using var dbContext = CreateDbContext();

        var migrations = dbContext.Database.GetMigrations();
        var unit = dbContext.Model.FindEntityType(typeof(GameUnit));

        Assert.Contains(migrations, migration => migration.EndsWith("_AddUnitCombatStats", StringComparison.Ordinal));
        Assert.Equal("type", unit?.FindProperty(nameof(GameUnit.Type))?.GetColumnName());
        Assert.Equal("health", unit?.FindProperty(nameof(GameUnit.Health))?.GetColumnName());
        Assert.Equal(
            "remaining_movement",
            unit?.FindProperty(nameof(GameUnit.RemainingMovement))?.GetColumnName());
        Assert.Equal(
            "has_attacked_this_turn",
            unit?.FindProperty(nameof(GameUnit.HasAttackedThisTurn))?.GetColumnName());
    }

    [Fact]
    public void PlayerDeploymentReadinessMigrationAndPropertyAreRegistered()
    {
        using var dbContext = CreateDbContext();

        var migrations = dbContext.Database.GetMigrations();
        var player = dbContext.Model.FindEntityType(typeof(GamePlayer));

        Assert.Contains(
            migrations,
            migration => migration.EndsWith("_AddPlayerDeploymentReady", StringComparison.Ordinal));
        Assert.Equal("is_ready", player?.FindProperty(nameof(GamePlayer.IsReady))?.GetColumnName());
    }

    [Fact]
    public void BotPlayerPropertyIsRegistered()
    {
        using var dbContext = CreateDbContext();
        var migrations = dbContext.Database.GetMigrations();
        var player = dbContext.Model.FindEntityType(typeof(GamePlayer));

        Assert.Contains(migrations, migration => migration.EndsWith("_AddBotPlayer", StringComparison.Ordinal));
        Assert.Equal("is_bot", player?.FindProperty(nameof(GamePlayer.IsBot))?.GetColumnName());
        Assert.False((bool?)player?.FindProperty(nameof(GamePlayer.IsBot))?.GetDefaultValue());
    }

    [Fact]
    public void MatchVersionRemainsAConcurrencyToken()
    {
        using var dbContext = CreateDbContext();

        var match = dbContext.Model.FindEntityType(typeof(GameMatch));
        var version = match?.FindProperty(nameof(GameMatch.Version));

        Assert.NotNull(match);
        Assert.Equal("game_matches", match.GetTableName());
        Assert.NotNull(version);
        Assert.True(version.IsConcurrencyToken);
    }

    [Fact]
    public void MatchEventsAreMappedAsAnOrderedAggregateCollection()
    {
        using var dbContext = CreateDbContext();

        var migrations = dbContext.Database.GetMigrations();
        var matchEvent = dbContext.Model.FindEntityType(typeof(GameMatchEvent));

        Assert.Contains(
            migrations,
            migration => migration.EndsWith("_AddMatchEventLog", StringComparison.Ordinal));
        Assert.Equal("game_match_events", matchEvent?.GetTableName());
        Assert.Equal("sequence", matchEvent?.FindProperty(nameof(GameMatchEvent.Sequence))?.GetColumnName());
        Assert.Equal("type", matchEvent?.FindProperty(nameof(GameMatchEvent.Type))?.GetColumnName());
    }

    [Fact]
    public void HiddenSpecialTilesArePersistedWithoutClientSideCoordinates()
    {
        using var dbContext = CreateDbContext();

        var migrations = dbContext.Database.GetMigrations();
        var specialTile = dbContext.Model.FindEntityType(typeof(GameSpecialTile));

        Assert.Contains(
            migrations,
            migration => migration.EndsWith("_AddHiddenSpecialTiles", StringComparison.Ordinal));
        Assert.Equal("game_special_tiles", specialTile?.GetTableName());
        Assert.Equal("is_revealed", specialTile?.FindProperty(nameof(GameSpecialTile.IsRevealed))?.GetColumnName());
    }

    [Fact]
    public void MatchRematchFlowMigrationAndPropertiesAreRegistered()
    {
        using var dbContext = CreateDbContext();

        var migrations = dbContext.Database.GetMigrations();
        var match = dbContext.Model.FindEntityType(typeof(GameMatch));

        Assert.Contains(
            migrations,
            migration => migration.EndsWith("_AddMatchRematchFlow", StringComparison.Ordinal));
        Assert.Equal(
            "completed_at_utc",
            match?.FindProperty(nameof(GameMatch.CompletedAtUtc))?.GetColumnName());
        Assert.Equal(
            "rematch_requested_by_player_id",
            match?.FindProperty(nameof(GameMatch.RematchRequestedByPlayerId))?.GetColumnName());
        Assert.Equal(
            "rematch_match_id",
            match?.FindProperty(nameof(GameMatch.RematchMatchId))?.GetColumnName());
    }

    [Fact]
    public void AggregateIdentifiersAreGeneratedByTheDomain()
    {
        using var dbContext = CreateDbContext();

        Assert.Equal(
            ValueGenerated.Never,
            dbContext.Model.FindEntityType(typeof(GameMatch))?.FindProperty(nameof(GameMatch.Id))?.ValueGenerated);
        Assert.Equal(
            ValueGenerated.Never,
            dbContext.Model.FindEntityType(typeof(GamePlayer))?.FindProperty(nameof(GamePlayer.Id))?.ValueGenerated);
        Assert.Equal(
            ValueGenerated.Never,
            dbContext.Model.FindEntityType(typeof(GameUnit))?.FindProperty(nameof(GameUnit.Id))?.ValueGenerated);
        Assert.Equal(
            ValueGenerated.Never,
            dbContext.Model.FindEntityType(typeof(GameMatchEvent))?.FindProperty(nameof(GameMatchEvent.Id))?.ValueGenerated);
        Assert.Equal(
            ValueGenerated.Never,
            dbContext.Model.FindEntityType(typeof(GameSpecialTile))?.FindProperty(nameof(GameSpecialTile.Id))?.ValueGenerated);
    }

    [Fact]
    public void TurnDeadlineIsPersistedAndIndexedAndModelMatchesMigrations()
    {
        using var dbContext = CreateDbContext();
        var match = dbContext.Model.FindEntityType(typeof(GameMatch));
        Assert.Contains(dbContext.Database.GetMigrations(), migration => migration.EndsWith("_AddTurnDeadline", StringComparison.Ordinal));
        Assert.Equal("turn_expires_at_utc", match?.FindProperty(nameof(GameMatch.TurnExpiresAtUtc))?.GetColumnName());
        Assert.Contains(match!.GetIndexes(), index => index.Properties.Select(property => property.Name)
            .SequenceEqual([nameof(GameMatch.Status), nameof(GameMatch.TurnExpiresAtUtc)]));
        Assert.False(dbContext.Database.HasPendingModelChanges());
    }

    [Fact]
    public void ReconnaissanceIsViewerSpecificAndHasNoLiveUnitForeignKey()
    {
        using var dbContext = CreateDbContext();
        var sighting = dbContext.Model.FindEntityType(typeof(EnemySighting))!;
        Assert.Contains(dbContext.Database.GetMigrations(), migration => migration.EndsWith("_AddReconnaissance", StringComparison.Ordinal));
        Assert.Equal("game_enemy_sightings", sighting.GetTableName());
        Assert.Equal(ValueGenerated.Never, sighting.FindProperty(nameof(EnemySighting.Id))!.ValueGenerated);
        Assert.Contains(sighting.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name)
            .SequenceEqual([nameof(EnemySighting.MatchId), nameof(EnemySighting.ViewerPlayerId), nameof(EnemySighting.EnemyUnitId)]));
        Assert.DoesNotContain(sighting.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(GameUnit));
        Assert.Equal("revealed_until_turn_number", dbContext.Model.FindEntityType(typeof(GameUnit))!
            .FindProperty(nameof(GameUnit.RevealedUntilTurnNumber))!.GetColumnName());
        Assert.Equal("from_visible_to_seats", dbContext.Model.FindEntityType(typeof(GameMatchEvent))!
            .FindProperty(nameof(GameMatchEvent.FromVisibleToSeats))!.GetColumnName());
        Assert.Equal("to_visible_to_seats", dbContext.Model.FindEntityType(typeof(GameMatchEvent))!
            .FindProperty(nameof(GameMatchEvent.ToVisibleToSeats))!.GetColumnName());
    }

    private static GameDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<GameDbContext>()
            .UseNpgsql("Host=localhost;Database=game;Username=game")
            .Options;

        return new GameDbContext(options);
    }
}
