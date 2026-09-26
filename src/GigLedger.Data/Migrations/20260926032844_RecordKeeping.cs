using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GigLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class RecordKeeping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CorrectionReason",
                table: "Trips",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrectionReason",
                table: "TripActuals",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrectionReason",
                table: "Tips",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrectionReason",
                table: "Shifts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrectionReason",
                table: "ShiftCloses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrectionReason",
                table: "Settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrectionReason",
                table: "HomeRates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrectionReason",
                table: "ChargeSessions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Attachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Owner = table.Column<string>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", nullable: false),
                    Content = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SupersedesId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CorrectionReason = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attachments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Drives",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    StartOdometer = table.Column<decimal>(type: "TEXT", nullable: false),
                    StartOdometerGrade = table.Column<string>(type: "TEXT", nullable: false),
                    EndOdometer = table.Column<decimal>(type: "TEXT", nullable: false),
                    EndOdometerGrade = table.Column<string>(type: "TEXT", nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    ShiftId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RecordedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SupersedesId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CorrectionReason = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Drives", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Expenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    AmountGrade = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SupersedesId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CorrectionReason = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expenses", x => x.Id);
                });

            // EF generated UpdateData here to set CorrectionReason = null on the two seeded rows.
            // A new nullable column is already null, and the append-only triggers (correctly)
            // refuse any UPDATE, which aborted the whole migration. Removed by hand.

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_Owner_OwnerId",
                table: "Attachments",
                columns: new[] { "Owner", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Drives_ShiftId",
                table: "Drives",
                column: "ShiftId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Attachments");

            migrationBuilder.DropTable(
                name: "Drives");

            migrationBuilder.DropTable(
                name: "Expenses");

            migrationBuilder.DropColumn(
                name: "CorrectionReason",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "CorrectionReason",
                table: "TripActuals");

            migrationBuilder.DropColumn(
                name: "CorrectionReason",
                table: "Tips");

            migrationBuilder.DropColumn(
                name: "CorrectionReason",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "CorrectionReason",
                table: "ShiftCloses");

            migrationBuilder.DropColumn(
                name: "CorrectionReason",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "CorrectionReason",
                table: "HomeRates");

            migrationBuilder.DropColumn(
                name: "CorrectionReason",
                table: "ChargeSessions");
        }
    }
}
