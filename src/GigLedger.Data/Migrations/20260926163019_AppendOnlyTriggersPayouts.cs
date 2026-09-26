using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GigLedger.Data.Migrations
{
    /// <summary>SRS FR-25 for the Payouts table (FR-23), in its own migration like every table before it.</summary>
    public partial class AppendOnlyTriggersPayouts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE TRIGGER Payouts_NoUpdate BEFORE UPDATE ON Payouts " +
                "BEGIN SELECT RAISE(ABORT, 'Ledger rows are never updated: Payouts'); END;");
            migrationBuilder.Sql(
                "CREATE TRIGGER Payouts_NoDelete BEFORE DELETE ON Payouts " +
                "BEGIN SELECT RAISE(ABORT, 'Ledger rows are never deleted: Payouts'); END;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER Payouts_NoUpdate;");
            migrationBuilder.Sql("DROP TRIGGER Payouts_NoDelete;");
        }
    }
}
