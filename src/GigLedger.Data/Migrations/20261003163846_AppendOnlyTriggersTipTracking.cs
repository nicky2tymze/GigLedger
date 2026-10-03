using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GigLedger.Data.Migrations
{
    /// <summary>SRS FR-25 for the TipsIn table (FR-6a), in its own migration like every table before it.</summary>
    public partial class AppendOnlyTriggersTipTracking : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE TRIGGER TipsIn_NoUpdate BEFORE UPDATE ON TipsIn " +
                "BEGIN SELECT RAISE(ABORT, 'Ledger rows are never updated: TipsIn'); END;");
            migrationBuilder.Sql(
                "CREATE TRIGGER TipsIn_NoDelete BEFORE DELETE ON TipsIn " +
                "BEGIN SELECT RAISE(ABORT, 'Ledger rows are never deleted: TipsIn'); END;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER TipsIn_NoUpdate;");
            migrationBuilder.Sql("DROP TRIGGER TipsIn_NoDelete;");
        }
    }
}
