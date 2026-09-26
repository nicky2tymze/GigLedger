using Bunit;
using GigLedger.Core;
using GigLedger.Data;
using GigLedger.Web;
using GigLedger.Web.Components.Pages;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace GigLedger.Tests;

/// <summary>
/// Slice 2 screens, written before the pages. Numbers on screen are compared to what the
/// services computed, passed through Display, never to retyped constants.
/// </summary>
public sealed class EnergyPageTests : TestContext
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 9, 0, 0, TimeSpan.FromHours(-5));

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly LedgerServices _ledger;
    private readonly TestClock _clock = new(Now);

    public EnergyPageTests()
    {
        _connection.Open();
        _ledger = new LedgerServices(LedgerDatabase.Open(_connection), _clock);
        Services.AddSingleton<TimeProvider>(_clock);
        Services.AddSingleton<ISettingsService>(_ledger);
        Services.AddSingleton<IOfferService>(_ledger);
        Services.AddSingleton<ITripService>(_ledger);
        Services.AddSingleton<IShiftService>(_ledger);
        Services.AddSingleton<IChargeService>(_ledger);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }

    private IChargeService Charges => _ledger;
    private ITripService Trips => _ledger;
    private IShiftService Shifts => _ledger;
    private ISettingsService Settings => _ledger;

    private static ChargeSession Fast(decimal odometer, decimal kwh, decimal cost, int daysAgo) => new(
        Now.AddDays(-daysAgo), new(odometer, Grade.Measured), new(kwh, Grade.Measured), new(cost, Grade.Measured),
        20, 80, "Blink", ChargeType.DcFast, Purpose.Work);

    private static void Enter(IRenderedComponent<Charges> page, string kwh, string cost, string type = "DcFast")
    {
        page.Find("#odometer").Change("17935");
        page.Find("#kwh").Change(kwh);
        page.Find("#cost").Change(cost);
        page.Find("#start-soc").Change("36");
        page.Find("#end-soc").Change("71");
        page.Find("#type").Change(type);
    }

    // ---- Charges page ----

    [Fact]
    public void UI_Charges_RecordsAFastSession()
    {
        var page = RenderComponent<Charges>();
        Enter(page, kwh: "24.3", cost: "16.77");
        page.Find("#record-charge").Click();

        var stored = Assert.Single(Charges.Between(Now.AddDays(-1), Now.AddDays(1))).Session;
        Assert.Equal(new Graded<decimal>(17_935m, Grade.Measured), stored.Odometer);
        Assert.Equal(new Graded<decimal>(16.77m, Grade.Measured), stored.Cost);
        Assert.Equal((36, 71, ChargeType.DcFast), (stored.StartSoc, stored.EndSoc, stored.Type));
        Assert.Equal(Display.PricePerKwh(16.77m / 24.3m), page.Find("#price-fast").TextContent.Trim());
    }

    [Fact]
    public void UI_Charges_AHomeSessionNeedsNoCostAndNamesThePlaceholder()
    {
        var page = RenderComponent<Charges>();
        Enter(page, kwh: "20", cost: "", type: "Home");
        page.Find("#record-charge").Click();

        var stored = Assert.Single(Charges.Between(Now.AddDays(-1), Now.AddDays(1)));
        Assert.Null(stored.Session.Cost);
        Assert.Equal(3.00m, stored.Cost.Value);
        Assert.Contains("home rate", page.Find("#assumptions").TextContent);
    }

    [Fact]
    public void UI_Charges_ShowsMeasuredEfficiencyOnceItCanBeMeasured()
    {
        Charges.Record(Fast(1000m, 40m, 27.60m, daysAgo: 6));
        Charges.Record(Fast(1150m, 35m, 24.15m, daysAgo: 4));
        Charges.Record(Fast(1300m, 50m, 34.50m, daysAgo: 2));
        var page = RenderComponent<Charges>();
        Assert.Equal(Display.Efficiency(4.0m), page.Find("#efficiency").TextContent.Trim());
        Assert.Equal(Display.PricePerKwh(0.69m), page.Find("#price-blend").TextContent.Trim());
        Assert.Equal(3, page.FindAll("#sessions tr.session").Count);
    }

    [Fact]
    public void UI_Charges_BlankOdometerAndChargeAreRecordedAsUnknown()
    {
        var page = RenderComponent<Charges>();
        page.Find("#kwh").Change("24.288");
        page.Find("#cost").Change("16.76");
        page.Find("#record-charge").Click();

        var stored = Assert.Single(Charges.Between(Now.AddDays(-1), Now.AddDays(1))).Session;
        Assert.Null(stored.Odometer);
        Assert.Null(stored.StartSoc);
        Assert.Null(stored.EndSoc);
        var row = page.Find("#sessions tr.session").TextContent;
        Assert.Contains("odometer unknown", row);
        Assert.Contains("charge unknown", row);
    }

    [Fact]
    public void UI_Charges_OneSessionSaysWhyThereIsNoEfficiency()
    {
        Charges.Record(Fast(1000m, 40m, 27.60m, daysAgo: 6));
        var page = RenderComponent<Charges>();
        Assert.Contains("two sessions", page.Find("#efficiency").TextContent);
    }

    [Fact]
    public void UI_Charges_AnImpossibleSessionShowsTheReason()
    {
        var page = RenderComponent<Charges>();
        Enter(page, kwh: "0", cost: "0");
        page.Find("#record-charge").Click();
        Assert.NotEmpty(page.Find(".error").TextContent.Trim());
        Assert.Empty(Charges.Between(Now.AddDays(-1), Now.AddDays(1)));
    }

    [Fact]
    public void UI_Charges_SetsTheHomeRate()
    {
        var page = RenderComponent<Charges>();
        Assert.Contains("placeholder", page.Find("#current-home-rate").TextContent);

        page.Find("#home-rate").Change("0.13");
        page.Find("#home-rate-from").Change("2026-09-01");
        page.Find("#set-home-rate").Click();

        var rate = Settings.HomeRateOn(new DateOnly(2026, 9, 25));
        Assert.Equal(0.13m, rate.PerKwh);
        Assert.False(rate.IsPlaceholder);
        Assert.DoesNotContain("placeholder", page.Find("#current-home-rate").TextContent);
    }

    // ---- Shift page: tips and per-trip rates ----

    private Guid FinishedTrip()
    {
        var shift = Shifts.Start("Spark", Now, new(17_000m, Grade.Measured));
        var trip = _ledger.Accept(shift, new Offer(new(34.89m, Grade.Stated), new(6.6m, Grade.Stated), 2, 32, new(58, Grade.Stated), Now), Now);
        Trips.RecordActuals(trip, new(new(55, Grade.Entered), new(6.4m, Grade.Entered), new(5.7m, Grade.Entered)));
        return trip;
    }

    [Fact]
    public void UI_Shift_RecordsATip()
    {
        var trip = FinishedTrip();
        var page = RenderComponent<Home>();
        page.Find("#tip-0").Change("6.00");
        page.Find("#save-tip-0").Click();
        Assert.Equal(6.00m, Trips.Get(trip).Tip!.Value.Value);
    }

    [Fact]
    public void UI_Shift_ShowsTheNetRateOfAFinishedTrip()
    {
        var trip = FinishedTrip();
        var page = RenderComponent<Home>();
        var report = Trips.Report(trip);
        Assert.Equal(Display.Rate(report.Rates!.NetPerHour.Value), page.Find("#net-0").TextContent.Trim());
    }

    // ---- Summary: tips ----

    [Fact]
    public void UI_Summary_ShowsTips()
    {
        var trip = FinishedTrip();
        Trips.RecordTip(trip, 6.00m, Now.AddHours(12));
        var shift = Trips.Get(trip).ShiftId;
        Shifts.End(shift, Now.AddMinutes(90), new(17_012.1m, Grade.Measured));

        var page = RenderComponent<Summary>(p => p.Add(s => s.Id, shift));
        Assert.Equal(Display.Money(6.00m), page.Find("#tips").TextContent.Trim());
    }
}
