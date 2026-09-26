using GigLedger.Core;
using GigLedger.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GigLedger.Tests;

/// <summary>
/// Slice 2 storage and services, written from SRS 0.3 and SDD 6.5 before the code. Each test has
/// its own in-memory database and a clock it controls, because the 30-day window (FR-9a) is time.
/// </summary>
public sealed class EnergyStorageTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 9, 0, 0, TimeSpan.FromHours(-5));

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly LedgerContext _db;
    private readonly LedgerServices _ledger;
    private readonly TestClock _clock = new(Now);

    public EnergyStorageTests()
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

    private IChargeService Charges => _ledger;
    private ITripService Trips => _ledger;
    private IShiftService Shifts => _ledger;
    private ISettingsService Settings => _ledger;

    private static ChargeSession Fast(decimal odometer, decimal kwh, decimal cost, int daysAgo, int startSoc = 20) => new(
        Now.AddDays(-daysAgo), new(odometer, Grade.Measured), new(kwh, Grade.Measured), new(cost, Grade.Measured),
        startSoc, 80, "Blink", ChargeType.DcFast, Purpose.Work);

    private static ChargeSession Home(decimal odometer, decimal kwh, int daysAgo) => new(
        Now.AddDays(-daysAgo), new(odometer, Grade.Measured), new(kwh, Grade.Entered), null,
        20, 80, "Home", ChargeType.Home, Purpose.Personal);

    private static Offer Run4Offer(DateTimeOffset at) => new(
        new(34.89m, Grade.Stated), new(6.6m, Grade.Stated), 2, 32, new(58, Grade.Stated), at);

    private static readonly GradedActuals Run4Actuals =
        new(new(55, Grade.Entered), new(6.4m, Grade.Entered), new(5.7m, Grade.Entered));

    // ---- FR-5: sessions ----

    [Fact]
    public void FR5_ASessionComesBackWithEveryField()
    {
        var home = Home(17_935m, 22.5m, daysAgo: 1);
        Charges.Record(home);
        var stored = Assert.Single(Charges.Between(Now.AddDays(-2), Now));
        Assert.Equal(home, stored.Session);
    }

    [Fact]
    public void FR5_AnImpossibleSessionIsRefusedAtEntry()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Charges.Record(Fast(17_935m, 0m, 0m, daysAgo: 1)));
        Assert.Empty(Charges.Between(Now.AddDays(-2), Now));
    }

    [Fact]
    public void FR5_TheHomeRateStartsAtThePlaceholder()
    {
        Assert.Equal(HomeRate.Placeholder, Settings.HomeRateOn(new DateOnly(2026, 9, 25)));
    }

    [Fact]
    public void FR5_ARealRateAppliesFromItsDateAndThePlaceholderBefore()
    {
        Settings.SetHomeRate(0.13m, new DateOnly(2026, 9, 1));
        Assert.True(Settings.HomeRateOn(new DateOnly(2026, 8, 31)).IsPlaceholder);
        var real = Settings.HomeRateOn(new DateOnly(2026, 9, 1));
        Assert.Equal(0.13m, real.PerKwh);
        Assert.False(real.IsPlaceholder);
    }

    [Fact]
    public void FR5_AHomeSessionIsCostedAtTheRateOnItsDate()
    {
        Settings.SetHomeRate(0.13m, new DateOnly(2026, 9, 20));
        Charges.Record(Home(1000m, 20m, daysAgo: 10)); // 09/15: placeholder
        Charges.Record(Home(1100m, 20m, daysAgo: 1));  // 09/24: real
        var costs = Charges.Between(Now.AddDays(-30), Now).OrderBy(c => c.Session.At).Select(c => c.Cost.Value);
        Assert.Equal([3.00m, 2.60m], costs);
    }

    // ---- FR-8, FR-9: the report over a range ----

    [Fact]
    public void FR9_TheReportMeasuresTheRange()
    {
        Charges.Record(Fast(1000m, 40m, 27.60m, daysAgo: 6));
        Charges.Record(Fast(1150m, 35m, 24.15m, daysAgo: 4));
        Charges.Record(Fast(1300m, 50m, 34.50m, daysAgo: 2));
        var report = Charges.Report(Now.AddDays(-30), Now);
        Assert.Equal(3, report.Sessions);
        Assert.Equal(4.0m, report.Efficiency!.Value);
        Assert.Equal(0.69m, report.Prices.Blend!.Value);
    }

    // ---- FR-9a: the 30 days before the shift ----

    [Fact]
    public void FR9a_TheSummaryUsesOnlyTheThirtyDaysBeforeTheShift()
    {
        // 40 days ago, at $0.49: outside the window and must not count.
        Charges.Record(Fast(500m, 60m, 29.40m, daysAgo: 40));
        // Inside the window: 300 miles on 75 kWh = 4.0 mi/kWh, all at $0.69.
        Charges.Record(Fast(1000m, 40m, 27.60m, daysAgo: 6));
        Charges.Record(Fast(1150m, 35m, 24.15m, daysAgo: 4));
        Charges.Record(Fast(1300m, 50m, 34.50m, daysAgo: 2));

        var shift = Shifts.Start("Spark", Now, new(1300m, Grade.Measured));
        Shifts.End(shift, Now.AddMinutes(60), new(1340m, Grade.Measured));

        var summary = Shifts.Summary(shift);
        Assert.Equal(6.90m, summary.EnergyCost.Value); // 40 mi x (0.69 / 4.0)
        Assert.Empty(summary.EnergyCost.Assumptions);  // measured, not defaulted
    }

    [Fact]
    public void FR9a_WithNoChargingTheSummaryStaysOnNamedDefaults()
    {
        var shift = Shifts.Start("Spark", Now, new(1300m, Grade.Measured));
        Shifts.End(shift, Now.AddMinutes(60), new(1340m, Grade.Measured));
        var summary = Shifts.Summary(shift);
        Assert.Contains(summary.EnergyCost.Assumptions, a => a.Input == "efficiency");
        Assert.Contains(summary.EnergyCost.Assumptions, a => a.Input == "energy price");
    }

    [Fact]
    public void FR9a_EvaluatingAnOfferUsesTheLastThirtyDays()
    {
        Charges.Record(Fast(1000m, 40m, 19.60m, daysAgo: 6)); // $0.49
        Charges.Record(Fast(1150m, 35m, 17.15m, daysAgo: 4));
        Charges.Record(Fast(1300m, 50m, 24.50m, daysAgo: 2));
        var offer = Run4Offer(Now);

        var expected = Calculations.ForecastNetPerHour(offer, new EnergyBasis(4.0m, 0.49m, []));
        var evaluation = _ledger.Evaluate(offer);
        Assert.Equal(expected.Value, evaluation.Forecast.Value);
        Assert.DoesNotContain(evaluation.Forecast.Assumptions, a => a.Input == "efficiency");
    }

    // ---- FR-6, FR-17: tips ----

    private Guid FinishedTrip()
    {
        var shift = Shifts.Start("Spark", Now, new(17_000m, Grade.Measured));
        var trip = _ledger.Accept(shift, Run4Offer(Now), Now);
        Trips.RecordActuals(trip, Run4Actuals);
        return trip;
    }

    [Fact]
    public void FR6_ATipIsRecordedOncePerTrip()
    {
        var trip = FinishedTrip();
        Trips.RecordTip(trip, 6.00m, Now.AddHours(12));
        Assert.Equal(new Graded<decimal>(6.00m, Grade.Stated), Trips.Get(trip).Tip);
        Assert.Throws<InvalidOperationException>(() => Trips.RecordTip(trip, 1.00m, Now.AddHours(13)));
    }

    [Fact]
    public void FR6_ATipOnAnUnknownTripIsNotFound()
    {
        Assert.Throws<NotFoundException>(() => Trips.RecordTip(Guid.NewGuid(), 6.00m, Now));
    }

    [Fact]
    public void FR6_ANegativeTipIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Trips.RecordTip(FinishedTrip(), -1m, Now));
    }

    [Fact]
    public void FR17_TheTripReportShowsRatesBeforeAndAfterTheTip()
    {
        var trip = FinishedTrip();
        Trips.RecordTip(trip, 6.00m, Now.AddHours(12));
        var report = Trips.Report(trip);

        var expected = EnergyCalculations.TripRates(34.89m, 6.00m, Run4Actuals.Values, report.Energy);
        Assert.Equal(expected.GrossPerHourBeforeTip.Value, report.Rates!.GrossPerHourBeforeTip.Value);
        Assert.Equal(expected.GrossPerHour.Value, report.Rates.GrossPerHour.Value);
        Assert.Equal(expected.NetPerHour.Value, report.Rates.NetPerHour.Value);
    }

    [Fact]
    public void FR16_TheTripReportHasTheEstimateError()
    {
        var report = Trips.Report(FinishedTrip());
        Assert.Equal(new EstimateError(-3, -0.2m), report.EstimateError);
    }

    [Fact]
    public void FR14_ATripWithoutActualsHasNoRatesYet()
    {
        var shift = Shifts.Start("Spark", Now, new(17_000m, Grade.Measured));
        var report = Trips.Report(_ledger.Accept(shift, Run4Offer(Now), Now));
        Assert.Null(report.Rates);
        Assert.Null(report.EstimateError);
    }

    [Fact]
    public void FR17_TheShiftSummaryIncludesTips()
    {
        var trip = FinishedTrip();
        Trips.RecordTip(trip, 6.00m, Now.AddHours(12));
        var shift = Trips.Get(trip).ShiftId;
        Shifts.End(shift, Now.AddMinutes(90), new(17_012.1m, Grade.Measured));
        var summary = Shifts.Summary(shift);
        Assert.Equal(40.89m, summary.Gross);
        Assert.Equal(6.00m, summary.Tips);
    }

    // ---- FR-25 on the new tables ----

    [Fact]
    public void FR25e_TheNewTablesRefuseBulkChangesToo()
    {
        // The first triggers named the tables that existed then. New tables need their own.
        Charges.Record(Fast(1000m, 40m, 27.60m, daysAgo: 1));
        Trips.RecordTip(FinishedTrip(), 6.00m, Now);
        Assert.Throws<SqliteException>(() => _db.ChargeSessions.ExecuteDelete());
        Assert.Throws<SqliteException>(() => _db.Tips.ExecuteUpdate(s => s.SetProperty(t => t.Amount, 0m)));
        Assert.Throws<SqliteException>(() => _db.HomeRates.ExecuteDelete());
    }
}
