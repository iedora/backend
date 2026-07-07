using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Iedora.Menus.Migrations
{
    /// <inheritdoc />
    public partial class MenuInit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "menu");

            migrationBuilder.CreateTable(
                name: "restaurants",
                schema: "menu",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    DescriptionI18n = table.Column<string>(type: "jsonb", nullable: true),
                    LogoUrl = table.Column<string>(type: "text", nullable: true),
                    BannerUrl = table.Column<string>(type: "text", nullable: true),
                    Theme = table.Column<string>(type: "jsonb", nullable: true),
                    DefaultLanguage = table.Column<string>(type: "text", nullable: false, defaultValue: "en"),
                    SupportedLanguages = table.Column<List<string>>(type: "text[]", nullable: false),
                    DefaultCurrency = table.Column<string>(type: "text", nullable: false, defaultValue: "EUR"),
                    OnboardingCompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_restaurants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "menus",
                schema: "menu",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    NameI18n = table.Column<string>(type: "jsonb", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    DescriptionI18n = table.Column<string>(type: "jsonb", nullable: true),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_menus", x => x.Id);
                    table.ForeignKey(
                        name: "FK_menus_restaurants_RestaurantId",
                        column: x => x.RestaurantId,
                        principalSchema: "menu",
                        principalTable: "restaurants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "categories",
                schema: "menu",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    MenuId = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    NameI18n = table.Column<string>(type: "jsonb", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    DescriptionI18n = table.Column<string>(type: "jsonb", nullable: true),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    TranslationsSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_categories_menus_MenuId",
                        column: x => x.MenuId,
                        principalSchema: "menu",
                        principalTable: "menus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_categories_restaurants_RestaurantId",
                        column: x => x.RestaurantId,
                        principalSchema: "menu",
                        principalTable: "restaurants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "items",
                schema: "menu",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    NameI18n = table.Column<string>(type: "jsonb", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    DescriptionI18n = table.Column<string>(type: "jsonb", nullable: true),
                    PriceCents = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false, defaultValue: "EUR"),
                    ImageUrl = table.Column<string>(type: "text", nullable: true),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Available = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    Variants = table.Column<string>(type: "jsonb", nullable: true),
                    TranslationsSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_items_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalSchema: "menu",
                        principalTable: "categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_items_restaurants_RestaurantId",
                        column: x => x.RestaurantId,
                        principalSchema: "menu",
                        principalTable: "restaurants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_categories_MenuId",
                schema: "menu",
                table: "categories",
                column: "MenuId");

            migrationBuilder.CreateIndex(
                name: "IX_categories_RestaurantId",
                schema: "menu",
                table: "categories",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_items_CategoryId",
                schema: "menu",
                table: "items",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_items_RestaurantId",
                schema: "menu",
                table: "items",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_menus_RestaurantId",
                schema: "menu",
                table: "menus",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_restaurants_Slug",
                schema: "menu",
                table: "restaurants",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_restaurants_TenantId",
                schema: "menu",
                table: "restaurants",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "items",
                schema: "menu");

            migrationBuilder.DropTable(
                name: "categories",
                schema: "menu");

            migrationBuilder.DropTable(
                name: "menus",
                schema: "menu");

            migrationBuilder.DropTable(
                name: "restaurants",
                schema: "menu");
        }
    }
}
