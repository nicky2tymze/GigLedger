using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GigLedger.Data.Migrations
{
    /// <summary>
    /// SRS FR-25 for ChargeSessions again. UnknownOdometerAndCharge rebuilt the table to make
    /// columns nullable, and SQLite drops a table's triggers with the table.
    /// </summary>
    public partial class AppendOnlyTriggersUnknown : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE TRIGGER ChargeSessions_NoUpdate BEFORE UPDATE ON ChargeSessions " +
                "BEGIN SELECT RAISE(ABORT, 'Ledger rows are never updated: ChargeSessions'); END;");
            migrationBuilder.Sql(
                "CREATE TRIGGER ChargeSessions_NoDelete BEFORE DELETE ON ChargeSessions " +
                "BEGIN SELECT RAISE(ABORT, 'Ledger rows are never deleted: ChargeSessions'); END;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER ChargeSessions_NoUpdate;");
            migrationBuilder.Sql("DROP TRIGGER ChargeSessions_NoDelete;");
        }
    }
}
