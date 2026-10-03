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
/// Slice 4, story 4: cancelled trips (SRS 0.10 FR-3, FR-3a, FR-21b; SDD 0.11 section 6.12), written before the code.
/// </summary>
public sealed class CancelRuleTests
{
    private static readonly DateTimeOffset T = new(2026, 8, 2, 7, 30, 0, TimeSpan.FromHours(-5));

    [Fact]
    public void FR3a_TheReasonsAreTheArchitectsListInHisOrder()
    {
        Assert.Equal(
            [CancelReason.OrderNotReady, CancelReason.Unreachable, CancelReason.WrongAddress, CancelReason.CustomerIssue,
             CancelReason.Emergency, CancelReason.CarProblems, CancelReason.Other],
            Enum.GetValues<CancelReason>());
        Assert.Equal(
            ["Order not ready", "Unreachable", "Wrong address", "Customer issue (dogs, attitude)", "Emergency", "Car problems", "Other"],
            Enum.GetValues<CancelReason>().Select(CancelRules.Label));
    }

    [Fact]
    public void FR3a_OnlyAfterPickupIsShopped()
    {
        Assert.False(new Cancellation(T, CancelledBy.Customer, CancelStage.BeforePickup).Shopped);
        Assert.True(new Cancellation(T, CancelledBy.Customer, CancelStage.AfterPickup).Shopped);
        Assert.True(new Cancellation(T, CancelledBy.Customer, CancelStage.AtTheDoor).Shopped);
    }

    [Fact]
    public void FR3a_ADriverCancelNeedsAReason() =>
        Assert.Contains("reason", Assert.Throws<ArgumentException>(() =>
            CancelRules.Validate(new Cancellation(T, CancelledBy.Driver, CancelStage.BeforePickup))).Message);

    [Fact]
    public void FR3a_SomeoneElsesCancelTakesNoReason() =>
        Assert.Throws<ArgumentException>(() =>
            CancelRules.Validate(new Cancellation(T, CancelledBy.Customer, CancelStage.BeforePickup, CancelReason.Emergency)));

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void FR3a_OtherNeedsANote(string? note) =>
        Assert.Contains("note", Assert.Throws<ArgumentException>(() =>
            CancelRules.Validate(new Cancellation(T, CancelledBy.Driver, CancelStage.BeforePickup, CancelReason.Other, note))).Message);

    [Fact]
    public void FR3a_TheNoteIsTrimmed() =>
        Assert.Equal("flat tyre", CancelRules.Validate(new Cancellation(T, CancelledBy.Driver, CancelStage.AfterPickup, CancelReason.CarProblems, " flat tyre ")).Note);
}

/// <summary>Cancelling in storage: what it paid, its miles and minutes, the summary, the taxes, the report.</summary>
public sealed class CancelStorageTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 2, 6, 57, 0, TimeSpan.FromHours(-5));

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly LedgerContext _db;
    private readonly LedgerServices _ledger;
    private readonly TestClock _clock = new(T0.AddDays(1));

    public CancelStorageTests()
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

    private static GradedActuals Drove(int minutes, decimal route, decimal back) =>
        new(new(minutes, Grade.Entered), new(route, Grade.Entered), new(back, Grade.Entered));

    private (Guid Shift, Guid Trip) Accepted(decimal? promised = null)
    {
        // One shift open at a time: reuse it when a test accepts more than one trip.
        var shift = Shifts.Open()?.Id ?? Shifts.Start("Spark", T0, new(17_000m, Grade.Measured));
        var offer = new Offer(new(34.89m, Grade.Stated), new(6.6m, Grade.Stated), 1, 12, new(40, Grade.Stated), T0,
            PromisedTip: promised is { } p ? new Graded<decimal>(p, Grade.Stated) : null);
        return (shift, Offers.Accept(shift, offer, T0.AddMinutes(1)));
    }

    private static Cancellation At(CancelStage stage, CancelledBy by = CancelledBy.Customer, CancelReason? reason = null) =>
        new(T0.AddMinutes(20), by, stage, reason);

    [Fact]
    public void FR3a_ANotShoppedCancelPaysNothingAndHasNoMilesOrTripTime()
    {
        var (_, trip) = Accepted(promised: 8m);
        Trips.Cancel(trip, At(CancelStage.BeforePickup));

        var stored = Trips.Get(trip);
        Assert.Equal(0m, stored.Cancel!.Paid);
        Assert.Null(stored.Actuals);
        Assert.Equal(0m, stored.Tips.Gross);
        Assert.Equal(T0.AddMinutes(20), stored.Cancel.Cancellation.At);
    }

    [Fact]
    public void FR3a_ANotShoppedCancelTakesNoActuals() =>
        Assert.Throws<ArgumentException>(() => Trips.Cancel(Accepted().Trip, At(CancelStage.BeforePickup), Drove(10, 2m, 2m)));

    [Fact]
    public void FR3a_AShoppedCancelPaysThePayMinusTheTipAndKeepsItsActuals()
    {
        var (_, trip) = Accepted(promised: 8m);
        Trips.Cancel(trip, At(CancelStage.AfterPickup), Drove(25, 3.1m, 3.1m));

        var stored = Trips.Get(trip);
        Assert.Equal(26.89m, stored.Cancel!.Paid);
        Assert.Equal(26.89m, stored.Tips.Gross);
        Assert.Equal(25, stored.Actuals!.ElapsedMinutes.Value);
    }

    [Fact]
    public void FR3a_AShoppedCancelNeedsItsActuals() =>
        Assert.Throws<ArgumentException>(() => Trips.Cancel(Accepted(promised: 8m).Trip, At(CancelStage.AtTheDoor)));

    [Fact]
    public void FR3a_AShoppedCancelWithNoTipEnteredAsksForIt()
    {
        var (_, trip) = Accepted();
        var e = Assert.Throws<ArgumentException>(() => Trips.Cancel(trip, At(CancelStage.AtTheDoor), Drove(30, 6.6m, 6.6m)));
        Assert.Contains("tip", e.Message);
        Assert.Null(Trips.Get(trip).Cancel);

        Trips.Cancel(trip, At(CancelStage.AtTheDoor), Drove(30, 6.6m, 6.6m), promisedTip: 8m);
        Assert.Equal(26.89m, Trips.Get(trip).Cancel!.Paid);
        Assert.Equal(8m, Trips.Get(trip).Offer.PromisedTip!.Value.Value);
    }

    [Fact]
    public void FR3a_AShoppedCancelsMilesGoThroughTheEntryChecks()
    {
        var (_, trip) = Accepted(promised: 8m);
        Assert.Throws<NeedsAcknowledgementException>(() => Trips.Cancel(trip, At(CancelStage.AfterPickup), Drove(60, 40m, 20m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Trips.Cancel(trip, At(CancelStage.AfterPickup), Drove(0, 4m, 4m)));
        Assert.Null(Trips.Get(trip).Cancel);
    }

    [Fact]
    public void FR3a_ATripWithActualsOrAlreadyCancelledCannotBeCancelled()
    {
        var (_, done) = Accepted(promised: 8m);
        Trips.RecordActuals(done, Drove(40, 6.6m, 6.6m));
        Assert.Throws<InvalidOperationException>(() => Trips.Cancel(done, At(CancelStage.BeforePickup)));

        var (_, twice) = Accepted(promised: 8m);
        Trips.Cancel(twice, At(CancelStage.BeforePickup));
        Assert.Throws<InvalidOperationException>(() => Trips.Cancel(twice, At(CancelStage.BeforePickup)));
    }

    [Fact]
    public void FR3a_ATipPostedOnACancelledTripIsAddedToWhatItPaid()
    {
        var (_, trip) = Accepted(promised: 8m);
        Trips.Cancel(trip, At(CancelStage.AtTheDoor), Drove(30, 6.6m, 6.6m));
        Trips.RecordTip(trip, 3m, T0.AddHours(5));
        Assert.Equal(29.89m, Trips.Get(trip).Tips.Gross);
    }

    [Fact]
    public void FR3a_TheSummaryCountsAShoppedCancelAndLeavesANotShoppedOneOut()
    {
        var shift = Shifts.Start("Spark", T0, new(17_000m, Grade.Measured));
        Offer Run(decimal pay) => new(new(pay, Grade.Stated), new(6.6m, Grade.Stated), 1, 12, new(40, Grade.Stated), T0, PromisedTip: new(8m, Grade.Stated));
        var shopped = Offers.Accept(shift, Run(34.89m), T0.AddMinutes(1));
        var notShopped = Offers.Accept(shift, Run(20m), T0.AddMinutes(30));
        Trips.Cancel(shopped, At(CancelStage.AfterPickup), Drove(25, 3.1m, 3.1m));
        Trips.Cancel(notShopped, new Cancellation(T0.AddMinutes(40), CancelledBy.Store, CancelStage.BeforePickup));
        Shifts.End(shift, T0.AddHours(1), new(17_006.2m, Grade.Measured));

        var summary = Shifts.Summary(shift);
        Assert.Equal(1, summary.Trips);
        Assert.Equal(26.89m, summary.Gross);
        Assert.Equal(25m / 60m, summary.TripHours);
    }

    [Fact]
    public void FR31_ACancelCountsWhatItPaidInTheMonthAccepted()
    {
        _clock.Now = new(2026, 10, 5, 8, 0, 0, TimeSpan.FromHours(-5));
        var shift = Shifts.Start("Spark", new(2026, 9, 30, 20, 0, 0, TimeSpan.FromHours(-5)), new(17_000m, Grade.Measured));
        var trip = Offers.Accept(shift, new Offer(new(34.89m, Grade.Stated), new(6.6m, Grade.Stated), 1, 12, new(40, Grade.Stated),
            new(2026, 9, 30, 20, 0, 0, TimeSpan.FromHours(-5)), PromisedTip: new(8m, Grade.Stated)), new(2026, 9, 30, 20, 5, 0, TimeSpan.FromHours(-5)));
        Trips.Cancel(trip, new Cancellation(new(2026, 9, 30, 20, 30, 0, TimeSpan.FromHours(-5)), CancelledBy.Customer, CancelStage.AtTheDoor),
            Drove(25, 6.6m, 6.6m));
        Trips.RecordTip(trip, 3m, new(2026, 10, 1, 8, 0, 0, TimeSpan.FromHours(-5)));

        var months = Taxes.RecordedByMonth(2026, "Spark");
        Assert.Equal(26.89m, months[8]);
        Assert.Equal(3m, months[9]);
    }

    [Fact]
    public void FR21b_TheReportCountsByWhoWhenAndReasonWithPaidAndMiles()
    {
        var (_, a) = Accepted(promised: 8m);
        Trips.Cancel(a, At(CancelStage.AfterPickup), Drove(25, 3.1m, 3.1m));
        var (_, b) = Accepted(promised: 8m);
        Trips.Cancel(b, new Cancellation(T0.AddMinutes(50), CancelledBy.Driver, CancelStage.BeforePickup, CancelReason.OrderNotReady));

        var report = Trips.ReportCancels(new DateOnly(2026, 8, 2), new DateOnly(2026, 8, 2));

        Assert.Equal(2, report.Cancels);
        Assert.Equal(Enum.GetValues<CancelledBy>(), report.ByWho.Select(c => c.Value));
        Assert.Equal(1, report.ByWho.Single(c => c.Value == CancelledBy.Driver).Count);
        Assert.Equal(1, report.ByWhen.Single(c => c.Value == CancelStage.AfterPickup).Count);
        Assert.Equal(1, report.ByReason.Single(c => c.Value == CancelReason.OrderNotReady).Count);
        Assert.Equal(26.89m, report.Paid);
        Assert.Equal(6.2m, report.ShoppedMiles);
        Assert.Equal(0, Trips.ReportCancels(new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 3)).Cancels);
    }

    [Fact]
    public void FR25_CancelsAreNeverUpdatedOrDeleted()
    {
        Trips.Cancel(Accepted().Trip, At(CancelStage.BeforePickup));
        Assert.Contains("never deleted", Assert.Throws<SqliteException>(() => _db.Database.ExecuteSqlRaw("DELETE FROM Cancels")).Message);
        Assert.Contains("never updated", Assert.Throws<SqliteException>(() => _db.Database.ExecuteSqlRaw("UPDATE Cancels SET Paid = 0")).Message);
    }
}

/// <summary>Cancelling through HTTP.</summary>
public sealed class CancelApiTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly DateTimeOffset T0 = new(2026, 8, 2, 6, 57, 0, TimeSpan.FromHours(-5));

    private readonly LedgerApp _app = new();
    private readonly HttpClient _http;

    public CancelApiTests() => _http = _app.CreateClient();

    public void Dispose()
    {
        _http.Dispose();
        _app.Dispose();
    }

    [Fact]
    public async Task API_ATripIsCancelledAndTheReportCountsIt()
    {
        var shift = (await (await _http.PostAsJsonAsync("/api/shifts", new StartShiftRequest("Spark", T0, new(17_000m, Grade.Measured)), Json))
            .Content.ReadFromJsonAsync<Created>(Json))!.Id;
        var offer = new Offer(new(34.89m, Grade.Stated), new(6.6m, Grade.Stated), 1, 12, new(40, Grade.Stated), T0);
        var trip = (await (await _http.PostAsJsonAsync($"/api/shifts/{shift}/trips", new AcceptOfferRequest(offer, T0), Json))
            .Content.ReadFromJsonAsync<Created>(Json))!.Id;

        var cancel = new CancelRequest(new Cancellation(T0.AddMinutes(10), CancelledBy.Store, CancelStage.BeforePickup));
        Assert.Equal(HttpStatusCode.NoContent, (await _http.PostAsJsonAsync($"/api/trips/{trip}/cancel", cancel, Json)).StatusCode);

        var report = await _http.GetFromJsonAsync<CancelReport>("/api/cancellations?from=2026-08-02&to=2026-08-02", Json);
        Assert.Equal(1, report!.Cancels);
    }
}

/// <summary>Cancelling, and the return default, on the Shift page.</summary>
public sealed class CancelPageTests : TestContext
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 3, 5, 45, 0, TimeSpan.FromHours(-5));

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly LedgerServices _ledger;
    private readonly TestClock _clock = new(T0.AddMinutes(30));

    public CancelPageTests()
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

    private Guid Trip(int drops, decimal? promised = 8m)
    {
        var shift = ((IShiftService)_ledger).Start("Spark", T0, new(17_000m, Grade.Measured));
        return ((IOfferService)_ledger).Accept(shift, new Offer(new(34.89m, Grade.Stated), new(6.6m, Grade.Stated), drops, 12, new(40, Grade.Stated), T0,
            PromisedTip: promised is { } p ? new Graded<decimal>(p, Grade.Stated) : null), T0);
    }

    [Fact]
    public void UI_Shift_AOneDropTripsReturnDefaultsToItsRoute()
    {
        var trip = Trip(drops: 1);
        var page = RenderComponent<Home>();

        page.Find("#minutes-0").Change("40");
        page.Find("#route-0").Change("6.4");
        page.Find("#save-actuals-0").Click();

        Assert.Equal(6.4m, Trips.Get(trip).Actuals!.ReturnMiles.Value);
    }

    [Fact]
    public void UI_Shift_AMultiDropTripsReturnHasNoDefault()
    {
        var trip = Trip(drops: 2);
        var page = RenderComponent<Home>();

        page.Find("#minutes-0").Change("40");
        page.Find("#route-0").Change("6.4");
        page.Find("#save-actuals-0").Click();

        Assert.Null(Trips.Get(trip).Actuals);
        Assert.Contains("return", page.Find(".error").TextContent);
    }

    [Fact]
    public void UI_Shift_ANotShoppedCancel()
    {
        var trip = Trip(drops: 1);
        var page = RenderComponent<Home>();

        page.Find("#cancel-0").Click();
        page.Find("#cancel-by").Change("Store");
        page.Find("#cancel-stage").Change("BeforePickup");
        page.Find("#confirm-cancel").Click();

        var stored = Trips.Get(trip).Cancel!;
        Assert.Equal((CancelledBy.Store, CancelStage.BeforePickup, 0m), (stored.Cancellation.By, stored.Cancellation.Stage, stored.Paid));
        Assert.Contains("cancelled", page.Find("tr.trip").TextContent);
    }

    [Fact]
    public void UI_Shift_AShoppedDriverCancelWithItsReasonMilesAndTheReturnDefault()
    {
        var trip = Trip(drops: 1);
        var page = RenderComponent<Home>();

        page.Find("#cancel-0").Click();
        page.Find("#cancel-by").Change("Driver");
        page.Find("#cancel-reason").Change("CarProblems");
        page.Find("#cancel-stage").Change("AfterPickup");
        Assert.Equal("6.6", page.Find("#cancel-route").GetAttribute("value"));
        page.Find("#cancel-minutes").Change("25");
        page.Find("#cancel-route").Change("3.1");
        page.Find("#confirm-cancel").Click();

        var stored = Trips.Get(trip);
        Assert.Equal(CancelReason.CarProblems, stored.Cancel!.Cancellation.Reason);
        Assert.Equal(26.89m, stored.Cancel.Paid);
        Assert.Equal(3.1m, stored.Actuals!.ReturnMiles.Value);
    }

    [Fact]
    public void UI_Shift_AShoppedCancelWithNoTipAsksForIt()
    {
        var trip = Trip(drops: 1, promised: null);
        var page = RenderComponent<Home>();

        page.Find("#cancel-0").Click();
        page.Find("#cancel-by").Change("Customer");
        page.Find("#cancel-stage").Change("AtTheDoor");
        page.Find("#cancel-minutes").Change("30");
        page.Find("#cancel-tip").Change("8");
        page.Find("#confirm-cancel").Click();

        Assert.Equal(26.89m, Trips.Get(trip).Cancel!.Paid);
    }
}
