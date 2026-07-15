using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GoldenHour.Api.Migrations
{
    /// <inheritdoc />
    public partial class IdempotentCommandsAndParticipantInvites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "CreateIdempotencyKeyHash",
                schema: "golden_hour",
                table: "EmergencySessions",
                type: "bytea",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "CreateRequestHash",
                schema: "golden_hour",
                table: "EmergencySessions",
                type: "bytea",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmergencySessions_CreateIdempotencyKeyHash",
                schema: "golden_hour",
                table: "EmergencySessions",
                column: "CreateIdempotencyKeyHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmergencySessions_CreateIdempotencyKeyHash",
                schema: "golden_hour",
                table: "EmergencySessions");

            migrationBuilder.DropColumn(
                name: "CreateIdempotencyKeyHash",
                schema: "golden_hour",
                table: "EmergencySessions");

            migrationBuilder.DropColumn(
                name: "CreateRequestHash",
                schema: "golden_hour",
                table: "EmergencySessions");
        }
    }
}
