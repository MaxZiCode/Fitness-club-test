using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataAccessLayer.Migrations
{
    /// <inheritdoc />
    public partial class Update_Enrollments_Add_InitialSeats_Make_IdempodencyKeyHash_Required : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_enrollments_user_id_idempotency_key_hash",
                table: "enrollments");

            migrationBuilder.AlterColumn<string>(
                name: "idempotency_key_hash",
                table: "enrollments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "initial_seats",
                table: "enrollments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_enrollments_user_id_idempotency_key_hash",
                table: "enrollments",
                columns: new[] { "user_id", "idempotency_key_hash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_enrollments_user_id_idempotency_key_hash",
                table: "enrollments");

            migrationBuilder.DropColumn(
                name: "initial_seats",
                table: "enrollments");

            migrationBuilder.AlterColumn<string>(
                name: "idempotency_key_hash",
                table: "enrollments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.CreateIndex(
                name: "IX_enrollments_user_id_idempotency_key_hash",
                table: "enrollments",
                columns: new[] { "user_id", "idempotency_key_hash" },
                unique: true,
                filter: "idempotency_key_hash IS NOT NULL");
        }
    }
}
