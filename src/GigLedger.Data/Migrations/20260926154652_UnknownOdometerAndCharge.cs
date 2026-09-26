using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GigLedger.Data.Migrations
{
    /// <summary>
    /// SRS 0.5: a charge session's odometer and state of charge may be unknown. SQLite changes a
    /// column's nullability by rebuilding the table, which drops its append-only triggers (FR-25);
    /// EF runs the rebuild after this migration's own SQL, so AppendOnlyTriggersUnknown restores them.
    /// </summary>
    public partial class UnknownOdometerAndCharge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "StartSoc",
                table: "ChargeSessions",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<string>(
                name: "OdometerGrade",
                table: "ChargeSessions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<decimal>(
                name: "Odometer",
                table: "ChargeSessions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<int>(
                name: "EndSoc",
                table: "ChargeSessions",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Going back would store every unknown as a zero, which the ledger never does.
            throw new NotSupportedException("Unknown odometer and state of charge have no non-null form.");
        }
    }
}
