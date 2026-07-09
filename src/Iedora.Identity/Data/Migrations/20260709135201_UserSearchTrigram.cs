using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Iedora.Identity.Data.Migrations
{
    /// <inheritdoc />
    public partial class UserSearchTrigram : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateIndex(
                name: "ix_users_displayname_trgm",
                schema: "identity",
                table: "AspNetUsers",
                column: "DisplayName")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_users_email_trgm",
                schema: "identity",
                table: "AspNetUsers",
                column: "Email")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_displayname_trgm",
                schema: "identity",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "ix_users_email_trgm",
                schema: "identity",
                table: "AspNetUsers");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }
    }
}
