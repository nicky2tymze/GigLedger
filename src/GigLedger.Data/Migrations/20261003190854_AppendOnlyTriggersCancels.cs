using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GigLedger.Data.Migrations
{
    /// <summary>SRS FR-25 for the Cancels table (FR-3a), in its own migration like every table before it.</summary>
    public partial class AppendOnlyTriggersCancels : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE TRIGGER Cancels_NoUpdate BEFORE UPDATE ON Cancels " +
                "BEGIN SELECT RAISE(ABORT, 'Ledger rows are never updated: Cancels'); END;");
            migrationBuilder.Sql(
                "CREATE TRIGGER Cancels_NoDelete BEFORE DELETE ON Cancels " +
                "BEGIN SELECT RAISE(ABORT, 'Ledger rows are never deleted: Cancels'); END;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER Cancels_NoUpdate;");
            migrationBuilder.Sql("DROP TRIGGER Cancels_NoDelete;");
        }
    }
}
