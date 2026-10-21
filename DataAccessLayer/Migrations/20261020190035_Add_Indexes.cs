using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataAccessLayer.Migrations
{
    /// <inheritdoc />
    public partial class Add_Indexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_passes_user_id",
                table: "passes");

            migrationBuilder.DropIndex(
                name: "IX_enrollments_class_id",
                table: "enrollments");

            migrationBuilder.CreateIndex(
                name: "IX_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_passes_user_id",
                table: "passes",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_enrollments_class_id_status_user_id",
                table: "enrollments",
                columns: new[] { "class_id", "status", "user_id" })
                .Annotation("Npgsql:IndexInclude", new[] { "seats" });

            migrationBuilder.CreateIndex(
                name: "IX_enrollments_user_id",
                table: "enrollments",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_classes_starts_at_id",
                table: "classes",
                columns: new[] { "starts_at", "id" },
                filter: "is_cancelled = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_email",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_passes_user_id",
                table: "passes");

            migrationBuilder.DropIndex(
                name: "IX_enrollments_class_id_status_user_id",
                table: "enrollments");

            migrationBuilder.DropIndex(
                name: "IX_enrollments_user_id",
                table: "enrollments");

            migrationBuilder.DropIndex(
                name: "IX_classes_starts_at_id",
                table: "classes");

            migrationBuilder.CreateIndex(
                name: "IX_passes_user_id",
                table: "passes",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_enrollments_class_id",
                table: "enrollments",
                column: "class_id");
        }
    }
}
