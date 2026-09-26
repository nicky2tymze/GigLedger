using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GigLedger.Core;
using GigLedger.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GigLedger.Tests;

/// <summary>
/// Builds a workbook in the shape of Spark's earnings export: a Summary sheet with the total, and
/// a Transactions sheet with a title, blank rows, the header, the rows, and a disclaimer footer.
/// Every value here is synthetic.
/// </summary>
internal static class SparkWorkbook
{
    public sealed record Row(string TripId, string Time, string Type, string Earned, string Status = "Posted", string DepositedOn = "2025-12-31");

    public static readonly Row TipA = new("3134", "2025-12-31 03:49 PM", "Tip", "5.31");
    public static readonly Row TipB = new("3134", "2025-12-31 03:38 PM", "Tip", "6.34");
    public static readonly Row Trip = new("3134", "2025-12-31 11:02 AM", "Trip earnings", "18.50");
    public static readonly Row Bonus = new("", "2025-12-30 09:00 AM", "Incentive", "20", DepositedOn: "2025-12-30");

    public static decimal Sum(params Row[] rows) => rows.Sum(r => decimal.Parse(r.Earned));

    public static byte[] Build(Row[] rows, string? total = null, string zone = "CDT")
    {
        total ??= Sum(rows).ToString(System.Globalization.CultureInfo.InvariantCulture);
        List<string[]> summary =
        [
            ["", "Summary", "2025-01-01 - 2025-12-31"], [], ["", "Earnings"],
            ["", "Total earnings", "#" + total],
        ];
        List<string[]> transactions =
        [
            ["", "Transactions", "", "", "", "", "", "2025-01-01 - 2025-12-31"], [], [],
            ["", "Trip ID", $"Transaction date ({zone})", "Earning type", "Earned", "Deposit status", $"Deposited on ({zone})", "Deposited"],
        ];
        transactions.AddRange(rows.Select(r => new[] { "", r.TripId, r.Time, r.Type, "#" + r.Earned, r.Status, r.DepositedOn, "#" + r.Earned }));
        transactions.Add(["", "The amounts shown may be subject to updates."]);
        return Package(("Summary", summary), ("Transactions", transactions));
    }

    /// <summary>A cell starting with # is a number; anything else is an inline string; empty is no cell.</summary>
    public static byte[] Package(params (string Name, List<string[]> Rows)[] sheets)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create))
        {
            Write(zip, "[Content_Types].xml",
                "<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>");
            var workbook = new StringBuilder("<?xml version=\"1.0\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
            var rels = new StringBuilder("<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            for (var i = 0; i < sheets.Length; i++)
            {
                // Relationship ids deliberately not in sheet order, as in Spark's file.
                workbook.Append($"<sheet name=\"{sheets[i].Name}\" r:id=\"rId{i + 3}\" sheetId=\"{i + 1}\"/>");
                rels.Append($"<Relationship Id=\"rId{i + 3}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>");
                Write(zip, $"xl/worksheets/sheet{i + 1}.xml", Sheet(sheets[i].Rows));
            }
            Write(zip, "xl/workbook.xml", workbook.Append("</sheets></workbook>").ToString());
            Write(zip, "xl/_rels/workbook.xml.rels", rels.Append("</Relationships>").ToString());
        }
        return buffer.ToArray();
    }

    private static string Sheet(List<string[]> rows)
    {
        var xml = new StringBuilder("<?xml version=\"1.0\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        for (var r = 0; r < rows.Count; r++)
        {
            xml.Append($"<row r=\"{r + 1}\">");
            for (var c = 0; c < rows[r].Length; c++)
            {
                var value = rows[r][c];
                if (value.Length == 0) continue;
                var cell = $"{(char)('A' + c)}{r + 1}";
                xml.Append(value.StartsWith('#')
                    ? $"<c r=\"{cell}\" t=\"n\"><v>{value[1..]}</v></c>"
                    : $"<c r=\"{cell}\" t=\"inlineStr\"><is><t>{SecurityElement.Escape(value)}</t></is></c>");
            }
            xml.Append("</row>");
        }
        return xml.Append("</sheetData></worksheet>").ToString();
    }

    private static void Write(ZipArchive zip, string path, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}

/// <summary>Slice 3c payout import, written from SRS 0.5 FR-23 and SDD 6.8 before the code.</summary>
public sealed class PayoutImportTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 9, 0, 0, TimeSpan.FromHours(-5));

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly LedgerContext _db;
    private readonly LedgerServices _ledger;

    public PayoutImportTests()
    {
        _connection.Open();
        _db = LedgerDatabase.Open(_connection);
        _ledger = new LedgerServices(_db, new TestClock(Now));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private IPayoutService Payouts => _ledger;

    private static IReadOnlyList<Payout> Parse(byte[] xlsx) => PayoutImport.Parse(new MemoryStream(xlsx));

    private ImportResult Import(byte[] xlsx) => Payouts.Import(new MemoryStream(xlsx));

    // ---- Xlsx.ReadSheet ----

    [Fact]
    public void FR23_ASheetIsReadByName_WithCellsInTheirColumns()
    {
        var xlsx = SparkWorkbook.Package(
            ("First", [["x"]]),
            ("Second", [["a", "", "#5.31"], ["", "b & c"]]));
        var rows = Xlsx.ReadSheet(new MemoryStream(xlsx), "Second");
        Assert.Equal(["a", "", "5.31"], rows[0]);
        Assert.Equal("b & c", rows[1][1]);
        Assert.Equal("", rows[1][0]);
    }

    [Fact]
    public void FR23_SharedStringsAreRead()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create))
        {
            void Write(string path, string xml)
            {
                using var w = new StreamWriter(zip.CreateEntry(path).Open());
                w.Write(xml);
            }
            const string ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            Write("xl/workbook.xml", $"<workbook xmlns=\"{ns}\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"S\" r:id=\"rId1\" sheetId=\"1\"/></sheets></workbook>");
            Write("xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Target=\"worksheets/sheet1.xml\" Type=\"worksheet\"/></Relationships>");
            Write("xl/sharedStrings.xml", $"<sst xmlns=\"{ns}\"><si><t>zero</t></si><si><r><t>o</t></r><r><t>ne</t></r></si></sst>");
            Write("xl/worksheets/sheet1.xml", $"<worksheet xmlns=\"{ns}\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"s\"><v>1</v></c><c r=\"B1\" t=\"s\"><v>0</v></c></row></sheetData></worksheet>");
        }
        Assert.Equal(["one", "zero"], Xlsx.ReadSheet(new MemoryStream(buffer.ToArray()), "S")[0]);
    }

    [Fact]
    public void FR23_AMissingSheetIsRefused()
    {
        var e = Assert.Throws<ArgumentException>(() => Xlsx.ReadSheet(new MemoryStream(SparkWorkbook.Package(("A", []))), "Transactions"));
        Assert.Contains("Transactions", e.Message);
    }

    // ---- PayoutImport.Parse ----

    [Fact]
    public void FR23_ARowBecomesAPayout()
    {
        var p = Assert.Single(Parse(SparkWorkbook.Build([SparkWorkbook.TipA])));
        Assert.Equal("Spark", p.Platform);
        Assert.Equal("3134", p.TripId);
        Assert.Equal(new DateTimeOffset(2025, 12, 31, 15, 49, 0, TimeSpan.FromHours(-5)), p.At);
        Assert.Equal("CDT", p.Zone);
        Assert.Equal(PayoutType.Tip, p.Type);
        Assert.Equal(new Graded<decimal>(5.31m, Grade.Stated), p.Amount);
        Assert.Equal("Posted", p.DepositStatus);
        Assert.Equal(new DateOnly(2025, 12, 31), p.DepositedOn);
    }

    [Fact]
    public void FR23_EveryTypeIsRead_AndTheFooterIsNotARow()
    {
        var rows = Parse(SparkWorkbook.Build([SparkWorkbook.TipA, SparkWorkbook.Trip, SparkWorkbook.Bonus,
            new("3135", "2025-12-29 10:00 AM", "Adjustment credits", "3.00")]));
        Assert.Equal([PayoutType.Tip, PayoutType.TripEarnings, PayoutType.Incentive, PayoutType.AdjustmentCredit], rows.Select(r => r.Type));
    }

    [Fact]
    public void FR23_MorningAndMidnightReadOnATwentyFourHourClock()
    {
        var rows = Parse(SparkWorkbook.Build([
            SparkWorkbook.TipA with { Time = "2025-12-31 12:05 AM" },
            SparkWorkbook.TipB with { Time = "2025-12-31 12:05 PM" }]));
        Assert.Equal([0, 12], rows.Select(r => r.At.Hour));
    }

    [Fact]
    public void FR23_AnUnknownEarningTypeIsRefusedWithItsRow()
    {
        var e = Assert.Throws<ArgumentException>(() => Parse(SparkWorkbook.Build([SparkWorkbook.TipA, SparkWorkbook.TipB with { Type = "Referral" }])));
        Assert.Contains("row 6", e.Message);
        Assert.Contains("Referral", e.Message);
    }

    [Fact]
    public void FR23_AnUnreadableAmountIsRefused()
    {
        Assert.Throws<ArgumentException>(() => Parse(SparkWorkbook.Build([SparkWorkbook.TipA with { Earned = "abc" }], total: "5.31")));
    }

    [Fact]
    public void FR23_AZoneOtherThanCdtIsRefused()
    {
        var e = Assert.Throws<ArgumentException>(() => Parse(SparkWorkbook.Build([SparkWorkbook.TipA], zone: "EST")));
        Assert.Contains("EST", e.Message);
    }

    [Fact]
    public void FR23_AFileWhoseRowsDoNotSumToItsTotalIsRefused()
    {
        var e = Assert.Throws<ArgumentException>(() => Parse(SparkWorkbook.Build([SparkWorkbook.TipA, SparkWorkbook.TipB], total: "100.00")));
        Assert.Contains("100", e.Message);
    }

    [Fact]
    public void FR23_TheTotalIsComparedToTheCent_ThoughStoredAsAFloat()
    {
        // Spark's own summary reads 49999.79999999999 for $49,999.80.
        var rows = Parse(SparkWorkbook.Build([SparkWorkbook.TipA with { Earned = "5.3" }], total: "5.2999999999999998"));
        Assert.Equal(5.30m, Assert.Single(rows).Amount.Value);
    }

    // ---- Import: storing ----

    [Fact]
    public void FR23_ImportStoresEveryPayout_ForTheYear()
    {
        var result = Import(SparkWorkbook.Build([SparkWorkbook.TipA, SparkWorkbook.TipB, SparkWorkbook.Trip, SparkWorkbook.Bonus]));
        Assert.Equal(new ImportResult(4, 0), result);
        var year = Payouts.Year(2025);
        Assert.Equal(4, year.Count);
        Assert.Equal(SparkWorkbook.Sum(SparkWorkbook.TipA, SparkWorkbook.TipB, SparkWorkbook.Trip, SparkWorkbook.Bonus), year.Sum(p => p.Amount.Value));
        Assert.Empty(Payouts.Year(2026));
    }

    [Fact]
    public void FR23_ImportingTheSameFileTwiceStoresItOnce()
    {
        Import(SparkWorkbook.Build([SparkWorkbook.TipA, SparkWorkbook.Trip]));
        Assert.Equal(new ImportResult(1, 2), Import(SparkWorkbook.Build([SparkWorkbook.TipA, SparkWorkbook.Trip, SparkWorkbook.TipB])));
        Assert.Equal(3, Payouts.Year(2025).Count);
    }

    [Fact]
    public void FR23_TwoIdenticalRowsAreTwoPayouts_AndStayTwo()
    {
        var twice = SparkWorkbook.Build([SparkWorkbook.TipA, SparkWorkbook.TipA]);
        Assert.Equal(new ImportResult(2, 0), Import(twice));
        Assert.Equal(new ImportResult(0, 2), Import(twice));
        Assert.Equal(2, Payouts.Year(2025).Count);
    }

    [Fact]
    public void FR23_ARefusedFileStoresNothing()
    {
        Assert.Throws<ArgumentException>(() => Import(SparkWorkbook.Build([SparkWorkbook.TipA], total: "1.00")));
        Assert.Empty(Payouts.Year(2025));
    }

    [Fact]
    public void FR23_PayoutsComeBackAsImported()
    {
        Import(SparkWorkbook.Build([SparkWorkbook.Bonus]));
        Assert.Equal(Parse(SparkWorkbook.Build([SparkWorkbook.Bonus])), Payouts.Year(2025));
    }

    // ---- FR-31, FR-29: payouts as the record ----

    private ITaxService Taxes => _ledger;

    private static readonly decimal December = SparkWorkbook.Sum(SparkWorkbook.TipA, SparkWorkbook.Trip, SparkWorkbook.Bonus);

    private void LogSparkTrip(DateTimeOffset accepted, decimal pay)
    {
        var shift = ((IShiftService)_ledger).Start("Spark", accepted, new(17_000m, Grade.Measured));
        _ledger.Accept(shift, new Offer(new(pay, Grade.Stated), new(6.6m, Grade.Stated), 2, 32, new(58, Grade.Stated), accepted), accepted);
    }

    [Fact]
    public void FR31_ImportedPayoutsAreTheRecord_ByTransactionMonth()
    {
        Import(SparkWorkbook.Build([SparkWorkbook.TipA, SparkWorkbook.Trip, SparkWorkbook.Bonus]));
        var reported = Enumerable.Repeat(0m, 11).Append(December).ToList();
        Taxes.RecordForm(new PlatformForm(2025, "Spark", TaxForm.Form1099K, December, reported));

        var r = Taxes.Reconcile(2025, "Spark")!;
        Assert.Equal(PaymentSource.ImportedPayouts, r.RecordedFrom);
        Assert.Equal((December, 0m), (r.Recorded, r.Difference));
        Assert.Equal(December, r.Months![11].Recorded);
    }

    [Fact]
    public void FR31_WithoutPayouts_TheLoggedTripsAreTheRecord()
    {
        LogSparkTrip(Now, 34.89m);
        Taxes.RecordForm(new PlatformForm(2026, "Spark", TaxForm.Form1099Nec, 40.00m, null));
        var r = Taxes.Reconcile(2026, "Spark")!;
        Assert.Equal(PaymentSource.LoggedTrips, r.RecordedFrom);
        Assert.Equal(34.89m, r.Recorded);
    }

    [Fact]
    public void FR31_PayoutsAndLoggedTripsAreNeverAddedTogether()
    {
        // The same trip, logged by hand and present in the export, is income once.
        LogSparkTrip(new(2025, 12, 31, 11, 0, 0, TimeSpan.FromHours(-5)), 18.50m);
        Import(SparkWorkbook.Build([SparkWorkbook.TipA, SparkWorkbook.Trip, SparkWorkbook.Bonus]));
        Assert.Equal(December, Taxes.RecordedByMonth(2025, "Spark").Sum());
    }

    [Fact]
    public void FR29_TheSummaryGrossComesFromThePayouts_AndSaysSo()
    {
        Import(SparkWorkbook.Build([SparkWorkbook.TipA, SparkWorkbook.Trip, SparkWorkbook.Bonus]));
        var s = Taxes.Summary(2025);
        Assert.Equal(December, s.GrossByPlatform["Spark"]);
        Assert.Equal(PaymentSource.ImportedPayouts, s.GrossFrom!["Spark"]);
    }

    [Fact]
    public void FR25_PayoutsRefuseBulkChanges()
    {
        Import(SparkWorkbook.Build([SparkWorkbook.TipA]));
        // The trigger's own message, so a missing table cannot pass for a guarded one.
        Assert.Contains("never deleted", Assert.Throws<SqliteException>(() => _db.Database.ExecuteSqlRaw("DELETE FROM Payouts")).Message);
        Assert.Contains("never updated", Assert.Throws<SqliteException>(() => _db.Database.ExecuteSqlRaw("UPDATE Payouts SET Amount = 0")).Message);
    }
}

/// <summary>FR-23 through HTTP (FR-33).</summary>
public sealed class PayoutImportApiTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly WebApplicationFactory<Program> _app = new LedgerApp();
    private readonly HttpClient _http;

    public PayoutImportApiTests() => _http = _app.CreateClient();

    public void Dispose()
    {
        _http.Dispose();
        _app.Dispose();
    }

    private Task<HttpResponseMessage> Import(byte[] xlsx) =>
        _http.PostAsync("/api/payouts/import", new ByteArrayContent(xlsx));

    [Fact]
    public async Task FR33_ImportThenListTheYear()
    {
        var response = await Import(SparkWorkbook.Build([SparkWorkbook.TipA, SparkWorkbook.Trip]));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new ImportResult(2, 0), await response.Content.ReadFromJsonAsync<ImportResult>(Json));

        var year = await _http.GetFromJsonAsync<List<Payout>>("/api/payouts?year=2025", Json);
        Assert.Equal(2, year!.Count);
    }

    [Fact]
    public async Task FR33_ARefusedFileIs400WithTheReason()
    {
        var response = await Import(SparkWorkbook.Build([SparkWorkbook.TipA], total: "1.00"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Total earnings", await response.Content.ReadAsStringAsync());
    }
}
