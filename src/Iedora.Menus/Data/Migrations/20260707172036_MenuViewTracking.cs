using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Iedora.Menus.Migrations
{
    /// <inheritdoc />
    public partial class MenuViewTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "daily_view",
                schema: "menu",
                columns: table => new
                {
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Day = table.Column<string>(type: "text", nullable: false),
                    Language = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_view", x => new { x.RestaurantId, x.Day, x.Language });
                    table.ForeignKey(
                        name: "FK_daily_view_restaurants_RestaurantId",
                        column: x => x.RestaurantId,
                        principalSchema: "menu",
                        principalTable: "restaurants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "item_view",
                schema: "menu",
                columns: table => new
                {
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Day = table.Column<string>(type: "text", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_view", x => new { x.ItemId, x.Day });
                    table.ForeignKey(
                        name: "FK_item_view_items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "menu",
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_item_view_restaurants_RestaurantId",
                        column: x => x.RestaurantId,
                        principalSchema: "menu",
                        principalTable: "restaurants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "item_view_seen",
                schema: "menu",
                columns: table => new
                {
                    VisitorId = table.Column<string>(type: "text", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Day = table.Column<string>(type: "text", nullable: false),
                    SeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_view_seen", x => new { x.VisitorId, x.ItemId, x.Day });
                    table.ForeignKey(
                        name: "FK_item_view_seen_items_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "menu",
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "menu_session",
                schema: "menu",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Day = table.Column<string>(type: "text", nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_menu_session", x => x.Id);
                    table.ForeignKey(
                        name: "FK_menu_session_restaurants_RestaurantId",
                        column: x => x.RestaurantId,
                        principalSchema: "menu",
                        principalTable: "restaurants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "view_seen",
                schema: "menu",
                columns: table => new
                {
                    VisitorId = table.Column<string>(type: "text", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    HourBucket = table.Column<string>(type: "text", nullable: false),
                    SeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_view_seen", x => new { x.VisitorId, x.RestaurantId, x.HourBucket });
                    table.ForeignKey(
                        name: "FK_view_seen_restaurants_RestaurantId",
                        column: x => x.RestaurantId,
                        principalSchema: "menu",
                        principalTable: "restaurants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_daily_view_TenantId_Day",
                schema: "menu",
                table: "daily_view",
                columns: new[] { "TenantId", "Day" });

            migrationBuilder.CreateIndex(
                name: "IX_item_view_RestaurantId",
                schema: "menu",
                table: "item_view",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_item_view_TenantId_Day",
                schema: "menu",
                table: "item_view",
                columns: new[] { "TenantId", "Day" });

            migrationBuilder.CreateIndex(
                name: "IX_item_view_seen_ItemId",
                schema: "menu",
                table: "item_view_seen",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_item_view_seen_SeenAt",
                schema: "menu",
                table: "item_view_seen",
                column: "SeenAt");

            migrationBuilder.CreateIndex(
                name: "IX_menu_session_RestaurantId",
                schema: "menu",
                table: "menu_session",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_menu_session_TenantId_Day",
                schema: "menu",
                table: "menu_session",
                columns: new[] { "TenantId", "Day" });

            migrationBuilder.CreateIndex(
                name: "IX_view_seen_RestaurantId",
                schema: "menu",
                table: "view_seen",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_view_seen_SeenAt",
                schema: "menu",
                table: "view_seen",
                column: "SeenAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_view",
                schema: "menu");

            migrationBuilder.DropTable(
                name: "item_view",
                schema: "menu");

            migrationBuilder.DropTable(
                name: "item_view_seen",
                schema: "menu");

            migrationBuilder.DropTable(
                name: "menu_session",
                schema: "menu");

            migrationBuilder.DropTable(
                name: "view_seen",
                schema: "menu");
        }
    }
}
