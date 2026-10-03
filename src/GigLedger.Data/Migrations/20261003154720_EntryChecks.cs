using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GigLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class EntryChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EntryMarks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Record = table.Column<string>(type: "TEXT", nullable: false),
                    RecordId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Limit = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<decimal>(type: "TEXT", nullable: false),
                    Passed = table.Column<decimal>(type: "TEXT", nullable: false),
                    Level = table.Column<string>(type: "TEXT", nullable: false),
                    Explanation = table.Column<string>(type: "TEXT", nullable: true),
                    At = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SupersedesId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CorrectionReason = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntryMarks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Limits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayConfirm = table.Column<decimal>(type: "TEXT", nullable: false),
                    PayDocument = table.Column<decimal>(type: "TEXT", nullable: false),
                    SpeedConfirm = table.Column<decimal>(type: "TEXT", nullable: false),
                    SpeedDocument = table.Column<decimal>(type: "TEXT", nullable: false),
                    TripLengthConfirm = table.Column<decimal>(type: "TEXT", nullable: false),
                    TripLengthDocument = table.Column<decimal>(type: "TEXT", nullable: false),
                    TipConfirm = table.Column<decimal>(type: "TEXT", nullable: false),
                    TipDocument = table.Column<decimal>(type: "TEXT", nullable: false),
                    ShiftLengthConfirm = table.Column<decimal>(type: "TEXT", nullable: false),
                    ShiftLengthDocument = table.Column<decimal>(type: "TEXT", nullable: false),
                    BatteryKwh = table.Column<decimal>(type: "TEXT", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SupersedesId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CorrectionReason = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Limits", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Limits",
                columns: new[] { "Id", "BatteryKwh", "CorrectionReason", "PayConfirm", "PayDocument", "RecordedAt", "ShiftLengthConfirm", "ShiftLengthDocument", "SpeedConfirm", "SpeedDocument", "SupersedesId", "TipConfirm", "TipDocument", "TripLengthConfirm", "TripLengthDocument" },
                values: new object[] { new Guid("5e771495-0000-4000-8000-000000000003"), 64.8m, null, 80m, 250m, new DateTimeOffset(new DateTime(2026, 10, 3, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, -5, 0, 0, 0)), 12m, 16m, 60m, 90m, null, 40m, 150m, 50m, 150m });

            migrationBuilder.CreateIndex(
                name: "IX_EntryMarks_RecordId",
                table: "EntryMarks",
                column: "RecordId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EntryMarks");

            migrationBuilder.DropTable(
                name: "Limits");
        }
    }
}
