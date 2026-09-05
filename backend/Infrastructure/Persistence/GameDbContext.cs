using Game.Domain.Entities;
using Game.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Game.Infrastructure.Persistence;

public sealed class GameDbContext(DbContextOptions<GameDbContext> options) : DbContext(options)
{
    public DbSet<GameMatch> Matches => Set<GameMatch>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureMatch(modelBuilder);
        ConfigurePlayer(modelBuilder);
        ConfigureUnit(modelBuilder);
        ConfigureMatchEvent(modelBuilder);
        ConfigureSpecialTile(modelBuilder);
        ConfigureEnemySighting(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    private static void ConfigureMatch(ModelBuilder modelBuilder)
    {
        var match = modelBuilder.Entity<GameMatch>();

        match.ToTable("game_matches");
        match.HasKey(item => item.Id);
        match.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
        match.Property(item => item.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
        match.Property(item => item.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32);
        match.Property(item => item.TurnNumber).HasColumnName("turn_number");
        match.Property(item => item.Version).HasColumnName("version").IsConcurrencyToken();
        match.Property(item => item.ActivePlayerId).HasColumnName("active_player_id");
        match.Property(item => item.TurnExpiresAtUtc).HasColumnName("turn_expires_at_utc");
        match.HasIndex(item => new { item.Status, item.TurnExpiresAtUtc });
        match.Property(item => item.WinnerPlayerId).HasColumnName("winner_player_id");
        match.Property(item => item.CompletedAtUtc).HasColumnName("completed_at_utc");
        match.Property(item => item.RematchRequestedByPlayerId).HasColumnName("rematch_requested_by_player_id");
        match.Property(item => item.RematchMatchId).HasColumnName("rematch_match_id");
        match.Property(item => item.CreatedAtUtc).HasColumnName("created_at_utc");

        match.HasMany(item => item.Players)
            .WithOne()
            .HasForeignKey(player => player.MatchId)
            .OnDelete(DeleteBehavior.Cascade);
        match.Navigation(item => item.Players).UsePropertyAccessMode(PropertyAccessMode.Field);

        match.HasMany(item => item.Units)
            .WithOne()
            .HasForeignKey(unit => unit.MatchId)
            .OnDelete(DeleteBehavior.Cascade);
        match.Navigation(item => item.Units).UsePropertyAccessMode(PropertyAccessMode.Field);

        match.HasMany(item => item.Events)
            .WithOne()
            .HasForeignKey(item => item.MatchId)
            .OnDelete(DeleteBehavior.Cascade);
        match.Navigation(item => item.Events).UsePropertyAccessMode(PropertyAccessMode.Field);

        match.HasMany(item => item.SpecialTiles)
            .WithOne()
            .HasForeignKey(item => item.MatchId)
            .OnDelete(DeleteBehavior.Cascade);
        match.Navigation(item => item.SpecialTiles).UsePropertyAccessMode(PropertyAccessMode.Field);

        match.HasMany(item => item.EnemySightings)
            .WithOne()
            .HasForeignKey(item => item.MatchId)
            .OnDelete(DeleteBehavior.Cascade);
        match.Navigation(item => item.EnemySightings).UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigurePlayer(ModelBuilder modelBuilder)
    {
        var player = modelBuilder.Entity<GamePlayer>();

        player.ToTable("game_players");
        player.HasKey(item => item.Id);
        player.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
        player.Property(item => item.MatchId).HasColumnName("match_id");
        player.Property(item => item.Name).HasColumnName("name").HasMaxLength(40).IsRequired();
        player.Property(item => item.Seat).HasColumnName("seat");
        player.Property(item => item.IsReady).HasColumnName("is_ready").HasDefaultValue(false);
        player.Property(item => item.JoinedAtUtc).HasColumnName("joined_at_utc");
        player.HasIndex(item => new { item.MatchId, item.Seat }).IsUnique();
    }

    private static void ConfigureUnit(ModelBuilder modelBuilder)
    {
        var unit = modelBuilder.Entity<GameUnit>();

        unit.ToTable("game_units");
        unit.HasKey(item => item.Id);
        unit.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
        unit.Property(item => item.MatchId).HasColumnName("match_id");
        unit.Property(item => item.OwnerPlayerId).HasColumnName("owner_player_id");
        unit.Property(item => item.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(24)
            .HasDefaultValue(UnitType.Infantry);
        unit.Property(item => item.Health).HasColumnName("health").HasDefaultValue(4);
        unit.Property(item => item.RemainingMovement).HasColumnName("remaining_movement").HasDefaultValue(0);
        unit.Property(item => item.HasAttackedThisTurn).HasColumnName("has_attacked_this_turn").HasDefaultValue(false);
        unit.Property(item => item.RevealedUntilTurnNumber).HasColumnName("revealed_until_turn_number");
        unit.Property(item => item.Column).HasColumnName("column");
        unit.Property(item => item.Row).HasColumnName("row");
        unit.HasIndex(item => new { item.MatchId, item.Column, item.Row }).IsUnique();
    }

    private static void ConfigureMatchEvent(ModelBuilder modelBuilder)
    {
        var matchEvent = modelBuilder.Entity<GameMatchEvent>();

        matchEvent.ToTable("game_match_events");
        matchEvent.HasKey(item => item.Id);
        matchEvent.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
        matchEvent.Property(item => item.MatchId).HasColumnName("match_id");
        matchEvent.Property(item => item.Sequence).HasColumnName("sequence");
        matchEvent.Property(item => item.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(32);
        matchEvent.Property(item => item.TurnNumber).HasColumnName("turn_number");
        matchEvent.Property(item => item.ActorPlayerId).HasColumnName("actor_player_id");
        matchEvent.Property(item => item.RelatedPlayerId).HasColumnName("related_player_id");
        matchEvent.Property(item => item.UnitId).HasColumnName("unit_id");
        matchEvent.Property(item => item.UnitType)
            .HasColumnName("unit_type")
            .HasConversion<string>()
            .HasMaxLength(24);
        matchEvent.Property(item => item.TargetUnitId).HasColumnName("target_unit_id");
        matchEvent.Property(item => item.TargetUnitType)
            .HasColumnName("target_unit_type")
            .HasConversion<string>()
            .HasMaxLength(24);
        matchEvent.Property(item => item.FromColumn).HasColumnName("from_column");
        matchEvent.Property(item => item.FromRow).HasColumnName("from_row");
        matchEvent.Property(item => item.ToColumn).HasColumnName("to_column");
        matchEvent.Property(item => item.ToRow).HasColumnName("to_row");
        matchEvent.Property(item => item.Amount).HasColumnName("amount");
        matchEvent.Property(item => item.WasDestroyed).HasColumnName("was_destroyed");
        matchEvent.Property(item => item.FromVisibleToSeats).HasColumnName("from_visible_to_seats");
        matchEvent.Property(item => item.ToVisibleToSeats).HasColumnName("to_visible_to_seats");
        matchEvent.HasIndex(item => new { item.MatchId, item.Sequence }).IsUnique();
    }

    private static void ConfigureEnemySighting(ModelBuilder modelBuilder)
    {
        var sighting = modelBuilder.Entity<EnemySighting>();
        sighting.ToTable("game_enemy_sightings");
        sighting.HasKey(item => item.Id);
        sighting.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
        sighting.Property(item => item.MatchId).HasColumnName("match_id");
        sighting.Property(item => item.ViewerPlayerId).HasColumnName("viewer_player_id");
        // No live-unit FK: losing a unit in the fog must not erase the opponent's memory.
        sighting.Property(item => item.EnemyUnitId).HasColumnName("enemy_unit_id");
        sighting.Property(item => item.UnitType).HasColumnName("unit_type").HasConversion<string>().HasMaxLength(24);
        sighting.Property(item => item.Column).HasColumnName("column");
        sighting.Property(item => item.Row).HasColumnName("row");
        sighting.Property(item => item.LastSeenTurnNumber).HasColumnName("last_seen_turn_number");
        sighting.HasIndex(item => new { item.MatchId, item.ViewerPlayerId, item.EnemyUnitId }).IsUnique();
    }

    private static void ConfigureSpecialTile(ModelBuilder modelBuilder)
    {
        var specialTile = modelBuilder.Entity<GameSpecialTile>();

        specialTile.ToTable("game_special_tiles");
        specialTile.HasKey(item => item.Id);
        specialTile.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
        specialTile.Property(item => item.MatchId).HasColumnName("match_id");
        specialTile.Property(item => item.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(24);
        specialTile.Property(item => item.Column).HasColumnName("column");
        specialTile.Property(item => item.Row).HasColumnName("row");
        specialTile.Property(item => item.IsRevealed).HasColumnName("is_revealed");
        specialTile.Property(item => item.RevealedByUnitId).HasColumnName("revealed_by_unit_id");
        specialTile.HasIndex(item => new { item.MatchId, item.Column, item.Row }).IsUnique();
    }
}
