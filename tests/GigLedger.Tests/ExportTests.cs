using System.IO.Compression;
using System.Net;
using Bunit;
using GigLedger.Core;
using GigLedger.Data;
using GigLedger.Web;
using GigLedger.Web.Components.Pages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GigLedger.Tests;

/// <summary>Slice 3b pass 3: the full export (FR-32), written before the code.</summary>
public sealed class ExportTests : TestContext
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 9, 0, 0, TimeSpan.FromHours(-5));

    private static readonly string[] Tables =
    [
        "Shifts", "ShiftCloses", "Trips", "TripActuals", "Settings", "ChargeSessions", "Tips", "HomeRates",
        "Drives", "Expenses", "Attachments", "MileageRates", "PlatformForms",
    ];

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly LedgerContext _db;
    private readonly LedgerServices _ledger;
    private readonly string _backups = Directory.CreateTempSubdirectory("gigledger-export-").FullName;

    public ExportTests()
    {
        _connection.Open();
        _db = LedgerDatabase.Open(_connection);
        _ledger = new LedgerServices(_db, new TestClock(Now));
        Services.AddSingleton<IBackupService>(_ledger);
        Services.AddSingleton(new BackupFolder(_backups));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        _connection.Dispose();
        Directory.Delete(_backups, recursive: true);
    }

    private ZipArchive Exported()
    {
        var buffer = new MemoryStream();
        ((IExportService)_ledger).Export(buffer);
        buffer.Position = 0;
        return new ZipArchive(buffer, ZipArchiveMode.Read);
    }

    private static string[] Lines(ZipArchive zip, string entry)
    {
        using var reader = new StreamReader(zip.GetEntry(entry)!.Open());
        return reader.ReadToEnd().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
    }

    [Fact]
    public void FR32_EveryTableInTheModelIsExported()
    {
        // From the model, not a list: a table added later cannot be left out of the export unnoticed.
        using var zip = Exported();
        foreach (var table in _db.Model.GetEntityTypes().Select(e => e.GetTableName()!))
            Assert.True(zip.GetEntry($"{table}.csv") is not null, $"{table} is not in the export");
    }

    private static readonly byte[] ReceiptPdf = "%PDF-1.7 Walmart receipt $18.47"u8.ToArray();

    [Fact]
    public void FR32_EveryTableEveryVersionAndEveryReceipt()
    {
        var shift = ((IShiftService)_ledger).Start("Spark", Now, new(17_000m, Grade.Measured));
        var trip = _ledger.Accept(shift, new Offer(new(34.89m, Grade.Stated), new(6.6m, Grade.Stated), 2, 32, new(58, Grade.Stated), Now), Now);
        ((ITripService)_ledger).RecordActuals(trip, new(new(15, Grade.Entered), new(6.4m, Grade.Entered), new(5.7m, Grade.Entered)));
        ((ICorrectionService)_ledger).CorrectActuals(trip, new(new(55, Grade.Entered), new(6.4m, Grade.Entered), new(5.7m, Grade.Entered)), "55 minutes, not 15");
        var expense = ((IExpenseService)_ledger).Record(new Expense(new(2026, 9, 25), ExpenseCategory.Supplies, new(18.47m, Grade.Measured), "Binders"));
        var receipt = ((IAttachmentService)_ledger).Attach(AttachedTo.Expense, expense, "walmart.pdf", "application/pdf", ReceiptPdf);

        using var zip = Exported();
        foreach (var table in Tables)
            Assert.NotNull(zip.GetEntry($"{table}.csv"));

        var actuals = Lines(zip, "TripActuals.csv");
        Assert.Equal(3, actuals.Length);                         // header, the original, the correction
        Assert.Contains(actuals, l => l.EndsWith(",\"55 minutes, not 15\"")); // the reason has a comma: it must be quoted

        var trips = Lines(zip, "Trips.csv");
        Assert.Contains(trips, l => l.Contains(",34.89,"));        // invariant decimal
        Assert.Contains(trips, l => l.Contains("2026-09-25T09:00:00.0000000-05:00")); // round-trip time, offset kept

        using var stored = zip.GetEntry($"receipts/{receipt}-walmart.pdf")!.Open();
        using var copy = new MemoryStream();
        stored.CopyTo(copy);
        Assert.Equal(ReceiptPdf, copy.ToArray());
        Assert.DoesNotContain(Lines(zip, "Attachments.csv")[0].Split(','), c => c == "Content"); // bytes are files, not a column

        var manifest = Lines(zip, "manifest.txt");
        Assert.Contains("TripActuals: 2", manifest);
        Assert.Contains("receipts: 1", manifest);
    }

    [Fact]
    public void FR32_AnEmptyLedgerStillExportsEveryTableWithItsHeader()
    {
        using var zip = Exported();
        foreach (var table in Tables)
            Assert.StartsWith("Id,", Lines(zip, $"{table}.csv")[0]);
        Assert.Single(Lines(zip, "Trips.csv"));    // header only
        Assert.Equal(2, Lines(zip, "Settings.csv").Length); // header and the seeded row
    }

    [Fact]
    public void UI_Backup_OffersTheFullExport()
    {
        var page = RenderComponent<Backup>();
        Assert.Equal("/api/export", page.Find("#export-all").GetAttribute("href"));
    }
}

public sealed class ExportApiTests : IDisposable
{
    private readonly LedgerApp _app = new();
    private readonly HttpClient _http;

    public ExportApiTests() => _http = _app.CreateClient();

    public void Dispose()
    {
        _http.Dispose();
        _app.Dispose();
    }

    [Fact]
    public async Task FR33_TheExportIsAZipDownload()
    {
        var response = await _http.GetAsync("/api/export");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType!.MediaType);
        using var zip = new ZipArchive(await response.Content.ReadAsStreamAsync(), ZipArchiveMode.Read);
        Assert.NotNull(zip.GetEntry("manifest.txt"));
    }
}
