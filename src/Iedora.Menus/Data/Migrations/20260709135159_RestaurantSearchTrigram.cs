using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Iedora.Menus.Data.Migrations
{
    /// <inheritdoc />
    public partial class RestaurantSearchTrigram : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateIndex(
                name: "ix_restaurants_name_trgm",
                schema: "menu",
                table: "restaurants",
                column: "Name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_restaurants_slug_trgm",
                schema: "menu",
                table: "restaurants",
                column: "Slug")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_restaurants_name_trgm",
                schema: "menu",
                table: "restaurants");

            migrationBuilder.DropIndex(
                name: "ix_restaurants_slug_trgm",
                schema: "menu",
                table: "restaurants");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }
    }
}
