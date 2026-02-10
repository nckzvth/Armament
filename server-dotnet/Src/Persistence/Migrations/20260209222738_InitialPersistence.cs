using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Armament.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "characters",
                columns: table => new
                {
                    CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    Experience = table.Column<long>(type: "bigint", nullable: false),
                    Might = table.Column<int>(type: "integer", nullable: false),
                    Will = table.Column<int>(type: "integer", nullable: false),
                    Alacrity = table.Column<int>(type: "integer", nullable: false),
                    Constitution = table.Column<int>(type: "integer", nullable: false),
                    MaxHealth = table.Column<int>(type: "integer", nullable: false),
                    MaxBuilderResource = table.Column<int>(type: "integer", nullable: false),
                    MaxSpenderResource = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<int>(type: "integer", nullable: false),
                    GearJson = table.Column<string>(type: "jsonb", nullable: false),
                    InventoryJson = table.Column<string>(type: "jsonb", nullable: false),
                    LearnedRecipesJson = table.Column<string>(type: "jsonb", nullable: false),
                    QuestProgressJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_characters", x => x.CharacterId);
                });

            migrationBuilder.CreateTable(
                name: "inventory_items",
                columns: table => new
                {
                    InventoryItemId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_items", x => x.InventoryItemId);
                    table.ForeignKey(
                        name: "FK_inventory_items_characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "characters",
                        principalColumn: "CharacterId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "learned_recipes",
                columns: table => new
                {
                    LearnedRecipeId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipeCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learned_recipes", x => x.LearnedRecipeId);
                    table.ForeignKey(
                        name: "FK_learned_recipes_characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "characters",
                        principalColumn: "CharacterId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "loot_claims",
                columns: table => new
                {
                    LootClaimId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
                    PickupToken = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ClaimedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_loot_claims", x => x.LootClaimId);
                    table.ForeignKey(
                        name: "FK_loot_claims_characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "characters",
                        principalColumn: "CharacterId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "quest_progress",
                columns: table => new
                {
                    QuestProgressId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CharacterId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProgressValue = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quest_progress", x => x.QuestProgressId);
                    table.ForeignKey(
                        name: "FK_quest_progress_characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "characters",
                        principalColumn: "CharacterId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_characters_name",
                table: "characters",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "ux_inventory_character_item",
                table: "inventory_items",
                columns: new[] { "CharacterId", "ItemCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_recipe_character_code",
                table: "learned_recipes",
                columns: new[] { "CharacterId", "RecipeCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_loot_claims_CharacterId",
                table: "loot_claims",
                column: "CharacterId");

            migrationBuilder.CreateIndex(
                name: "ux_loot_claims_pickup_token",
                table: "loot_claims",
                column: "PickupToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_quest_character_code",
                table: "quest_progress",
                columns: new[] { "CharacterId", "QuestCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventory_items");

            migrationBuilder.DropTable(
                name: "learned_recipes");

            migrationBuilder.DropTable(
                name: "loot_claims");

            migrationBuilder.DropTable(
                name: "quest_progress");

            migrationBuilder.DropTable(
                name: "characters");
        }
    }
}
