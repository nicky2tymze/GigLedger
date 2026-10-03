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
/// Slice 4, story 1 screens (SDD 0.7 section 6.9), written before the pages: declining on the
/// Offer page, accept starting a shift (FR-2b), and the declines table on Reports (FR-21a).
/// Each test renders a page against real services on an in-memory database.
/// </summary>
public sealed class DeclinePageTests : TestContext
{
    // A Monday, so the Reports page's default week starts on it.
    private static readonly DateTimeOffset T0 = new(2026, 8, 3, 5, 45, 0, TimeSpan.FromHours(-5));

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly LedgerServices _ledger;
    private readonly TestClock _clock = new(T0);
    private readonly HeldOffer _held = new();

    public DeclinePageTests()
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

    private Guid OpenShift() => Shifts.Start("Spark", T0, new(17_000m, Grade.Measured));

    private string NavigatedTo => Services.GetRequiredService<FakeNavigationManager>().Uri;

    private static void EnterRun(IRenderedComponent<OfferPage> page)
    {
        page.Find("#pay").Change("32.89");
        page.Find("#stated-miles").Change("4.1");
        page.Find("#drops").Change("2");
        page.Find("#items").Change("56");
        page.Find("#est-minutes").Change("64");
    }

    /// <summary>The offer the Offer page builds from EnterRun, at the clock's time.</summary>
    private static Offer TheRun(DateTimeOffset offeredAt) => new(
        new(32.89m, Grade.Stated), new(4.1m, Grade.Stated), 2, 56, new(64, Grade.Stated), offeredAt);

    private IRenderedComponent<OfferPage> EvaluatedOffer()
    {
        var page = RenderComponent<OfferPage>();
        EnterRun(page);
        page.Find("#evaluate").Click();
        return page;
    }

    // ---- Declining (FR-2a) ----

    [Fact]
    public void UI_Offer_DeclineListsEveryReasonInOrderWithItsLabel()
    {
        OpenShift();
        var page = EvaluatedOffer();

        page.Find("#decline").Click();

        var labels = page.FindAll("#decline-reasons label").Select(l => l.TextContent.Trim()).ToList();
        Assert.Equal(Enum.GetValues<DeclineReason>().Select(DeclineRules.Label), labels);
    }

    [Fact]
    public void UI_Offer_ConfirmingADeclineStoresTheReasonsAndTheNote()
    {
        var shift = OpenShift();
        var page = EvaluatedOffer();
        page.Find("#decline").Click();

        page.Find("#reason-Stairs").Change(true);
        page.Find("#reason-PayTooLow").Change(true);
        page.Find("#decline-note").Change("third floor walk-up");
        page.Find("#confirm-decline").Click();

        var stored = Assert.Single(Offers.DeclinesOnShift(shift));
        Assert.Equal(TheRun(T0), stored.Offer);
        Assert.Equal([DeclineReason.PayTooLow, DeclineReason.Stairs], stored.Reasons);
        Assert.Equal("third floor walk-up", stored.Note);
        Assert.Equal("http://localhost/", NavigatedTo);
    }

    [Fact]
    public void UI_Offer_OtherWithoutANoteShowsTheRefusalAndStoresNothing()
    {
        var shift = OpenShift();
        var page = EvaluatedOffer();
        page.Find("#decline").Click();

        page.Find("#reason-Other").Change(true);
        page.Find("#confirm-decline").Click();

        Assert.Contains("note", page.Find(".error").TextContent);
        Assert.Empty(Offers.DeclinesOnShift(shift));
    }

    [Fact]
    public void UI_Offer_WithNoShiftOpenThereIsNoDecline()
    {
        var page = EvaluatedOffer();
        Assert.Empty(page.FindAll("#decline"));
    }

    // ---- Accept starts a shift (FR-2b) ----

    [Fact]
    public void UI_Offer_WithNoShiftOpenAcceptSaysItStartsAShift()
    {
        var page = EvaluatedOffer();
        Assert.Equal("Accept and start a shift", page.Find("#accept").TextContent.Trim());
    }

    [Fact]
    public void UI_Offer_AcceptWithNoShiftHoldsTheOfferAndGoesToTheShiftPage()
    {
        var page = EvaluatedOffer();
        _clock.Now = T0.AddMinutes(1);

        page.Find("#accept").Click();

        Assert.Equal(TheRun(T0), _held.Offer);
        Assert.Equal(T0.AddMinutes(1), _held.AcceptPressedAt);
        Assert.Equal("http://localhost/", NavigatedTo);
        Assert.Null(Shifts.Open());
    }

    [Fact]
    public void UI_Shift_AHeldOfferIsShownOnTheShiftPage()
    {
        _held.Hold(TheRun(T0), T0.AddMinutes(1));

        var page = RenderComponent<Home>();

        Assert.Contains(Display.Money(32.89m), page.Find("#held-offer").TextContent);
    }

    [Fact]
    public void UI_Shift_StartingTheShiftCompletesTheHeldAccept()
    {
        _held.Hold(TheRun(T0), T0.AddMinutes(1));
        _clock.Now = T0.AddMinutes(3);
        var page = RenderComponent<Home>();

        page.Find("#start-odometer").Change("17000");
        page.Find("#start-shift").Click();

        var shift = Shifts.Open()!;
        Assert.Equal(T0.AddMinutes(1), shift.StartedAt);
        var trip = Assert.Single(Trips.OnShift(shift.Id));
        Assert.Equal(TheRun(T0), trip.Offer);
        Assert.Equal(T0.AddMinutes(1), trip.AcceptedAt);
        Assert.Null(_held.Offer);
    }

    [Fact]
    public void UI_Shift_WithNothingHeldStartingAShiftAcceptsNothing()
    {
        var page = RenderComponent<Home>();

        page.Find("#start-odometer").Change("17000");
        page.Find("#start-shift").Click();

        Assert.Empty(Trips.OnShift(Shifts.Open()!.Id));
        Assert.Empty(page.FindAll("#held-offer"));
    }

    [Fact]
    public void FR2b_HoldThenClearLeavesNothingHeld()
    {
        _held.Hold(TheRun(T0), T0.AddMinutes(1));
        _held.Clear();
        Assert.Null(_held.Offer);
    }

    // ---- The declines table (FR-21a) ----

    [Fact]
    public void UI_Reports_ShowsTheWeeksDeclinesByReason()
    {
        var shift = OpenShift();
        Offers.Decline(shift, TheRun(T0), [DeclineReason.Heavy, DeclineReason.Stairs], null, T0.AddMinutes(5));
        var expected = Offers.ReportDeclines(new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 9));

        var page = RenderComponent<ReportsPage>();

        Assert.Equal(expected.Declines.ToString(), page.Find("#declines-total").TextContent.Trim());
        Assert.Equal("1", page.Find("#declines-Heavy").TextContent.Trim());
        Assert.Equal("0", page.Find("#declines-Alcohol").TextContent.Trim());
        Assert.Equal(expected.RuleSaidClears.ToString(), page.Find("#declines-clears").TextContent.Trim());
        Assert.Equal(expected.RuleSaidDoesNotClear.ToString(), page.Find("#declines-short").TextContent.Trim());
    }
}
