using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Game.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchEventLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "game_match_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    turn_number = table.Column<int>(type: "integer", nullable: false),
                    actor_player_id = table.Column<Guid>(type: "uuid", nullable: true),
                    related_player_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unit_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    target_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_unit_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    from_column = table.Column<int>(type: "integer", nullable: true),
                    from_row = table.Column<int>(type: "integer", nullable: true),
                    to_column = table.Column<int>(type: "integer", nullable: true),
                    to_row = table.Column<int>(type: "integer", nullable: true),
                    amount = table.Column<int>(type: "integer", nullable: true),
                    was_destroyed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_match_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_game_match_events_game_matches_match_id",
                        column: x => x.match_id,
                        principalTable: "game_matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_game_match_events_match_id_sequence",
                table: "game_match_events",
                columns: new[] { "match_id", "sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "game_match_events");
        }
    }
}
