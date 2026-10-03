using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GigLedger.Data.Migrations
{
    /// <summary>SRS FR-25 for the PromisedTips table (FR-6a), in its own migration like every table before it.</summary>
    public partial class AppendOnlyTriggersPromisedTips : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE TRIGGER PromisedTips_NoUpdate BEFORE UPDATE ON PromisedTips " +
                "BEGIN SELECT RAISE(ABORT, 'Ledger rows are never updated: PromisedTips'); END;");
            migrationBuilder.Sql(
                "CREATE TRIGGER PromisedTips_NoDelete BEFORE DELETE ON PromisedTips " +
                "BEGIN SELECT RAISE(ABORT, 'Ledger rows are never deleted: PromisedTips'); END;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER PromisedTips_NoUpdate;");
            migrationBuilder.Sql("DROP TRIGGER PromisedTips_NoDelete;");
        }
    }
}
