using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GigLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class Payouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Payouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Platform = table.Column<string>(type: "TEXT", nullable: false),
                    TripId = table.Column<string>(type: "TEXT", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Zone = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    AmountGrade = table.Column<string>(type: "TEXT", nullable: false),
                    DepositStatus = table.Column<string>(type: "TEXT", nullable: false),
                    DepositedOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    RecordedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SupersedesId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CorrectionReason = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payouts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Payouts_Platform_TripId",
                table: "Payouts",
                columns: new[] { "Platform", "TripId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Payouts");
        }
    }
}
