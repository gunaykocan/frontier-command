using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Game.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTurnDeadline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "turn_expires_at_utc",
                table: "game_matches",
                type: "timestamp with time zone",
                nullable: true);

            // Existing active matches receive one full turn on upgrade; no history is removed.
            migrationBuilder.Sql("""
                UPDATE game_matches
                SET turn_expires_at_utc = CURRENT_TIMESTAMP + INTERVAL '90 seconds',
                    version = version + 1
                WHERE status = 'InProgress';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_game_matches_status_turn_expires_at_utc",
                table: "game_matches",
                columns: new[] { "status", "turn_expires_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_game_matches_status_turn_expires_at_utc",
                table: "game_matches");

            migrationBuilder.DropColumn(
                name: "turn_expires_at_utc",
                table: "game_matches");
        }
    }
}
