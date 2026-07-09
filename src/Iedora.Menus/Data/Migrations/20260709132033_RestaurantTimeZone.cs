using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Iedora.Menus.Migrations
{
    /// <inheritdoc />
    public partial class RestaurantTimeZone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TimeZone",
                schema: "menu",
                table: "restaurants",
                type: "text",
                nullable: false,
                defaultValue: "UTC");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TimeZone",
                schema: "menu",
                table: "restaurants");
        }
    }
}
