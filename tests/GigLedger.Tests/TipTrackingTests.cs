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
/// Slice 4, story 3: tip tracking (SRS 0.8 FR-1, FR-6, FR-6a, FR-17, FR-31; SDD 0.9 section 6.11),
/// written before the code. Pay is the total shown, tip included; a posted tip is never added on top.
/// </summary>
public sealed class TipRuleTests
{
    private static readonly DateTimeOffset T = new(2026, 8, 2, 7, 0, 0, TimeSpan.FromHours(-5));

    [Fact]
    public void FR6_AnUntrackedTripsGrossIsItsPayWhateverPosts() =>
        Assert.Equal(new TipState(false, 34.89m, 0m, false, 6m, null), TipAccounting.State(34.89m, null, 6m, false));

    [Fact]
    public void FR6a_ATrackedTripIsPendingAndItsGrossIsThePay()
    {
        var state = TipAccounting.State(34.89m, 8m, 0m, false);
        Assert.Equal(new TipState(true, 26.89m, 8m, true, 0m, null), state);
        Assert.Equal(34.89m, state.Gross);
    }

    [Fact]
    public void FR6a_AllInTheGrossIsTheBasePlusWhatPostedAndTheAdjustmentShows()
    {
        var state = TipAccounting.State(34.89m, 8m, 6.50m, true);
        Assert.Equal(33.39m, state.Gross);
        Assert.Equal(-1.50m, state.Adjustment);
        Assert.False(state.Pending);
    }

    [Fact]
    public void FR6a_AllInWithNothingPostedIsAKnownZeroTip()
    {
        var state = TipAccounting.State(34.89m, 8m, 0m, true);
        Assert.Equal(26.89m, state.Gross);
        Assert.Equal(-8m, state.Adjustment);
    }

    [Fact]
    public void FR6a_AllTipsInOpens24HoursAfterTheTripEnds()
    {
        Assert.Equal(T.AddMinutes(58).AddHours(24), TipAccounting.AllTipsInOpensAt(T, 58));
        Assert.Equal(T.AddHours(24), TipAccounting.AllTipsInOpensAt(T, null));
    }

    [Fact]
    public void FR6a_APromisedTipCanBeAllOfThePayButNotMoreOrNegative()
    {
        TipAccounting.RefuseImpossiblePromise(10m, 10m);
        Assert.Throws<ArgumentOutOfRangeException>(() => TipAccounting.RefuseImpossiblePromise(10m, 10.01m));
        Assert.Throws<ArgumentOutOfRangeException>(() => TipAccounting.RefuseImpossiblePromise(10m, -1m));
    }
}

/// <summary>Tip tracking in storage, the shift summary, the rates, and the tax months.</summary>
public sealed class TipTrackingStorageTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 2, 6, 57, 0, TimeSpan.FromHours(-5));
    private static readonly GradedActuals Run = new(new(58, Grade.Entered), new(6.4m, Grade.Entered), new(5.7m, Grade.Entered));

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly LedgerContext _db;
    private readonly LedgerServices _ledger;
    private readonly TestClock _clock = new(T0.AddDays(1));

    public TipTrackingStorageTests()
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
    private ISettingsService Settings => _ledger;
    private ITaxService Taxes => _ledger;

    private static Offer Total(decimal pay, decimal? promised = null) => new(
        new(pay, Grade.Stated), new(6.6m, Grade.Stated), 2, 32, new(58, Grade.Stated), T0.AddMinutes(1),
        PromisedTip: promised is { } p ? new Graded<decimal>(p, Grade.Stated) : null);

    private Guid Shift() => Shifts.Start("Spark", T0, new(17_000m, Grade.Measured));

    private (Guid Shift, Guid Trip) TripWithActuals(Offer offer)
    {
        var shift = Shift();
        var trip = Offers.Accept(shift, offer, T0.AddMinutes(2));
        Trips.RecordActuals(trip, Run);
        return (shift, trip);
    }

    private ShiftSummary Close(Guid shift)
    {
        Shifts.End(shift, T0.AddHours(2), new(17_012.1m, Grade.Measured));
        return Shifts.Summary(shift);
    }

    // ---- FR-6: posted tips ----

    [Fact]
    public void FR6_ATripCanHaveSeveralPostedTipsAndTheyAreSummed()
    {
        var (_, trip) = TripWithActuals(Total(34.89m));
        Trips.RecordTip(trip, 2.00m, T0.AddHours(3));
        Trips.RecordTip(trip, 4.50m, T0.AddHours(5));
        Assert.Equal(6.50m, Trips.Get(trip).Tip!.Value.Value);
    }

    [Fact]
    public void FR6_ANegativeTipIsStillRefused()
    {
        var (_, trip) = TripWithActuals(Total(34.89m));
        Assert.Throws<ArgumentOutOfRangeException>(() => Trips.RecordTip(trip, -1m, T0.AddHours(3)));
    }

    [Fact]
    public void FR6_OnAnUntrackedTripAPostedTipIsNeverAddedToThePay()
    {
        var (shift, trip) = TripWithActuals(Total(34.89m));
        Trips.RecordTip(trip, 6.00m, T0.AddHours(3));

        var summary = Close(shift);
        Assert.Equal(34.89m, summary.Gross);
        Assert.Equal(0m, summary.Tips);
    }

    [Fact]
    public void FR17_AnUntrackedTripsRatesUseItsPay()
    {
        var (_, trip) = TripWithActuals(Total(34.89m));
        Trips.RecordTip(trip, 6.00m, T0.AddHours(3));

        var rates = Trips.Report(trip).Rates!;
        Assert.Equal(Calculations.ActualGrossPerHour(34.89m, Run.Values).Value, rates.GrossPerHour.Value);
    }

    // ---- FR-6a: tracking ----

    [Fact]
    public void FR6a_ThePromisedTipIsStoredWithTheTrip()
    {
        var (_, trip) = TripWithActuals(Total(34.89m, 8m));
        Assert.Equal(new Graded<decimal>(8m, Grade.Stated), Trips.Get(trip).Offer.PromisedTip);
    }

    [Fact]
    public void FR6a_APromisedTipAboveThePayIsRefusedAndNothingIsStored()
    {
        var shift = Shift();
        Assert.Throws<ArgumentOutOfRangeException>(() => Offers.Accept(shift, Total(10m, 12m), T0.AddMinutes(2)));
        Assert.Empty(Trips.OnShift(shift));
    }

    [Fact]
    public void FR6a_ThePromisedTipIsCheckedAgainstTheTipLimit()
    {
        var shift = Shift();
        var e = Assert.Throws<NeedsAcknowledgementException>(() => Offers.Accept(shift, Total(70m, 45m), T0.AddMinutes(2)));
        Assert.Equal(EntryLimit.Tip, Assert.Single(e.Checks).Limit);
    }

    [Fact]
    public void FR6a_WhilePendingTheGrossIsThePayAndThePromisedTipIsCounted()
    {
        var (shift, trip) = TripWithActuals(Total(34.89m, 8m));
        Trips.RecordTip(trip, 6.50m, T0.AddHours(3));

        Assert.True(Trips.Get(trip).Tips.Pending);
        var summary = Close(shift);
        Assert.Equal(34.89m, summary.Gross);
        Assert.Equal(8m, summary.Tips);
    }

    [Fact]
    public void FR6a_AllTipsInIsRefusedUntil24HoursAfterTheTripEnded()
    {
        var (_, trip) = TripWithActuals(Total(34.89m, 8m));
        var opens = Trips.Get(trip).AllTipsInOpensAt;
        Assert.Equal(T0.AddMinutes(2 + 58).AddHours(24), opens);

        var e = Assert.Throws<InvalidOperationException>(() => Trips.MarkAllTipsIn(trip, opens.AddSeconds(-1)));
        Assert.Contains("change", e.Message);
        Trips.MarkAllTipsIn(trip, opens);
        Assert.True(Trips.Get(trip).AllTipsIn);
    }

    [Fact]
    public void FR6a_AnUntrackedTripHasNothingToMark()
    {
        var (_, trip) = TripWithActuals(Total(34.89m));
        Assert.Throws<InvalidOperationException>(() => Trips.MarkAllTipsIn(trip, T0.AddDays(3)));
    }

    [Fact]
    public void FR6a_AllTipsInIsMarkedOnce()
    {
        var (_, trip) = TripWithActuals(Total(34.89m, 8m));
        Trips.MarkAllTipsIn(trip, T0.AddDays(3));
        Assert.Throws<InvalidOperationException>(() => Trips.MarkAllTipsIn(trip, T0.AddDays(4)));
    }

    [Fact]
    public void FR6a_AllInTheGrossIsTheBasePlusWhatPosted()
    {
        var (shift, trip) = TripWithActuals(Total(34.89m, 8m));
        Trips.RecordTip(trip, 6.50m, T0.AddHours(3));
        Trips.MarkAllTipsIn(trip, T0.AddDays(3));

        Assert.Equal(-1.50m, Trips.Get(trip).Tips.Adjustment);
        var summary = Close(shift);
        Assert.Equal(33.39m, summary.Gross);
        Assert.Equal(6.50m, summary.Tips);
    }

    [Fact]
    public void FR6a_ALateTipIsAcceptedAndTheGrossRecomputed()
    {
        var (shift, trip) = TripWithActuals(Total(34.89m, 8m));
        Trips.RecordTip(trip, 6.50m, T0.AddHours(3));
        Trips.MarkAllTipsIn(trip, T0.AddDays(3));
        Trips.RecordTip(trip, 1.50m, T0.AddDays(3).AddHours(1));

        Assert.Equal(0m, Trips.Get(trip).Tips.Adjustment);
        Assert.Equal(34.89m, Close(shift).Gross);
    }

    [Fact]
    public void FR17_ATrackedTripsRatesBeforeTheTipUseItsBase()
    {
        var (_, trip) = TripWithActuals(Total(34.89m, 8m));
        var rates = Trips.Report(trip).Rates!;
        Assert.Equal(Calculations.ActualGrossPerHour(26.89m, Run.Values).Value, rates.GrossPerHourBeforeTip.Value);
        Assert.Equal(Calculations.ActualGrossPerHour(34.89m, Run.Values).Value, rates.GrossPerHour.Value);
    }

    // ---- FR-31: tax months, no import ----

    [Fact]
    public void FR31_AnUntrackedTripCountsWholeInTheMonthAccepted()
    {
        _clock.Now = new(2026, 10, 5, 8, 0, 0, TimeSpan.FromHours(-5));
        var shift = Shifts.Start("Spark", new(2026, 9, 30, 20, 0, 0, TimeSpan.FromHours(-5)), new(17_000m, Grade.Measured));
        var trip = Offers.Accept(shift, Total(34.89m), new(2026, 9, 30, 20, 5, 0, TimeSpan.FromHours(-5)));
        Trips.RecordTip(trip, 6.00m, new(2026, 10, 1, 8, 0, 0, TimeSpan.FromHours(-5)));

        var months = Taxes.RecordedByMonth(2026, "Spark");
        Assert.Equal(34.89m, months[8]);
        Assert.Equal(0m, months[9]);
    }

    [Fact]
    public void FR31_AnAllInTripCountsItsBaseWhenAcceptedAndItsTipsWhenPosted()
    {
        _clock.Now = new(2026, 10, 5, 8, 0, 0, TimeSpan.FromHours(-5));
        var shift = Shifts.Start("Spark", new(2026, 9, 30, 20, 0, 0, TimeSpan.FromHours(-5)), new(17_000m, Grade.Measured));
        var trip = Offers.Accept(shift, Total(34.89m, 8m), new(2026, 9, 30, 20, 5, 0, TimeSpan.FromHours(-5)));
        Trips.RecordTip(trip, 6.50m, new(2026, 10, 1, 8, 0, 0, TimeSpan.FromHours(-5)));
        Trips.MarkAllTipsIn(trip, new(2026, 10, 3, 8, 0, 0, TimeSpan.FromHours(-5)));

        var months = Taxes.RecordedByMonth(2026, "Spark");
        Assert.Equal(26.89m, months[8]);
        Assert.Equal(6.50m, months[9]);
    }

    // ---- FR-25 ----

    [Fact]
    public void FR25_AllTipsInIsNeverUpdatedOrDeleted()
    {
        var (_, trip) = TripWithActuals(Total(34.89m, 8m));
        Trips.MarkAllTipsIn(trip, T0.AddDays(3));
        Assert.Contains("never deleted", Assert.Throws<SqliteException>(() => _db.Database.ExecuteSqlRaw("DELETE FROM TipsIn")).Message);
        Assert.Contains("never updated", Assert.Throws<SqliteException>(() => _db.Database.ExecuteSqlRaw("UPDATE TipsIn SET At = At")).Message);
    }
}

/// <summary>Tip tracking through HTTP (FR-33).</summary>
public sealed class TipTrackingApiTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly DateTimeOffset T0 = new(2026, 8, 2, 6, 57, 0, TimeSpan.FromHours(-5));

    private readonly LedgerApp _app = new();
    private readonly HttpClient _http;

    public TipTrackingApiTests() => _http = _app.CreateClient();

    public void Dispose()
    {
        _http.Dispose();
        _app.Dispose();
    }

    [Fact]
    public async Task API_SettingsCanBeReadAndSet()
    {
        var settings = await _http.GetFromJsonAsync<Settings>("/api/settings", Json);
        Assert.Equal(Core.Settings.Initial, settings);
        (await _http.PostAsJsonAsync("/api/settings", settings! with { AcceptThreshold = 30m }, Json)).EnsureSuccessStatusCode();
        Assert.Equal(30m, (await _http.GetFromJsonAsync<Settings>("/api/settings", Json))!.AcceptThreshold);
    }

    [Fact]
    public async Task API_AllTipsInIsMarkedThroughTheApi()
    {
        var shift = (await (await _http.PostAsJsonAsync("/api/shifts", new StartShiftRequest("Spark", T0, new(17_000m, Grade.Measured)), Json))
            .Content.ReadFromJsonAsync<Created>(Json))!.Id;
        var offer = new Offer(new(34.89m, Grade.Stated), new(6.6m, Grade.Stated), 2, 32, new(58, Grade.Stated), T0, PromisedTip: new(8m, Grade.Stated));
        var trip = (await (await _http.PostAsJsonAsync($"/api/shifts/{shift}/trips", new AcceptOfferRequest(offer, T0), Json))
            .Content.ReadFromJsonAsync<Created>(Json))!.Id;

        var early = await _http.PostAsJsonAsync($"/api/trips/{trip}/tips-in", new TipsInRequest(T0.AddHours(1)), Json);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        var marked = await _http.PostAsJsonAsync($"/api/trips/{trip}/tips-in", new TipsInRequest(T0.AddDays(2)), Json);
        Assert.Equal(HttpStatusCode.NoContent, marked.StatusCode);
    }
}

/// <summary>Tip tracking screens (SDD 6.11).</summary>
public sealed class TipTrackingPageTests : TestContext
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 3, 5, 45, 0, TimeSpan.FromHours(-5));

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly LedgerServices _ledger;
    private readonly TestClock _clock = new(T0);

    public TipTrackingPageTests()
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

    private ISettingsService Settings => _ledger;
    private IShiftService Shifts => _ledger;
    private IOfferService Offers => _ledger;
    private ITripService Trips => _ledger;

    private Guid TrackedTrip()
    {
        var shift = Shifts.Start("Spark", T0, new(17_000m, Grade.Measured));
        var trip = Offers.Accept(shift, new Offer(new(34.89m, Grade.Stated), new(6.6m, Grade.Stated), 2, 32, new(58, Grade.Stated), T0,
            PromisedTip: new(8m, Grade.Stated)), T0);
        Trips.RecordActuals(trip, new(new(58, Grade.Entered), new(6.4m, Grade.Entered), new(5.7m, Grade.Entered)));
        return trip;
    }

    [Fact]
    public void UI_Offer_ThePromisedTipFieldIsAlwaysThere()
    {
        // SRS 0.9: no setting gates it.
        var page = RenderComponent<OfferPage>();
        Assert.NotEmpty(page.FindAll("#promised-tip"));
    }

    [Fact]
    public void UI_Offer_ABlankPromisedTipLeavesTheTripUntracked()
    {
        var shift = Shifts.Start("Spark", T0, new(17_000m, Grade.Measured));
        var page = RenderComponent<OfferPage>();
        page.Find("#pay").Change("34.89");
        page.Find("#stated-miles").Change("6.6");
        page.Find("#items").Change("32");
        page.Find("#est-minutes").Change("58");
        page.Find("#evaluate").Click();
        page.Find("#accept").Click();

        Assert.Null(Assert.Single(Trips.OnShift(shift)).Offer.PromisedTip);
    }

    [Fact]
    public void UI_Offer_ThePromisedTipIsStoredWithTheTrip()
    {
        var shift = Shifts.Start("Spark", T0, new(17_000m, Grade.Measured));
        var page = RenderComponent<OfferPage>();
        page.Find("#pay").Change("34.89");
        page.Find("#stated-miles").Change("6.6");
        page.Find("#items").Change("32");
        page.Find("#est-minutes").Change("58");
        page.Find("#promised-tip").Change("8");
        page.Find("#evaluate").Click();
        page.Find("#accept").Click();

        Assert.Equal(8m, Assert.Single(Trips.OnShift(shift)).Offer.PromisedTip!.Value.Value);
    }

    [Fact]
    public void UI_Shift_ATrackedTripShowsPendingAndHidesAllTipsInFor24Hours()
    {
        TrackedTrip();
        _clock.Now = T0.AddHours(20);

        var page = RenderComponent<Home>();

        Assert.Contains("tip pending", page.Find("tr.trip-tip").TextContent);
        Assert.Empty(page.FindAll("#tips-in-0"));
    }

    [Fact]
    public void UI_Shift_After24HoursAllTipsInMarksTheTrip()
    {
        var trip = TrackedTrip();
        _clock.Now = T0.AddMinutes(58).AddHours(24);

        var page = RenderComponent<Home>();
        page.Find("#tips-in-0").Click();

        Assert.True(Trips.Get(trip).AllTipsIn);
        Assert.Contains("all tips in", page.Find("tr.trip-tip").TextContent);
    }
}
