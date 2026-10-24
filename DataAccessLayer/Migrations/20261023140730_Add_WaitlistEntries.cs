using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DataAccessLayer.Migrations
{
    /// <inheritdoc />
    public partial class Add_WaitlistEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "waitlist_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    class_id = table.Column<long>(type: "bigint", nullable: false),
                    seats = table.Column<int>(type: "integer", nullable: false),
                    initial_position = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    idempotency_key_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_waitlist_entries", x => x.id);
                    table.ForeignKey(
                        name: "FK_waitlist_entries_classes_class_id",
                        column: x => x.class_id,
                        principalTable: "classes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_waitlist_entries_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_waitlist_entries_class_id_created_at_id",
                table: "waitlist_entries",
                columns: new[] { "class_id", "created_at", "id" },
                filter: "status = 'waiting'");

            migrationBuilder.CreateIndex(
                name: "IX_waitlist_entries_class_id_user_id",
                table: "waitlist_entries",
                columns: new[] { "class_id", "user_id" },
                unique: true,
                filter: "status = 'waiting'");

            migrationBuilder.CreateIndex(
                name: "IX_waitlist_entries_user_id_idempotency_key_hash",
                table: "waitlist_entries",
                columns: new[] { "user_id", "idempotency_key_hash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "waitlist_entries");
        }
    }
}
