using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Armament.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CharacterClassSpec : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BaseClassId",
                table: "characters",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SpecId",
                table: "characters",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BaseClassId",
                table: "characters");

            migrationBuilder.DropColumn(
                name: "SpecId",
                table: "characters");
        }
    }
}
