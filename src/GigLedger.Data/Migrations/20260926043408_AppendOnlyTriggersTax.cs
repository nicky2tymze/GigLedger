using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GigLedger.Data.Migrations
{
    /// <summary>SRS FR-25 for the Slice 3b tax tables.</summary>
    public partial class AppendOnlyTriggersTax : Migration
    {
        private static readonly string[] LedgerTables = ["MileageRates", "PlatformForms"];

        /// <inheritdoc />
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

        /// <inheritdoc />
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
