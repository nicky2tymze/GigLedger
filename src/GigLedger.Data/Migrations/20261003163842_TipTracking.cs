using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GigLedger.Data.Migrations
{
    /// <inheritdoc />
    public partial class TipTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PromisedTip",
                table: "Trips",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PromisedTipGrade",
                table: "Trips",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TrackTips",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "PromisedTip",
                table: "Declines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PromisedTipGrade",
                table: "Declines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TipsIn",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TripId = table.Column<Guid>(type: "TEXT", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SupersedesId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CorrectionReason = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TipsIn", x => x.Id);
                });

            // EF generated UpdateData here to set TrackTips = false on the seeded settings row. The new
            // column's default is already false, and the append-only triggers (correctly) refuse any
            // UPDATE, which would abort the migration on a real database. Removed by hand, as in RecordKeeping.

            migrationBuilder.CreateIndex(
                name: "IX_TipsIn_TripId",
                table: "TipsIn",
                column: "TripId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TipsIn");

            migrationBuilder.DropColumn(
                name: "PromisedTip",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "PromisedTipGrade",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "TrackTips",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "PromisedTip",
                table: "Declines");

            migrationBuilder.DropColumn(
                name: "PromisedTipGrade",
                table: "Declines");
        }
    }
}
