using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GigLedger.Data.Migrations
{
    /// <summary>
    /// SRS FR-25, SDD 5.3: ledger rows are never updated or deleted. LedgerContext refuses
    /// both through EF, but ExecuteUpdate, ExecuteDelete, and raw SQL never pass through
    /// SaveChanges, so the database itself refuses them here.
    ///
    /// A later migration that must change seeded data drops the trigger, makes the change,
    /// and recreates it, so the exception is visible in the migration history.
    /// </summary>
    public partial class AppendOnlyTriggers : Migration
    {
        private static readonly string[] LedgerTables = ["Shifts", "ShiftCloses", "Trips", "TripActuals", "Settings"];

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in LedgerTables)
            {
                migrationBuilder.Sql(
                    $"CREATE TRIGGER {table}_NoUpdate BEFORE UPDATE ON {table} " +
                    $"BEGIN SELECT RAISE(ABORT, 'Ledger rows are never updated: {table}'); END;");
                migrationBuilder.Sql(
                    $"CREATE TRIGGER {table}_NoDelete BEFORE DELETE ON {table} " +
                    $"BEGIN SELECT RAISE(ABORT, 'Ledger rows are never deleted: {table}'); END;");
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in LedgerTables)
            {
                migrationBuilder.Sql($"DROP TRIGGER {table}_NoUpdate;");
                migrationBuilder.Sql($"DROP TRIGGER {table}_NoDelete;");
            }
        }
    }
}
