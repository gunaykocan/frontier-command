using Game.Domain.Entities;
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

        base.OnModelCreating(modelBuilder);
    }

    private static void ConfigureMatch(ModelBuilder modelBuilder)
    {
        var match = modelBuilder.Entity<GameMatch>();

        match.ToTable("game_matches");
        match.HasKey(item => item.Id);
        match.Property(item => item.Id).HasColumnName("id");
        match.Property(item => item.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
        match.Property(item => item.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32);
        match.Property(item => item.TurnNumber).HasColumnName("turn_number");
        match.Property(item => item.Version).HasColumnName("version").IsConcurrencyToken();
        match.Property(item => item.ActivePlayerId).HasColumnName("active_player_id");
        match.Property(item => item.WinnerPlayerId).HasColumnName("winner_player_id");
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
    }

    private static void ConfigurePlayer(ModelBuilder modelBuilder)
    {
        var player = modelBuilder.Entity<GamePlayer>();

        player.ToTable("game_players");
        player.HasKey(item => item.Id);
        player.Property(item => item.Id).HasColumnName("id");
        player.Property(item => item.MatchId).HasColumnName("match_id");
        player.Property(item => item.Name).HasColumnName("name").HasMaxLength(40).IsRequired();
        player.Property(item => item.Seat).HasColumnName("seat");
        player.Property(item => item.JoinedAtUtc).HasColumnName("joined_at_utc");
        player.HasIndex(item => new { item.MatchId, item.Seat }).IsUnique();
    }

    private static void ConfigureUnit(ModelBuilder modelBuilder)
    {
        var unit = modelBuilder.Entity<GameUnit>();

        unit.ToTable("game_units");
        unit.HasKey(item => item.Id);
        unit.Property(item => item.Id).HasColumnName("id");
        unit.Property(item => item.MatchId).HasColumnName("match_id");
        unit.Property(item => item.OwnerPlayerId).HasColumnName("owner_player_id");
        unit.Property(item => item.Column).HasColumnName("column");
        unit.Property(item => item.Row).HasColumnName("row");
        unit.HasIndex(item => new { item.MatchId, item.Column, item.Row }).IsUnique();
    }
}
