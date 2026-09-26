using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GigLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class ChargesTipsHomeRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChargeSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Odometer = table.Column<decimal>(type: "TEXT", nullable: false),
                    OdometerGrade = table.Column<string>(type: "TEXT", nullable: false),
                    Kwh = table.Column<decimal>(type: "TEXT", nullable: false),
                    KwhGrade = table.Column<string>(type: "TEXT", nullable: false),
                    Cost = table.Column<decimal>(type: "TEXT", nullable: true),
                    CostGrade = table.Column<string>(type: "TEXT", nullable: true),
                    StartSoc = table.Column<int>(type: "INTEGER", nullable: false),
                    EndSoc = table.Column<int>(type: "INTEGER", nullable: false),
                    Charger = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SupersedesId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChargeSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HomeRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PerKwh = table.Column<decimal>(type: "TEXT", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    IsPlaceholder = table.Column<bool>(type: "INTEGER", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SupersedesId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HomeRates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tips",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TripId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    AmountGrade = table.Column<string>(type: "TEXT", nullable: false),
                    PostedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SupersedesId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tips", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "HomeRates",
                columns: new[] { "Id", "EffectiveFrom", "IsPlaceholder", "PerKwh", "RecordedAt", "SupersedesId" },
                values: new object[] { new Guid("5e771495-0000-4000-8000-000000000002"), new DateOnly(2026, 1, 1), true, 0.15m, new DateTimeOffset(new DateTime(2026, 9, 25, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, -5, 0, 0, 0)), null });

            migrationBuilder.CreateIndex(
                name: "IX_Tips_TripId",
                table: "Tips",
                column: "TripId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChargeSessions");

            migrationBuilder.DropTable(
                name: "HomeRates");

            migrationBuilder.DropTable(
                name: "Tips");
        }
    }
}
