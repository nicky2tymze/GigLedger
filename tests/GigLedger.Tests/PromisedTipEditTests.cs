using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bunit;
using GigLedger.Core;
using GigLedger.Data;
using GigLedger.Web;
using GigLedger.Web.Components.Pages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GigLedger.Tests;

/// <summary>
/// Slice 4, story 3, SRS 0.9 FR-6a: the promised tip added or changed after accept (SDD 0.10, 6.11),
/// written before the code. Adding fills a blank and needs no reason; a change needs one (FR-25).
/// </summary>
public sealed class PromisedTipEditTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 2, 6, 57, 0, TimeSpan.FromHours(-5));
    private static readonly GradedActuals Run = new(new(58, Grade.Entered), new(6.4m, Grade.Entered), new(5.7m, Grade.Entered));

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly LedgerContext _db;
    private readonly LedgerServices _ledger;
    private readonly TestClock _clock = new(T0.AddDays(1));

    public PromisedTipEditTests()
    {
        _connection.Open();
        _db = LedgerDatabase.Open(_connection);
        _ledger = new LedgerServices(_db, _clock);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private IShiftService Shifts => _ledger;
    private IOfferService Offers => _ledger;
    private ITripService Trips => _ledger;
    private ITaxService Taxes => _ledger;

    private (Guid Shift, Guid Trip) Trip(decimal? promised = null)
    {
        var shift = Shifts.Start("Spark", T0, new(17_000m, Grade.Measured));
        var offer = new Offer(new(34.89m, Grade.Stated), new(6.6m, Grade.Stated), 2, 32, new(58, Grade.Stated), T0,
            PromisedTip: promised is { } p ? new Graded<decimal>(p, Grade.Stated) : null);
        var trip = Offers.Accept(shift, offer, T0.AddMinutes(2));
        Trips.RecordActuals(trip, Run);
        return (shift, trip);
    }

    [Fact]
    public void FR6a_ATipAddedAfterAcceptNeedsNoReasonAndTracksTheTrip()
    {
        var (_, trip) = Trip();
        Trips.SetPromisedTip(trip, 8m);

        var stored = Trips.Get(trip);
        Assert.Equal(new Graded<decimal>(8m, Grade.Stated), stored.Offer.PromisedTip);
        Assert.True(stored.Tips.Tracked);
        Assert.Equal(26.89m, stored.Tips.Base);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void FR6a_ChangingATipNeedsAReason(string? reason)
    {
        var (_, trip) = Trip(promised: 8m);
        var e = Assert.Throws<ArgumentException>(() => Trips.SetPromisedTip(trip, 9m, reason));
        Assert.Contains("reason", e.Message);
        Assert.Equal(8m, Trips.Get(trip).Offer.PromisedTip!.Value.Value);
    }

    [Fact]
    public void FR6a_AChangedTipIsTheCurrentOneAndTheOriginalStaysStored()
    {
        var (_, trip) = Trip(promised: 8m);
        Trips.SetPromisedTip(trip, 9.50m, "misread the offer");
        Trips.SetPromisedTip(trip, 10m, "misread it again");

        Assert.Equal(10m, Trips.Get(trip).Offer.PromisedTip!.Value.Value);
        Assert.Equal(8m, _db.Trips.AsNoTracking().Single(t => t.Id == trip).PromisedTip);
        var versions = _db.PromisedTips.AsNoTracking().Where(p => p.TripId == trip).AsEnumerable().OrderBy(p => p.RecordedAt).ToList();
        Assert.Equal(["misread the offer", "misread it again"], versions.Select(v => v.CorrectionReason));
        Assert.Equal(versions[0].Id, versions[1].SupersedesId);
    }

    [Fact]
    public void FR6a_AddingATipAfterTheShiftClosedNeedsAReason()
    {
        // The Architect, 2026-10-03: "to add a tip after a shift is complete, requires explanation".
        var (shift, trip) = Trip();
        Shifts.End(shift, T0.AddHours(2), new(17_012.1m, Grade.Measured));

        Assert.Throws<ArgumentException>(() => Trips.SetPromisedTip(trip, 8m));
        Trips.SetPromisedTip(trip, 8m, "forgot to enter it at accept");
        Assert.Equal(8m, Trips.Get(trip).Offer.PromisedTip!.Value.Value);
    }

    [Fact]
    public void FR6a_AnAddedTipAboveThePayIsRefused()
    {
        var (_, trip) = Trip();
        Assert.Throws<ArgumentOutOfRangeException>(() => Trips.SetPromisedTip(trip, 40m));
        Assert.Null(Trips.Get(trip).Offer.PromisedTip);
    }

    [Fact]
    public void FR6a_AnAddedTipIsCheckedAgainstTheTipLimit()
    {
        var shift = Shifts.Start("Spark", T0, new(17_000m, Grade.Measured));
        var trip = Offers.Accept(shift, new Offer(new(70m, Grade.Stated), new(6.6m, Grade.Stated), 2, 32, new(58, Grade.Stated), T0), T0.AddMinutes(2));

        Assert.Throws<NeedsAcknowledgementException>(() => Trips.SetPromisedTip(trip, 45m));
        Trips.SetPromisedTip(trip, 45m, acknowledgement: new Acknowledgement(true));
        Assert.Equal(45m, Trips.Get(trip).Offer.PromisedTip!.Value.Value);
    }

    [Fact]
    public void FR6a_ChangingTheTipAfterAllTipsInRecomputesTheAdjustment()
    {
        var (_, trip) = Trip(promised: 8m);
        Trips.RecordTip(trip, 6.50m, T0.AddHours(3));
        Trips.MarkAllTipsIn(trip, T0.AddDays(3));
        Trips.SetPromisedTip(trip, 6.50m, "the offer said 6.50");

        Assert.Equal(0m, Trips.Get(trip).Tips.Adjustment);
    }

    [Fact]
    public void FR31_TheTaxMonthsUseTheCurrentPromisedTip()
    {
        _clock.Now = new(2026, 10, 5, 8, 0, 0, TimeSpan.FromHours(-5));
        var shift = Shifts.Start("Spark", new(2026, 9, 30, 20, 0, 0, TimeSpan.FromHours(-5)), new(17_000m, Grade.Measured));
        var trip = Offers.Accept(shift, new Offer(new(34.89m, Grade.Stated), new(6.6m, Grade.Stated), 2, 32, new(58, Grade.Stated),
            new(2026, 9, 30, 20, 0, 0, TimeSpan.FromHours(-5))), new(2026, 9, 30, 20, 5, 0, TimeSpan.FromHours(-5)));
        Trips.SetPromisedTip(trip, 8m);
        Trips.RecordTip(trip, 6.50m, new(2026, 10, 1, 8, 0, 0, TimeSpan.FromHours(-5)));
        Trips.MarkAllTipsIn(trip, new(2026, 10, 3, 8, 0, 0, TimeSpan.FromHours(-5)));

        var months = Taxes.RecordedByMonth(2026, "Spark");
        Assert.Equal(26.89m, months[8]);
        Assert.Equal(6.50m, months[9]);
    }

    [Fact]
    public void FR25_PromisedTipsAreNeverUpdatedOrDeleted()
    {
        var (_, trip) = Trip();
        Trips.SetPromisedTip(trip, 8m);
        Assert.Contains("never deleted", Assert.Throws<SqliteException>(() => _db.Database.ExecuteSqlRaw("DELETE FROM PromisedTips")).Message);
        Assert.Contains("never updated", Assert.Throws<SqliteException>(() => _db.Database.ExecuteSqlRaw("UPDATE PromisedTips SET Amount = 0")).Message);
    }
}

/// <summary>The promised tip after accept, through HTTP.</summary>
public sealed class PromisedTipEditApiTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly DateTimeOffset T0 = new(2026, 8, 2, 6, 57, 0, TimeSpan.FromHours(-5));

    private readonly LedgerApp _app = new();
    private readonly HttpClient _http;

    public PromisedTipEditApiTests() => _http = _app.CreateClient();

    public void Dispose()
    {
        _http.Dispose();
        _app.Dispose();
    }

    [Fact]
    public async Task API_ATipIsAddedThenAChangeWithoutAReasonIsABadRequest()
    {
        var shift = (await (await _http.PostAsJsonAsync("/api/shifts", new StartShiftRequest("Spark", T0, new(17_000m, Grade.Measured)), Json))
            .Content.ReadFromJsonAsync<Created>(Json))!.Id;
        var offer = new Offer(new(34.89m, Grade.Stated), new(6.6m, Grade.Stated), 2, 32, new(58, Grade.Stated), T0);
        var trip = (await (await _http.PostAsJsonAsync($"/api/shifts/{shift}/trips", new AcceptOfferRequest(offer, T0), Json))
            .Content.ReadFromJsonAsync<Created>(Json))!.Id;

        Assert.Equal(HttpStatusCode.NoContent, (await _http.PostAsJsonAsync($"/api/trips/{trip}/promised-tip", new PromisedTipRequest(8m), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _http.PostAsJsonAsync($"/api/trips/{trip}/promised-tip", new PromisedTipRequest(9m), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _http.PostAsJsonAsync($"/api/trips/{trip}/promised-tip", new PromisedTipRequest(9m, "misread"), Json)).StatusCode);
    }
}

/// <summary>Adding and changing the tip on the Shift page.</summary>
public sealed class PromisedTipEditPageTests : TestContext
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 3, 5, 45, 0, TimeSpan.FromHours(-5));

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly LedgerServices _ledger;
    private readonly TestClock _clock = new(T0.AddHours(2));

    public PromisedTipEditPageTests()
    {
        _connection.Open();
        var db = LedgerDatabase.Open(_connection);
        _ledger = new LedgerServices(db, _clock);
        Services.AddSingleton<TimeProvider>(_clock);
        Services.AddSingleton<ISettingsService>(_ledger);
        Services.AddSingleton<IOfferService>(_ledger);
        Services.AddSingleton<ITripService>(_ledger);
        Services.AddSingleton<IShiftService>(_ledger);
        Services.AddSingleton<IEntryCheckService>(_ledger);
        Services.AddSingleton(new HeldOffer());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }

    private ITripService Trips => _ledger;

    private Guid Trip(decimal? promised = null)
    {
        var shift = ((IShiftService)_ledger).Start("Spark", T0, new(17_000m, Grade.Measured));
        var trip = ((IOfferService)_ledger).Accept(shift, new Offer(new(34.89m, Grade.Stated), new(6.6m, Grade.Stated), 2, 32, new(58, Grade.Stated), T0,
            PromisedTip: promised is { } p ? new Graded<decimal>(p, Grade.Stated) : null), T0);
        Trips.RecordActuals(trip, new(new(58, Grade.Entered), new(6.4m, Grade.Entered), new(5.7m, Grade.Entered)));
        return trip;
    }

    [Fact]
    public void UI_Shift_ATipCanBeAddedToAnUntrackedTrip()
    {
        var trip = Trip();
        var page = RenderComponent<Home>();

        page.Find("#promised-0").Change("8");
        page.Find("#set-promised-0").Click();

        Assert.Equal(8m, Trips.Get(trip).Offer.PromisedTip!.Value.Value);
        Assert.Contains("tip pending", page.Find("tr.trip-tip").TextContent);
    }

    [Fact]
    public void UI_Shift_ChangingATipAsksForAReason()
    {
        var trip = Trip(promised: 8m);
        var page = RenderComponent<Home>();

        page.Find("#promised-0").Change("9");
        page.Find("#set-promised-0").Click();
        Assert.Contains("reason", page.Find(".error").TextContent);
        Assert.Equal(8m, Trips.Get(trip).Offer.PromisedTip!.Value.Value);

        page.Find("#promised-reason-0").Change("misread the offer");
        page.Find("#set-promised-0").Click();
        Assert.Equal(9m, Trips.Get(trip).Offer.PromisedTip!.Value.Value);
    }
}
