using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Game.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUnitCombatStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "has_attacked_this_turn",
                table: "game_units",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "health",
                table: "game_units",
                type: "integer",
                nullable: false,
                defaultValue: 4);

            migrationBuilder.AddColumn<int>(
                name: "remaining_movement",
                table: "game_units",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "type",
                table: "game_units",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "Infantry");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "has_attacked_this_turn",
                table: "game_units");

            migrationBuilder.DropColumn(
                name: "health",
                table: "game_units");

            migrationBuilder.DropColumn(
                name: "remaining_movement",
                table: "game_units");

            migrationBuilder.DropColumn(
                name: "type",
                table: "game_units");
        }
    }
}
