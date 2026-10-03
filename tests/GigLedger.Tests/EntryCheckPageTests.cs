using Bunit;
using Bunit.TestDoubles;
using GigLedger.Core;
using GigLedger.Data;
using GigLedger.Web;
using GigLedger.Web.Components.Pages;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace GigLedger.Tests;

/// <summary>
/// Slice 4, story 2 screens (SDD 0.8 section 6.10), written before the pages: the confirm step on
/// the Offer and Shift pages, and the explained-values table on Reports (FR-38).
/// </summary>
public sealed class EntryCheckPageTests : TestContext
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 3, 5, 45, 0, TimeSpan.FromHours(-5));

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly LedgerServices _ledger;
    private readonly TestClock _clock = new(T0);
    private readonly HeldOffer _held = new();

    public EntryCheckPageTests()
    {
        _connection.Open();
        var db = LedgerDatabase.Open(_connection);
        _ledger = new LedgerServices(db, _clock);
        Services.AddSingleton<TimeProvider>(_clock);
        Services.AddSingleton<ISettingsService>(_ledger);
        Services.AddSingleton<IOfferService>(_ledger);
        Services.AddSingleton<ITripService>(_ledger);
        Services.AddSingleton<IShiftService>(_ledger);
        Services.AddSingleton<IReportService>(_ledger);
        Services.AddSingleton<IEntryCheckService>(_ledger);
        Services.AddSingleton(_held);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }

    private IShiftService Shifts => _ledger;
    private IOfferService Offers => _ledger;
    private ITripService Trips => _ledger;
    private IEntryCheckService Checks => _ledger;

    private Guid OpenShift() => Shifts.Start("Spark", T0, new(17_000m, Grade.Measured));

    private IRenderedComponent<OfferPage> EvaluatedOffer(string pay)
    {
        var page = RenderComponent<OfferPage>();
        page.Find("#pay").Change(pay);
        page.Find("#stated-miles").Change("4.1");
        page.Find("#items").Change("10");
        page.Find("#est-minutes").Change("30");
        page.Find("#evaluate").Click();
        return page;
    }

    // ---- Offer ----

    [Fact]
    public void UI_Offer_AcceptPastThePayLimitShowsTheConfirmStepFirst()
    {
        var shift = OpenShift();
        var page = EvaluatedOffer("95");

        page.Find("#accept").Click();

        Assert.Contains("Pay $95.00 is above $80.00", page.Find("#ack-panel").TextContent);
        Assert.Empty(Trips.OnShift(shift));

        page.Find("#ack-confirm").Click();

        var trip = Assert.Single(Trips.OnShift(shift));
        Assert.Equal(MarkLevel.Confirmed, Assert.Single(Checks.MarksOn(trip.Id)).Level);
    }

    [Fact]
    public void UI_Offer_PastTheDocumentLevelTheStepAsksForAnExplanation()
    {
        var shift = OpenShift();
        var page = EvaluatedOffer("300");
        page.Find("#accept").Click();

        Assert.Empty(page.FindAll("#ack-confirm"));
        page.Find("#ack-record").Click();
        Assert.Empty(Trips.OnShift(shift));

        page.Find("#ack-explanation").Change("holiday surge pay");
        page.Find("#ack-record").Click();

        var mark = Assert.Single(Checks.MarksOn(Assert.Single(Trips.OnShift(shift)).Id));
        Assert.Equal((MarkLevel.Explained, "holiday surge pay"), (mark.Level, mark.Explanation));
    }

    [Fact]
    public void UI_Offer_DeclinePastThePayLimitShowsTheConfirmStepToo()
    {
        var shift = OpenShift();
        var page = EvaluatedOffer("95");
        page.Find("#decline").Click();
        page.Find("#reason-Heavy").Change(true);

        page.Find("#confirm-decline").Click();
        Assert.Empty(Offers.DeclinesOnShift(shift));

        page.Find("#ack-confirm").Click();
        Assert.Single(Offers.DeclinesOnShift(shift));
    }

    [Fact]
    public void UI_Offer_TheHeldOfferCarriesItsConfirmToTheShift()
    {
        var page = EvaluatedOffer("95");
        page.Find("#accept").Click();
        page.Find("#ack-confirm").Click();
        Assert.Equal(new Acknowledgement(true), _held.Acknowledgement);

        var home = RenderComponent<Home>();
        home.Find("#start-odometer").Change("17000");
        home.Find("#start-shift").Click();

        var trip = Assert.Single(Trips.OnShift(Shifts.Open()!.Id));
        Assert.Equal(MarkLevel.Confirmed, Assert.Single(Checks.MarksOn(trip.Id)).Level);
    }

    // ---- Shift ----

    [Fact]
    public void UI_Shift_ActualsPastALimitShowTheConfirmStep()
    {
        var shift = OpenShift();
        var trip = Offers.Accept(shift, new Offer(new(30m, Grade.Stated), new(20m, Grade.Stated), 1, 5, new(60, Grade.Stated), T0), T0);
        var page = RenderComponent<Home>();

        page.Find("#minutes-0").Change("60");
        page.Find("#route-0").Change("40");
        page.Find("#return-0").Change("20");
        page.Find("#save-actuals-0").Click();

        Assert.Contains("is above 50 mi", page.Find("#ack-panel").TextContent);
        Assert.Null(Trips.Get(trip).Actuals);

        page.Find("#ack-confirm").Click();
        Assert.NotNull(Trips.Get(trip).Actuals);
    }

    [Fact]
    public void UI_Shift_EndingPastTwelveHoursShowsTheConfirmStep()
    {
        var shift = OpenShift();
        _clock.Now = T0.AddHours(13);
        var page = RenderComponent<Home>();

        page.Find("#end-odometer").Change("17100");
        page.Find("#end-shift").Click();

        Assert.Contains("Shift length", page.Find("#ack-panel").TextContent);
        Assert.Null(Shifts.Get(shift).EndedAt);

        page.Find("#ack-confirm").Click();
        Assert.NotNull(Shifts.Get(shift).EndedAt);
    }

    // ---- Reports (FR-38) ----

    [Fact]
    public void UI_Reports_ListsTheWeeksExplainedValues()
    {
        Offers.Accept(OpenShift(), new Offer(new(300m, Grade.Stated), new(4.1m, Grade.Stated), 1, 5, new(30, Grade.Stated), T0), T0,
            new Acknowledgement(true, "holiday surge pay"));

        var page = RenderComponent<ReportsPage>();

        var row = page.Find("#explained-values tr.explained");
        Assert.Contains("holiday surge pay", row.TextContent);
        Assert.Contains("Pay", row.TextContent);
    }
}
