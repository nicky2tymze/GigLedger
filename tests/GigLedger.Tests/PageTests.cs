using Bunit;
using Bunit.TestDoubles;
using GigLedger.Core;
using GigLedger.Data;
using GigLedger.Web;
using GigLedger.Web.Components.Pages;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace GigLedger.Tests;

/// <summary>A clock the test can move.</summary>
public sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("Test", Now.Offset, "Test", "Test");
}

/// <summary>
/// SDD 7.3 screens, written before the pages. Each test renders a page against real services
/// on an in-memory database. Numbers on screen are compared to Core's results passed through
/// Display, never retyped, so the screen is held to showing what Core computed.
/// </summary>
public sealed class PageTests : TestContext
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 3, 5, 45, 0, TimeSpan.FromHours(-5));
    private static readonly EnergyBasis Defaults = EnergyBasis.FromDefaults(4.0m, 0.69m);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly LedgerServices _ledger;
    private readonly TestClock _clock = new(T0);

    public PageTests()
    {
        _connection.Open();
        var db = LedgerDatabase.Open(_connection);
        _ledger = new LedgerServices(db, _clock);
        Services.AddSingleton<TimeProvider>(_clock);
        Services.AddSingleton<ISettingsService>(_ledger);
        Services.AddSingleton<IOfferService>(_ledger);
        Services.AddSingleton<ITripService>(_ledger);
        Services.AddSingleton<IShiftService>(_ledger);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }

    private IShiftService Shifts => _ledger;
    private ITripService Trips => _ledger;

    private Guid OpenShift() => Shifts.Start("Spark", T0, new(17_000m, Grade.Measured));

    private static void EnterRun11(IRenderedComponent<OfferPage> page, string pay = "32.89", string minutes = "64")
    {
        page.Find("#pay").Change(pay);
        page.Find("#stated-miles").Change("4.1");
        page.Find("#drops").Change("2");
        page.Find("#items").Change("56");
        page.Find("#est-minutes").Change(minutes);
    }

    // ---- Offer: at the accept screen (Overview 5.1) ----

    [Fact]
    public void UI_Offer_ShowsTheForecastTheVerdictAndTheAssumptions()
    {
        var page = RenderComponent<OfferPage>();
        EnterRun11(page);
        page.Find("#evaluate").Click();

        var expected = Calculations.ForecastNetPerHour(
            new Core.Offer(new(32.89m, Grade.Stated), new(4.1m, Grade.Stated), 2, 56, new(64, Grade.Stated), T0),
            Defaults);
        Assert.Equal(Display.Rate(expected.Value), page.Find("#forecast").TextContent.Trim());
        Assert.Contains("Clears", page.Find("#verdict").TextContent);
        Assert.Equal(3, page.FindAll("#assumptions li").Count);
        Assert.Empty(Trips.OnShift(OpenShift())); // evaluating stored nothing
    }

    [Fact]
    public void UI_Offer_BelowTheThresholdDoesNotClear()
    {
        var page = RenderComponent<OfferPage>();
        EnterRun11(page, pay: "10.00");
        page.Find("#evaluate").Click();
        Assert.Contains("Does not clear", page.Find("#verdict").TextContent);
    }

    [Fact]
    public void UI_Offer_WithoutAnOpenShiftThereIsNothingToAcceptInto()
    {
        var page = RenderComponent<OfferPage>();
        EnterRun11(page);
        page.Find("#evaluate").Click();
        Assert.Empty(page.FindAll("#accept"));
        Assert.NotEmpty(page.FindAll("#no-shift"));
    }

    [Fact]
    public void UI_Offer_AcceptStoresTheTripAndReturnsToTheShift()
    {
        var shift = OpenShift();
        var page = RenderComponent<OfferPage>();
        EnterRun11(page);
        page.Find("#evaluate").Click();
        page.Find("#accept").Click();

        var trip = Assert.Single(Trips.OnShift(shift));
        Assert.Equal(new Graded<decimal>(32.89m, Grade.Stated), trip.Offer.Pay);
        Assert.Equal("http://localhost/", Services.GetRequiredService<FakeNavigationManager>().Uri);
    }

    [Fact]
    public void UI_Offer_ABadEntryShowsTheReasonInsteadOfFailing()
    {
        var page = RenderComponent<OfferPage>();
        EnterRun11(page, minutes: "0");
        page.Find("#evaluate").Click();
        Assert.NotEmpty(page.Find(".error").TextContent.Trim());
        Assert.Empty(page.FindAll("#forecast"));
    }

    // ---- Shift: start, trips, end (Overview 5.2, 5.3) ----

    [Fact]
    public void UI_Shift_StartsAShiftFromTheOdometer()
    {
        var page = RenderComponent<Home>();
        page.Find("#start-odometer").Change("17000");
        page.Find("#start-shift").Click();

        var open = Shifts.Open();
        Assert.NotNull(open);
        Assert.Equal(new Graded<decimal>(17_000m, Grade.Measured), open.StartOdometer);
        Assert.Equal(T0, open.StartedAt);
    }

    [Fact]
    public void UI_Shift_RecordsTheActualsOfATrip()
    {
        var shift = OpenShift();
        var trip = _ledger.Accept(shift, new Core.Offer(new(34.89m, Grade.Stated), new(6.6m, Grade.Stated), 2, 32, new(58, Grade.Stated), T0), T0);

        var page = RenderComponent<Home>();
        page.Find("#minutes-0").Change("55");
        page.Find("#route-0").Change("6.4");
        page.Find("#return-0").Change("5.7");
        page.Find("#save-actuals-0").Click();

        Assert.Equal(new TripActuals(55, 6.4m, 5.7m), Trips.Get(trip).Actuals!.Values);
    }

    [Fact]
    public void UI_Shift_EndsAndLinksToItsSummary()
    {
        var shift = OpenShift();
        _clock.Now = T0.AddMinutes(90);

        var page = RenderComponent<Home>();
        page.Find("#end-odometer").Change("17012.1");
        page.Find("#end-shift").Click();

        Assert.Null(Shifts.Open());
        Assert.Equal(T0.AddMinutes(90), Shifts.Get(shift).EndedAt);
        Assert.Equal($"/shifts/{shift}/summary", page.Find("#summary").GetAttribute("href"));
    }

    [Fact]
    public void UI_Shift_ABackwardsOdometerShowsTheReason()
    {
        OpenShift();
        _clock.Now = T0.AddMinutes(90);
        var page = RenderComponent<Home>();
        page.Find("#end-odometer").Change("16999");
        page.Find("#end-shift").Click();

        Assert.Contains("below", page.Find(".error").TextContent);
        Assert.NotNull(Shifts.Open());
    }

    // ---- Summary (Overview 5.3) ----

    private Guid ClosedShiftWithRun4()
    {
        var shift = OpenShift();
        var trip = _ledger.Accept(shift, new Core.Offer(new(34.89m, Grade.Stated), new(6.6m, Grade.Stated), 2, 32, new(58, Grade.Stated), T0), T0);
        Trips.RecordActuals(trip, new(new(55, Grade.Entered), new(6.4m, Grade.Entered), new(5.7m, Grade.Entered)));
        Shifts.End(shift, T0.AddMinutes(90), new(17_012.1m, Grade.Measured));
        return shift;
    }

    [Fact]
    public void UI_Summary_ShowsCoresNumbers()
    {
        var shift = ClosedShiftWithRun4();
        var core = Shifts.Summary(shift);

        var page = RenderComponent<Summary>(p => p.Add(s => s.Id, shift));
        Assert.Equal(Display.Money(core.Gross), page.Find("#gross").TextContent.Trim());
        Assert.Equal(Display.Money(core.Net.Value), page.Find("#net").TextContent.Trim());
        Assert.Equal(Display.Rate(core.ShiftRate.Value), page.Find("#shift-rate").TextContent.Trim());
        Assert.Equal(Display.Rate(core.TripRate!.Value), page.Find("#trip-rate").TextContent.Trim());
        Assert.Equal(Display.Rate(core.RateGap!.Value), page.Find("#gap").TextContent.Trim());
        Assert.Equal(Display.Miles(core.DeadheadMiles), page.Find("#deadhead-miles").TextContent.Trim());
    }

    [Fact]
    public void NFR2_TheSummaryShowsItsAssumptions()
    {
        var page = RenderComponent<Summary>(p => p.Add(s => s.Id, ClosedShiftWithRun4()));
        var shown = page.Find("#assumptions").TextContent;
        Assert.Contains("efficiency", shown);
        Assert.Contains("energy price", shown);
    }

    [Fact]
    public void UI_Summary_AShiftWithNoTripsSaysSo()
    {
        var shift = OpenShift();
        Shifts.End(shift, T0.AddMinutes(60), new(17_010m, Grade.Measured));
        var page = RenderComponent<Summary>(p => p.Add(s => s.Id, shift));
        Assert.Equal("no trips", page.Find("#trip-rate").TextContent.Trim());
        Assert.Equal("no trips", page.Find("#gap").TextContent.Trim());
    }

    [Fact]
    public void UI_Summary_OfAnOpenShiftSaysWhy()
    {
        var page = RenderComponent<Summary>(p => p.Add(s => s.Id, OpenShift()));
        Assert.Contains("still open", page.Find(".error").TextContent);
    }
}
