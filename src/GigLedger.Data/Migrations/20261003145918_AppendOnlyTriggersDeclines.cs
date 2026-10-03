using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GigLedger.Data.Migrations
{
    /// <summary>SRS FR-25 for the Declines table (FR-2a), in its own migration like every table before it.</summary>
    public partial class AppendOnlyTriggersDeclines : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE TRIGGER Declines_NoUpdate BEFORE UPDATE ON Declines " +
                "BEGIN SELECT RAISE(ABORT, 'Ledger rows are never updated: Declines'); END;");
            migrationBuilder.Sql(
                "CREATE TRIGGER Declines_NoDelete BEFORE DELETE ON Declines " +
                "BEGIN SELECT RAISE(ABORT, 'Ledger rows are never deleted: Declines'); END;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER Declines_NoUpdate;");
            migrationBuilder.Sql("DROP TRIGGER Declines_NoDelete;");
        }
    }
}
