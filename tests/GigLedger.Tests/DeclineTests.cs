using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GigLedger.Core;
using GigLedger.Data;
using GigLedger.Web;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GigLedger.Tests;

/// <summary>
/// Slice 4, story 1: declines with reasons (SRS 0.6 FR-2a, FR-21a; SDD 0.7 section 6.9), written
/// before the code. Each test gets its own in-memory SQLite database, migrated to the current schema.
/// Forecasts are compared to Core's own evaluation, never to constants.
/// </summary>
public sealed class DeclineTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 2, 6, 57, 0, TimeSpan.FromHours(-5));

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly LedgerContext _db;
    private readonly LedgerServices _ledger;

    public DeclineTests()
    {
        _connection.Open();
        _db = LedgerDatabase.Open(_connection);
        _ledger = new LedgerServices(_db, TimeProvider.System);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private IShiftService Shifts => _ledger;
    private IOfferService Offers => _ledger;
    private ITripService Trips => _ledger;
    private ISettingsService Settings => _ledger;

    /// <summary>A run that clears $25/hr on the default energy figures.</summary>
    private static Offer Clearing() => new(
        new(34.89m, Grade.Stated), new(6.6m, Grade.Stated), 2, 32, new(58, Grade.Stated), T0.AddMinutes(1));

    /// <summary>The same run at a pay that does not clear.</summary>
    private static Offer Short() => Clearing() with { Pay = new(9.00m, Grade.Stated) };

    private Guid OpenShift() => Shifts.Start("Spark", T0, new(17_000m, Grade.Measured));

    private Guid Decline(Guid shift, Offer offer, DateTimeOffset at, string? note = null, params DeclineReason[] reasons) =>
        Offers.Decline(shift, offer, reasons, note, at);

    /// <summary>A fresh context on the same database, so reads come from the file, not the cache.</summary>
    private LedgerContext Reopen() =>
        new(new DbContextOptionsBuilder<LedgerContext>().UseSqlite(_connection).Options);

    // ---- The reason list (FR-2a) ----

    [Fact]
    public void FR2a_ReasonsAreTheArchitectsListInHisOrder()
    {
        // SRS 0.6 FR-2a, in the order of consideration. A reorder in code fails here first.
        Assert.Equal(
            [
                DeclineReason.PayTooLow, DeclineReason.TooFar, DeclineReason.TooManyItems, DeclineReason.Pharmacy,
                DeclineReason.Alcohol, DeclineReason.Heavy, DeclineReason.Stairs, DeclineReason.Apartment,
                DeclineReason.TooManyDrops, DeclineReason.LowCharge, DeclineReason.EndingShift, DeclineReason.Other,
            ],
            Enum.GetValues<DeclineReason>());
    }

    [Fact]
    public void FR2a_EveryReasonHasTheLabelTheSrsGivesIt()
    {
        var labels = Enum.GetValues<DeclineReason>().Select(DeclineRules.Label).ToList();
        Assert.Equal(
            [
                "Pay too low", "Too far (includes bad geometry)", "Too many items", "Pharmacy", "Alcohol", "Heavy",
                "Stairs", "Apartment", "Too many drops (batched orders)", "Low charge", "Ending shift", "Other",
            ],
            labels);
    }

    // ---- Validation (FR-2a; SDD 6.9) ----

    [Fact]
    public void FR2a_ADeclineNeedsAReason()
    {
        var e = Assert.Throws<ArgumentException>(() => DeclineRules.Validate([], null));
        Assert.Contains("reason", e.Message);
    }

    [Fact]
    public void FR2a_AReasonGivenTwiceIsRefused()
    {
        var e = Assert.Throws<ArgumentException>(() => DeclineRules.Validate([DeclineReason.Heavy, DeclineReason.Heavy], null));
        Assert.Contains("twice", e.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FR2a_OtherNeedsANote(string? note)
    {
        var e = Assert.Throws<ArgumentException>(() => DeclineRules.Validate([DeclineReason.Other], note));
        Assert.Contains("note", e.Message);
    }

    [Fact]
    public void FR2a_ReasonsComeBackInDisplayOrderAndTheNoteTrimmed()
    {
        var (reasons, note) = DeclineRules.Validate([DeclineReason.Stairs, DeclineReason.PayTooLow], "  third floor  ");
        Assert.Equal([DeclineReason.PayTooLow, DeclineReason.Stairs], reasons);
        Assert.Equal("third floor", note);
    }

    [Fact]
    public void FR2a_ABlankNoteIsStoredAsNone()
    {
        var (_, note) = DeclineRules.Validate([DeclineReason.Heavy], "   ");
        Assert.Null(note);
    }

    // ---- Storing (FR-2a) ----

    [Fact]
    public void FR2a_ADeclineIsStoredWithTheOfferTheForecastTheReasonsTheNoteAndTheTime()
    {
        var shift = OpenShift();
        var expected = Offers.Evaluate(Clearing());

        var id = Decline(shift, Clearing(), T0.AddMinutes(2), "dog at the door", DeclineReason.Other, DeclineReason.Heavy);

        var stored = Assert.Single(Offers.DeclinesOnShift(shift));
        Assert.Equal(id, stored.Id);
        Assert.Equal(shift, stored.ShiftId);
        Assert.Equal(Clearing(), stored.Offer);
        Assert.Equal(T0.AddMinutes(2), stored.DeclinedAt);
        Assert.Equal(expected.Forecast.Value, stored.Forecast.Value);
        Assert.Equal(expected.Verdict, stored.Verdict);
        Assert.Equal(expected.Threshold, stored.Threshold);
        Assert.Equal([DeclineReason.Heavy, DeclineReason.Other], stored.Reasons);
        Assert.Equal("dog at the door", stored.Note);
    }

    [Fact]
    public void FR2a_TheStoredForecastKeepsItsAssumptions()
    {
        // NFR-2: with no charging recorded the forecast rests on the defaults, and says so after storing.
        var shift = OpenShift();
        var expected = Offers.Evaluate(Clearing());
        Assert.True(expected.Forecast.RestsOnDefault);

        Decline(shift, Clearing(), T0.AddMinutes(2), null, DeclineReason.PayTooLow);

        Assert.Equal(expected.Forecast.Assumptions, Assert.Single(Offers.DeclinesOnShift(shift)).Forecast.Assumptions);
    }

    [Fact]
    public void FR2a_TheVerdictIsTheOneAtTheMomentOfDeclining()
    {
        var shift = OpenShift();
        Decline(shift, Clearing(), T0.AddMinutes(2), null, DeclineReason.TooFar);

        Settings.Set(Settings.Get() with { AcceptThreshold = 100m });

        var stored = Assert.Single(Offers.DeclinesOnShift(shift));
        Assert.Equal(Verdict.Clears, stored.Verdict);
        Assert.Equal(25.00m, stored.Threshold);
    }

    [Fact]
    public void FR2a_ADeclineReadsBackFromTheFile()
    {
        var shift = OpenShift();
        Decline(shift, Short(), T0.AddMinutes(2), "  stairs and no lift ", DeclineReason.Stairs, DeclineReason.PayTooLow);

        using var fresh = Reopen();
        var stored = Assert.Single(new LedgerServices(fresh, TimeProvider.System).DeclinesOnShift(shift));
        Assert.Equal(Short(), stored.Offer);
        Assert.Equal([DeclineReason.PayTooLow, DeclineReason.Stairs], stored.Reasons);
        Assert.Equal("stairs and no lift", stored.Note);
        Assert.Equal(Verdict.DoesNotClear, stored.Verdict);
    }

    [Fact]
    public void FR2a_DeclinesComeBackInTheOrderDeclined()
    {
        var shift = OpenShift();
        Decline(shift, Short(), T0.AddMinutes(9), null, DeclineReason.Heavy);
        Decline(shift, Clearing(), T0.AddMinutes(3), null, DeclineReason.Alcohol);

        Assert.Equal([T0.AddMinutes(3), T0.AddMinutes(9)], Offers.DeclinesOnShift(shift).Select(d => d.DeclinedAt));
    }

    [Fact]
    public void FR2a_ADeclineIsNotATrip()
    {
        var shift = OpenShift();
        Decline(shift, Clearing(), T0.AddMinutes(2), null, DeclineReason.Pharmacy);
        Assert.Empty(Trips.OnShift(shift));
    }

    [Fact]
    public void FR2a_ADeclineNeedsAShiftThatExists()
    {
        Assert.Throws<NotFoundException>(() => Decline(Guid.NewGuid(), Clearing(), T0, null, DeclineReason.Heavy));
    }

    [Fact]
    public void FR2a_ADeclineNeedsTheShiftOpen()
    {
        var shift = OpenShift();
        Shifts.End(shift, T0.AddHours(1), new(17_010m, Grade.Measured));

        var e = Assert.Throws<InvalidOperationException>(() => Decline(shift, Clearing(), T0.AddHours(2), null, DeclineReason.Heavy));
        Assert.Contains("closed", e.Message);
    }

    [Fact]
    public void FR2a_AnInvalidDeclineStoresNothing()
    {
        var shift = OpenShift();
        Assert.Throws<ArgumentException>(() => Decline(shift, Clearing(), T0.AddMinutes(2), null, DeclineReason.Other));
        Assert.Empty(Offers.DeclinesOnShift(shift));
    }

    // ---- Nothing is deleted (FR-25) ----

    [Fact]
    public void FR25_ADeclineIsNeverUpdatedOrDeleted()
    {
        var shift = OpenShift();
        Decline(shift, Clearing(), T0.AddMinutes(2), null, DeclineReason.Heavy);
        // The trigger's own message, so a missing table cannot pass for a guarded one.
        Assert.Contains("never deleted", Assert.Throws<SqliteException>(() => _db.Database.ExecuteSqlRaw("DELETE FROM Declines")).Message);
        Assert.Contains("never updated", Assert.Throws<SqliteException>(() => _db.Database.ExecuteSqlRaw("UPDATE Declines SET Note = 'x'")).Message);
    }

    // ---- The report (FR-21a) ----

    [Fact]
    public void FR21a_EveryReasonIsCountedInDisplayOrderZerosIncluded()
    {
        var shift = OpenShift();
        Decline(shift, Short(), T0.AddMinutes(2), null, DeclineReason.Heavy);

        var report = Offers.ReportDeclines(new DateOnly(2026, 8, 2), new DateOnly(2026, 8, 2));

        Assert.Equal(Enum.GetValues<DeclineReason>(), report.ByReason.Select(c => c.Reason));
        Assert.Equal(1, report.ByReason.Single(c => c.Reason == DeclineReason.Heavy).Count);
        Assert.All(report.ByReason.Where(c => c.Reason != DeclineReason.Heavy), c => Assert.Equal(0, c.Count));
    }

    [Fact]
    public void FR21a_ADeclineWithTwoReasonsCountsUnderBoth()
    {
        var shift = OpenShift();
        Decline(shift, Short(), T0.AddMinutes(2), null, DeclineReason.Heavy, DeclineReason.Stairs);

        var report = Offers.ReportDeclines(new DateOnly(2026, 8, 2), new DateOnly(2026, 8, 2));

        Assert.Equal(1, report.Declines);
        Assert.Equal(1, report.ByReason.Single(c => c.Reason == DeclineReason.Heavy).Count);
        Assert.Equal(1, report.ByReason.Single(c => c.Reason == DeclineReason.Stairs).Count);
    }

    [Fact]
    public void FR21a_TheReportSplitsWhatTheRuleSaidWouldClear()
    {
        var shift = OpenShift();
        Decline(shift, Clearing(), T0.AddMinutes(2), null, DeclineReason.Stairs);
        Decline(shift, Short(), T0.AddMinutes(3), null, DeclineReason.PayTooLow);
        Decline(shift, Short(), T0.AddMinutes(4), null, DeclineReason.TooFar);

        var report = Offers.ReportDeclines(new DateOnly(2026, 8, 2), new DateOnly(2026, 8, 2));

        Assert.Equal(3, report.Declines);
        Assert.Equal(1, report.RuleSaidClears);
        Assert.Equal(2, report.RuleSaidDoesNotClear);
    }

    [Fact]
    public void FR21a_TheRangeIsByLocalDateInclusive()
    {
        var shift = OpenShift();
        // 23:30 at UTC-5 is the next day in UTC; it belongs to its local date.
        Decline(shift, Short(), new DateTimeOffset(2026, 8, 2, 23, 30, 0, TimeSpan.FromHours(-5)), null, DeclineReason.Heavy);
        Decline(shift, Short(), new DateTimeOffset(2026, 8, 3, 0, 30, 0, TimeSpan.FromHours(-5)), null, DeclineReason.Heavy);
        Decline(shift, Short(), new DateTimeOffset(2026, 8, 4, 8, 0, 0, TimeSpan.FromHours(-5)), null, DeclineReason.Heavy);

        Assert.Equal(1, Offers.ReportDeclines(new DateOnly(2026, 8, 2), new DateOnly(2026, 8, 2)).Declines);
        Assert.Equal(2, Offers.ReportDeclines(new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 4)).Declines);
        Assert.Equal(3, Offers.ReportDeclines(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)).Declines);
    }

    [Fact]
    public void FR21a_AnEmptyRangeReportsZeros()
    {
        var report = Offers.ReportDeclines(new DateOnly(2026, 8, 2), new DateOnly(2026, 8, 2));
        Assert.Equal(0, report.Declines);
        Assert.Equal(Enum.GetValues<DeclineReason>().Length, report.ByReason.Count);
        Assert.All(report.ByReason, c => Assert.Equal(0, c.Count));
    }
}

/// <summary>FR-2a and FR-21a through HTTP (FR-33, SDD 6.9).</summary>
public sealed class DeclineApiTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly DateTimeOffset T0 = new(2026, 8, 2, 6, 57, 0, TimeSpan.FromHours(-5));

    private readonly LedgerApp _app = new();
    private readonly HttpClient _http;

    public DeclineApiTests() => _http = _app.CreateClient();

    public void Dispose()
    {
        _http.Dispose();
        _app.Dispose();
    }

    private static Offer Run() => new(
        new(34.89m, Grade.Stated), new(6.6m, Grade.Stated), 2, 32, new(58, Grade.Stated), T0.AddMinutes(1));

    private async Task<Guid> StartShift()
    {
        var response = await _http.PostAsJsonAsync("/api/shifts", new StartShiftRequest("Spark", T0, new(17_000m, Grade.Measured)), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Created>(Json))!.Id;
    }

    private Task<HttpResponseMessage> PostDecline(Guid shift, DeclineReason[] reasons, string? note) =>
        _http.PostAsJsonAsync($"/api/shifts/{shift}/declines", new { offer = Run(), reasons, note, declinedAt = T0.AddMinutes(2) }, Json);

    [Fact]
    public async Task API_DeclineStoresAndReturnsItsId()
    {
        var shift = await StartShift();

        var response = await PostDecline(shift, [DeclineReason.Heavy], null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<Created>(Json))!.Id;
        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task API_OtherWithoutANoteIsABadRequestWithTheReason()
    {
        var shift = await StartShift();

        var response = await PostDecline(shift, [DeclineReason.Other], null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("note", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task API_DeclineReportCountsByReason()
    {
        var shift = await StartShift();
        (await PostDecline(shift, [DeclineReason.Heavy, DeclineReason.Stairs], null)).EnsureSuccessStatusCode();

        var report = await _http.GetFromJsonAsync<DeclineReport>("/api/declines?from=2026-08-02&to=2026-08-02", Json);

        Assert.Equal(1, report!.Declines);
        Assert.Equal(1, report.ByReason.Single(c => c.Reason == DeclineReason.Stairs).Count);
    }
}
