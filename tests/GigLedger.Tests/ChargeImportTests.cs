using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GigLedger.Core;
using GigLedger.Data;
using GigLedger.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace GigLedger.Tests;

/// <summary>
/// Slice 3c charge import, written from SRS 0.5 FR-22 and SDD 6.7 before the code. The rows are
/// synthetic in the shape of real receipts; no real receipt belongs in this repository.
/// </summary>
public sealed class ChargeImportTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 9, 0, 0, TimeSpan.FromHours(-5));

    private const string Header =
        "gmail_message_id,receipt_number,start_local,finish_local,timezone,kwh,rate_per_kwh,net_total,station_id,location_name,address\r\n";

    private const string RowA =
        "m1,CP-202609000000001,2026-09-24T08:18:17,2026-09-24T08:43:07,CDT,24.2880,0.69,16.76,T100-TEST-0001,Test Plaza,\"1 Main St, , Anytown, Florida\"\r\n";
    private const string RowB =
        "m2,202607000000002,2026-07-13T10:57:28,2026-07-13T11:17:14,CDT,21.8850,0.49,10.72,T100-TEST-0002,Test Plaza,\"1 Main St, , Anytown, Florida\"\r\n";
    // A 30-second false start the network did not bill: energy delivered, total zero.
    private const string FalseStart =
        "m3,202606000000003,2026-06-27T08:49:32,2026-06-27T08:50:02,CDT,0.1860,0.49,0.00,T100-TEST-0001,Test Plaza,\"1 Main St, , Anytown, Florida\"\r\n";

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly LedgerContext _db;
    private readonly LedgerServices _ledger;

    public ChargeImportTests()
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

    private IChargeService Charges => _ledger;

    private IReadOnlyList<ChargeSession> Stored() =>
        Charges.Between(Now.AddYears(-1), Now).Select(c => c.Session).ToList();

    // ---- Csv.Read ----

    [Fact]
    public void FR22_CsvReadsQuotedFieldsAndDoubledQuotes()
    {
        var rows = Csv.Read("a,\"b, c\",\"say \"\"hi\"\"\"\r\nd,,e\r\n");
        Assert.Equal(["a", "b, c", "say \"hi\""], rows[0]);
        Assert.Equal(["d", "", "e"], rows[1]);
    }

    [Fact]
    public void FR22_CsvIgnoresATrailingLineEnd_AndReadsBareNewlines()
    {
        Assert.Equal(2, Csv.Read("a,b\nc,d\n").Count);
    }

    // ---- ChargeImport.Parse ----

    [Fact]
    public void FR22_AReceiptRowBecomesADcFastSessionWithTheUnknownsLeftUnknown()
    {
        var s = Assert.Single(ChargeImport.Parse(Header + RowA));
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 8, 18, 17, TimeSpan.FromHours(-5)), s.At);
        Assert.Equal(new Graded<decimal>(24.2880m, Grade.Measured), s.Kwh);
        Assert.Equal(new Graded<decimal>(16.76m, Grade.Measured), s.Cost);
        Assert.Equal(ChargeType.DcFast, s.Type);
        Assert.Equal("Blink T100-TEST-0001, Test Plaza", s.Charger);
        Assert.Equal("CP-202609000000001", s.ReceiptNumber);
        Assert.Null(s.Odometer);
        Assert.Null(s.StartSoc);
        Assert.Null(s.EndSoc);
        Assert.Null(s.Purpose);
    }

    [Fact]
    public void FR22_ColumnsAreFoundByName_NotPosition()
    {
        const string reordered =
            "location_name,net_total,kwh,timezone,start_local,station_id,receipt_number\r\n" +
            "Test Plaza,16.76,24.2880,CDT,2026-09-24T08:18:17,T100-TEST-0001,CP-202609000000001\r\n";
        Assert.Equal(ChargeImport.Parse(Header + RowA), ChargeImport.Parse(reordered));
    }

    [Fact]
    public void FR22_StandardTimeIsSixHoursBehind()
    {
        var s = Assert.Single(ChargeImport.Parse(Header + RowA.Replace(",CDT,", ",CST,")));
        Assert.Equal(TimeSpan.FromHours(-6), s.At.Offset);
    }

    [Fact]
    public void FR22_AnUnbilledFalseStartIsKeptAtZeroCost()
    {
        var s = Assert.Single(ChargeImport.Parse(Header + FalseStart));
        Assert.Equal(0.00m, s.Cost!.Value.Value);
        Assert.Equal(0.1860m, s.Kwh.Value);
    }

    [Fact]
    public void FR22_AnUnknownZoneIsRefusedWithItsLine()
    {
        var e = Assert.Throws<ArgumentException>(() => ChargeImport.Parse(Header + RowA + RowB.Replace(",CDT,", ",EDT,")));
        Assert.Contains("line 3", e.Message);
        Assert.Contains("EDT", e.Message);
    }

    [Fact]
    public void FR22_AMissingColumnIsRefused()
    {
        var e = Assert.Throws<ArgumentException>(() => ChargeImport.Parse("receipt_number,start_local\r\nx,2026-09-24T08:18:17\r\n"));
        Assert.Contains("kwh", e.Message);
    }

    [Theory]
    [InlineData("24.2880", "abc")]     // unreadable total
    [InlineData("0.0000", "0.00")]     // no energy, which FR-5 refuses
    [InlineData("24.2880", "-1.00")]   // negative cost, which FR-5 refuses
    public void FR22_ARowFR5WouldRefuseIsRefusedWithItsLine(string kwh, string total)
    {
        var bad = RowB.Replace(",21.8850,0.49,10.72,", $",{kwh},0.49,{total},");
        var e = Assert.Throws<ArgumentException>(() => ChargeImport.Parse(Header + RowA + bad));
        Assert.Contains("line 3", e.Message);
    }

    // ---- Import: storing ----

    [Fact]
    public void FR22_ImportStoresEveryRow()
    {
        var result = Charges.Import(Header + RowA + RowB + FalseStart);
        Assert.Equal(new ImportResult(3, 0), result);
        Assert.Equal(3, Stored().Count);
        Assert.Equal(24.2880m + 21.8850m + 0.1860m, Stored().Sum(s => s.Kwh.Value));
        Assert.Equal(16.76m + 10.72m, Charges.Between(Now.AddYears(-1), Now).Sum(c => c.Cost.Value));
    }

    [Fact]
    public void FR22_ImportingTheSameFileTwiceStoresItOnce()
    {
        Charges.Import(Header + RowA + RowB);
        var again = Charges.Import(Header + RowA + RowB + FalseStart);
        Assert.Equal(new ImportResult(1, 2), again);
        Assert.Equal(3, Stored().Count);
    }

    [Fact]
    public void FR22_OneBadRowStoresNothing()
    {
        Assert.Throws<ArgumentException>(() => Charges.Import(Header + RowA + RowB.Replace(",CDT,", ",EDT,")));
        Assert.Empty(Stored());
    }

    [Fact]
    public void FR22_TheReceiptNumberAndTheUnknownsComeBackFromStorage()
    {
        Charges.Import(Header + RowA);
        Assert.Equal(Assert.Single(ChargeImport.Parse(Header + RowA)), Assert.Single(Stored()));
    }

    [Fact]
    public void FR22_ImportedSessionsAloneCannotMeasureEfficiency()
    {
        // No odometer on any receipt: the report falls back rather than inventing miles.
        Charges.Import(Header + RowA + RowB + FalseStart);
        Assert.Null(Charges.Report(Now.AddYears(-1), Now).Efficiency);
    }
}

/// <summary>FR-22 through HTTP (FR-33).</summary>
public sealed class ChargeImportApiTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private const string File =
        "receipt_number,start_local,timezone,kwh,net_total,station_id,location_name\r\n" +
        "CP-202609000000001,2026-09-24T08:18:17,CDT,24.2880,16.76,T100-TEST-0001,Test Plaza\r\n";

    private readonly WebApplicationFactory<Program> _app = new LedgerApp();
    private readonly HttpClient _http;

    public ChargeImportApiTests() => _http = _app.CreateClient();

    public void Dispose()
    {
        _http.Dispose();
        _app.Dispose();
    }

    private Task<HttpResponseMessage> Import(string csv) =>
        _http.PostAsync("/api/charges/import", new StringContent(csv, Encoding.UTF8, "text/csv"));

    [Fact]
    public async Task FR33_ImportAnswersWithTheCounts()
    {
        var first = await Import(File);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(new ImportResult(1, 0), await first.Content.ReadFromJsonAsync<ImportResult>(Json));

        var second = await Import(File);
        Assert.Equal(new ImportResult(0, 1), await second.Content.ReadFromJsonAsync<ImportResult>(Json));
    }

    [Fact]
    public async Task FR33_ARefusedFileIs400WithTheReason()
    {
        var response = await Import(File.Replace(",CDT,", ",EDT,"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("line 2", await response.Content.ReadAsStringAsync());
    }
}
