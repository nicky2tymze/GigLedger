using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GigLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class Declines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Declines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShiftId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Pay = table.Column<decimal>(type: "TEXT", nullable: false),
                    PayGrade = table.Column<string>(type: "TEXT", nullable: false),
                    StatedMiles = table.Column<decimal>(type: "TEXT", nullable: false),
                    StatedMilesGrade = table.Column<string>(type: "TEXT", nullable: false),
                    Drops = table.Column<int>(type: "INTEGER", nullable: false),
                    Items = table.Column<int>(type: "INTEGER", nullable: false),
                    EstimatedMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    EstimatedMinutesGrade = table.Column<string>(type: "TEXT", nullable: false),
                    OfferedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ReturnMilesOverride = table.Column<decimal>(type: "TEXT", nullable: true),
                    DeclinedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ForecastNetPerHour = table.Column<decimal>(type: "TEXT", nullable: false),
                    ForecastAssumptions = table.Column<string>(type: "TEXT", nullable: false),
                    Verdict = table.Column<string>(type: "TEXT", nullable: false),
                    Threshold = table.Column<decimal>(type: "TEXT", nullable: false),
                    Reasons = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    RecordedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SupersedesId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CorrectionReason = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Declines", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Declines_ShiftId",
                table: "Declines",
                column: "ShiftId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Declines");
        }
    }
}
