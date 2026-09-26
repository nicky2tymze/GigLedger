using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GigLedger.Core;
using GigLedger.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace GigLedger.Tests;

/// <summary>Slice 3a through HTTP, written before the endpoints (FR-33 over FR-25 to FR-28, NFR-7).</summary>
public sealed class RecordApiTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly DateOnly Today = new(2026, 9, 25);

    private readonly string _backups = Directory.CreateTempSubdirectory("gigledger-api-backup-").FullName;
    private readonly WebApplicationFactory<Program> _app;
    private readonly HttpClient _http;

    public RecordApiTests()
    {
        _app = new LedgerApp().WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<BackupFolder>();
            s.AddSingleton(new BackupFolder(_backups));
        }));
        _http = _app.CreateClient();
    }

    public void Dispose()
    {
        _http.Dispose();
        _app.Dispose();
        Directory.Delete(_backups, recursive: true);
    }

    private static Drive DriveIn(decimal end = 17_958m) =>
        new(Today, new(17_948m, Grade.Measured), new(end, Grade.Measured), Purpose.Work, "Home to Walmart 1102");

    private static Expense Binders() => new(Today, ExpenseCategory.Supplies, new(18.47m, Grade.Measured), "Binders");

    private async Task<Guid> Post<T>(string url, T body)
    {
        var response = await _http.PostAsJsonAsync(url, body, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Created>(Json))!.Id;
    }

    [Fact]
    public async Task FR26_ADriveAndTheYearsTotals()
    {
        await Post("/api/drives", DriveIn());
        var drives = await _http.GetFromJsonAsync<List<StoredDrive>>("/api/drives?year=2026", Json);
        Assert.Single(drives!);
        var totals = await _http.GetFromJsonAsync<MileageTotals>("/api/mileage/2026", Json);
        Assert.Equal(new MileageTotals(10m, 0m), totals);
    }

    [Fact]
    public async Task FR25_ACorrectionThroughTheApiKeepsTheHistory()
    {
        var original = await Post("/api/drives", DriveIn(end: 17_985m));
        var corrected = await Post($"/api/drives/{original}/correct", new CorrectDriveRequest(DriveIn(), "typed 17,958 as 17,985"));
        var history = await _http.GetFromJsonAsync<List<Version<Drive>>>($"/api/drives/{corrected}/history", Json);
        Assert.Equal([original, corrected], history!.Select(v => v.Id));
        Assert.Equal("typed 17,958 as 17,985", history[1].Reason);
    }

    [Fact]
    public async Task FR25_ACorrectionWithoutAReasonIs400()
    {
        var original = await Post("/api/drives", DriveIn());
        var response = await _http.PostAsJsonAsync($"/api/drives/{original}/correct", new CorrectDriveRequest(DriveIn(17_959m), ""), Json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FR28_AnExpenseAndItsReceipt()
    {
        var expense = await Post("/api/expenses", Binders());
        var content = "%PDF receipt"u8.ToArray();
        var attachment = await Post("/api/attachments",
            new AttachRequest(AttachedTo.Expense, expense, "walmart.pdf", "application/pdf", Convert.ToBase64String(content)));

        var file = await _http.GetAsync($"/api/attachments/{attachment}");
        Assert.Equal("application/pdf", file.Content.Headers.ContentType!.MediaType);
        Assert.Equal(content, await file.Content.ReadAsByteArrayAsync());
        var verification = await _http.GetFromJsonAsync<Verification>($"/api/attachments/{attachment}/verify", Json);
        Assert.True(verification!.Intact);
    }

    [Fact]
    public async Task FR27_AReceiptThatIsNotBase64Is400()
    {
        var expense = await Post("/api/expenses", Binders());
        var response = await _http.PostAsJsonAsync("/api/attachments",
            new AttachRequest(AttachedTo.Expense, expense, "x.pdf", "application/pdf", "not base64!"), Json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NFR7_ABackupGoesToTheConfiguredFolderOnly()
    {
        var response = await _http.PostAsync("/api/backup", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<BackupResult>(Json))!;
        Assert.Equal(Path.GetFullPath(_backups), Path.GetDirectoryName(Path.GetFullPath(result.Path)));
        Assert.True(File.Exists(result.Path));
    }
}
