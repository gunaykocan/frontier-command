using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Game.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReconnaissance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "revealed_until_turn_number",
                table: "game_units",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "from_visible_to_seats",
                table: "game_match_events",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "to_visible_to_seats",
                table: "game_match_events",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "game_enemy_sightings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    viewer_player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enemy_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    column = table.Column<int>(type: "integer", nullable: false),
                    row = table.Column<int>(type: "integer", nullable: false),
                    last_seen_turn_number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_enemy_sightings", x => x.id);
                    table.ForeignKey(
                        name: "FK_game_enemy_sightings_game_matches_match_id",
                        column: x => x.match_id,
                        principalTable: "game_matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_game_enemy_sightings_match_id_viewer_player_id_enemy_unit_id",
                table: "game_enemy_sightings",
                columns: new[] { "match_id", "viewer_player_id", "enemy_unit_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "game_enemy_sightings");

            migrationBuilder.DropColumn(
                name: "revealed_until_turn_number",
                table: "game_units");

            migrationBuilder.DropColumn(
                name: "from_visible_to_seats",
                table: "game_match_events");

            migrationBuilder.DropColumn(
                name: "to_visible_to_seats",
                table: "game_match_events");
        }
    }
}
