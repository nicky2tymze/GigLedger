using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GigLedger.Data.Migrations
{
    /// <summary>
    /// SRS 0.5 FR-22: a charge session carries the receipt it was imported from, and its purpose
    /// may be unknown. Making Purpose nullable rebuilds the table, which drops its append-only
    /// triggers (FR-25); AppendOnlyTriggersChargeImport restores them.
    /// </summary>
    public partial class ChargeImport : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Purpose",
                table: "ChargeSessions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AddColumn<string>(
                name: "ReceiptNumber",
                table: "ChargeSessions",
                type: "TEXT",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Going back would have to invent a purpose for every session whose purpose is unknown.
            throw new NotSupportedException("An unknown purpose has no non-null form.");
        }
    }
}
