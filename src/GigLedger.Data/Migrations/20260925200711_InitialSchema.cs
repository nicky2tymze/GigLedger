using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GigLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AcceptThreshold = table.Column<decimal>(type: "TEXT", nullable: false),
                    DefaultMilesPerKwh = table.Column<decimal>(type: "TEXT", nullable: false),
                    DefaultPricePerKwh = table.Column<decimal>(type: "TEXT", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SupersedesId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShiftCloses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShiftId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    EndOdometer = table.Column<decimal>(type: "TEXT", nullable: false),
                    EndOdometerGrade = table.Column<string>(type: "TEXT", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SupersedesId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftCloses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Shifts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Platform = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    StartOdometer = table.Column<decimal>(type: "TEXT", nullable: false),
                    StartOdometerGrade = table.Column<string>(type: "TEXT", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SupersedesId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shifts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TripActuals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TripId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ElapsedMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    ElapsedMinutesGrade = table.Column<string>(type: "TEXT", nullable: false),
                    RouteMiles = table.Column<decimal>(type: "TEXT", nullable: false),
                    RouteMilesGrade = table.Column<string>(type: "TEXT", nullable: false),
                    ReturnMiles = table.Column<decimal>(type: "TEXT", nullable: false),
                    ReturnMilesGrade = table.Column<string>(type: "TEXT", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SupersedesId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripActuals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Trips",
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
                    AcceptedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SupersedesId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trips", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShiftCloses_ShiftId",
                table: "ShiftCloses",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_TripActuals_TripId",
                table: "TripActuals",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_ShiftId",
                table: "Trips",
                column: "ShiftId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "ShiftCloses");

            migrationBuilder.DropTable(
                name: "Shifts");

            migrationBuilder.DropTable(
                name: "TripActuals");

            migrationBuilder.DropTable(
                name: "Trips");
        }
    }
}
