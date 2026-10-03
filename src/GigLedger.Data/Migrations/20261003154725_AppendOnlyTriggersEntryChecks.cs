using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GigLedger.Data.Migrations
{
    /// <summary>SRS FR-25 for the entry-check tables (FR-35, FR-36), in their own migration like every table before them.</summary>
    public partial class AppendOnlyTriggersEntryChecks : Migration
    {
        private static readonly string[] LedgerTables = ["EntryMarks", "Limits"];

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
