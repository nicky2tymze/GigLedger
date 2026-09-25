using GigLedger.Core;

namespace GigLedger.Tests;

/// <summary>
/// Slice 1 calculation tests, written from SRS 0.2 and SDD 0.2 before the code.
/// Expected values are worked by hand from the SDD formulas, or taken from the
/// driver's hand log (Docs/Personal/Driving_Log.md in FluxPlatform), cited per test.
/// Comparisons round to the cent because the log does; the code itself never rounds.
/// </summary>
public class CalculationTests
{
    // Slice 1 defaults, confirmed 2026-09-25 (SDD section 11).
    private static readonly EnergyBasis Defaults = EnergyBasis.FromDefaults(4.0m, 0.69m);

    private static decimal Cents(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    // Run 11, Mon 08/03: $32.89, 4.1 stated miles, 2 drops, 56 items, app estimate 64 min.
    private static Offer Run11(decimal? returnOverride = null) => new(
        Pay: new(32.89m, Grade.Stated),
        StatedMiles: new(4.1m, Grade.Stated),
        Drops: 2,
        Items: 56,
        EstimatedMinutes: new(64, Grade.Stated),
        OfferedAt: new DateTimeOffset(2026, 8, 3, 5, 45, 0, TimeSpan.FromHours(-5)),
        ReturnMilesOverride: returnOverride);

    // Sun 08/02 runs from the hand log's run table.
    private static readonly TripActuals Run4 = new(ElapsedMinutes: 55, RouteMiles: 6.4m, ReturnMiles: 5.7m);
    private const decimal Run4Pay = 34.89m; // 28.89 fee + 6.00 incentive
    private static readonly TripActuals Run5 = new(27, 3.6m, 3.6m);
    private static readonly TripActuals Run6 = new(33, 4.0m, 4.0m);

    // ---- FR-7: grades travel with values ----

    [Fact]
    public void FR7_GradeTravelsWithTheValue()
    {
        var offer = Run11();
        Assert.Equal(Grade.Stated, offer.Pay.Grade);
        Assert.NotEqual(new Graded<decimal>(4.1m, Grade.Entered), new Graded<decimal>(4.1m, Grade.Measured));
    }

    // ---- FR-10: defaults are named ----

    [Fact]
    public void FR10_DefaultEnergyBasisNamesBothDefaults()
    {
        Assert.Equal(4.0m, Defaults.MilesPerKwh);
        Assert.Equal(0.69m, Defaults.PricePerKwh);
        Assert.Equal(2, Defaults.Assumptions.Count);
        Assert.Contains(Defaults.Assumptions, a => a.Input == "efficiency");
        Assert.Contains(Defaults.Assumptions, a => a.Input == "energy price");
    }

    [Fact]
    public void FR10_EnergyCostPerMileCarriesTheDefaults()
    {
        var cost = Calculations.EnergyCostPerMile(Defaults);
        Assert.Equal(0.1725m, cost.Value); // 0.69 / 4.0
        Assert.True(cost.RestsOnDefault);
        Assert.Equal(2, cost.Assumptions.Count);
    }

    [Fact]
    public void FR10_MeasuredBasisCarriesNoAssumptions()
    {
        var measured = new EnergyBasis(4.2m, 0.69m, []);
        Assert.False(Calculations.EnergyCostPerMile(measured).RestsOnDefault);
    }

    // ---- FR-11, FR-13: forecast ----

    [Fact]
    public void FR11_ForecastSubtractsEnergyOfStatedPlusReturn()
    {
        // (32.89 - 0.1725 x (4.1 + 4.1)) / (64/60) = 31.4755 / 1.0667 = 29.508
        var forecast = Calculations.ForecastNetPerHour(Run11(), Defaults);
        Assert.Equal(29.51m, Cents(forecast.Value));
    }

    [Fact]
    public void FR11_Acceptance_Run11_WithoutEnergyMatchesTheHandLog()
    {
        // Hand log, Run 11 rate ladder: "their estimate, to last drop | 64 | $30.83".
        // That figure is gross, so a zero energy price must reproduce it exactly.
        var noEnergy = new EnergyBasis(4.0m, 0m, []);
        var forecast = Calculations.ForecastNetPerHour(Run11(), noEnergy);
        Assert.Equal(30.83m, Cents(forecast.Value));
    }

    [Fact]
    public void FR13_ReturnDefaultsToStatedMilesAndSaysSo()
    {
        var forecast = Calculations.ForecastNetPerHour(Run11(), Defaults);
        Assert.Contains(forecast.Assumptions, a => a.Input == "return miles");
        Assert.Equal(3, forecast.Assumptions.Count); // efficiency, energy price, return miles
    }

    [Fact]
    public void FR13_DriverOverrideReplacesTheReturnEstimate()
    {
        // (32.89 - 0.1725 x (4.1 + 2.0)) / (64/60) = 31.83775 / 1.0667 = 29.848
        var forecast = Calculations.ForecastNetPerHour(Run11(returnOverride: 2.0m), Defaults);
        Assert.Equal(29.85m, Cents(forecast.Value));
        Assert.DoesNotContain(forecast.Assumptions, a => a.Input == "return miles");
    }

    [Fact]
    public void FR11_ZeroEstimatedMinutesIsRejected()
    {
        var offer = Run11() with { EstimatedMinutes = new(0, Grade.Stated) };
        Assert.Throws<ArgumentOutOfRangeException>(() => Calculations.ForecastNetPerHour(offer, Defaults));
    }

    // ---- FR-12: the accept rule ----

    [Fact]
    public void FR12_ExactlyTwentyFiveClears()
    {
        Assert.Equal(Verdict.Clears, Calculations.AcceptVerdict(new Result(25.00m, []), 25.00m));
    }

    [Fact]
    public void FR12_OneCentShortDoesNotClear()
    {
        Assert.Equal(Verdict.DoesNotClear, Calculations.AcceptVerdict(new Result(24.99m, []), 25.00m));
    }

    // ---- FR-14: per trip, after the fact ----

    [Fact]
    public void FR14_ActualGrossPerHour_Run4()
    {
        // 34.89 / (55/60) = 38.062
        Assert.Equal(38.06m, Cents(Calculations.ActualGrossPerHour(Run4Pay, Run4).Value));
    }

    [Fact]
    public void FR14_ActualNetPerHour_Run4()
    {
        // (34.89 - 0.1725 x 12.1) / (55/60) = 32.80275 / 0.9167 = 35.785
        var net = Calculations.ActualNetPerHour(Run4Pay, Run4, Defaults);
        Assert.Equal(35.78m, Cents(net.Value));
        Assert.True(net.RestsOnDefault);
    }

    [Theory]
    // Hand log, Sun 08/02 run table, column "TRUE $/mi".
    [InlineData(34.89, 55, 6.4, 5.7, 2.88)] // run 4
    [InlineData(16.12, 27, 3.6, 3.6, 2.24)] // run 5
    [InlineData(27.84, 33, 4.0, 4.0, 3.48)] // run 6
    public void FR14_Acceptance_TruePerMileMatchesTheHandLog(
        double pay, int minutes, double route, double ret, double expected)
    {
        // InlineData cannot hold decimals; the values convert exactly at this precision.
        var actuals = new TripActuals(minutes, (decimal)route, (decimal)ret);
        Assert.Equal((decimal)expected, Cents(Calculations.TruePerMile((decimal)pay, actuals).Value));
    }

    [Fact]
    public void FR14_ZeroMinutesIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Calculations.ActualGrossPerHour(10m, new TripActuals(0, 3m, 3m)));
    }

    [Fact]
    public void FR14_ZeroMilesIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Calculations.TruePerMile(10m, new TripActuals(20, 0m, 0m)));
    }

    // ---- FR-15: deadhead share ----

    [Fact]
    public void FR15_Acceptance_DeadheadShareIsTheComplementOfTheLogsGeometry()
    {
        // The hand log's "geometry" column is the ROUTE share (run 4: 6.4 / 12.1 = 53%).
        // FR-15 asks for the RETURN share, so GigLedger must show 47% where the log shows 53%.
        var share = Calculations.DeadheadShare(Run4);
        Assert.Equal(0.47m, Cents(share.Value));
        Assert.Equal(0.50m, Cents(Calculations.DeadheadShare(Run5).Value));
    }

    // ---- FR-18 to FR-20: per shift ----

    private static readonly ShiftSpan Synthetic = new(ClockMinutes: 300, StartOdometer: 1000m, EndOdometer: 1050m);
    private static readonly TripRecord[] SyntheticTrips =
    [
        new(40m, new TripActuals(60, 10m, 8m)),
        new(60m, new TripActuals(120, 15m, 12m)),
    ];

    [Fact]
    public void FR18_ShiftAndDeadheadMilesComeFromTheOdometer()
    {
        var s = Calculations.SummarizeShift(Synthetic, SyntheticTrips, Defaults);
        Assert.Equal(50m, s.ShiftMiles);    // 1050 - 1000
        Assert.Equal(25m, s.DeadheadMiles); // 50 - (10 + 15) paid route miles
    }

    [Fact]
    public void FR19_ShiftRateSitsBesideTripRate()
    {
        var s = Calculations.SummarizeShift(Synthetic, SyntheticTrips, Defaults);
        Assert.Equal(20.00m, Cents(s.ShiftRate.Value)); // 100 / 5 h
        Assert.Equal(33.33m, Cents(s.TripRate.Value));  // 100 / 3 h
        Assert.Equal(13.33m, Cents(s.RateGap));
    }

    [Fact]
    public void FR20_SummaryTotals()
    {
        var s = Calculations.SummarizeShift(Synthetic, SyntheticTrips, Defaults);
        Assert.Equal(2, s.Trips);
        Assert.Equal(100m, s.Gross);
        Assert.Equal(8.625m, s.EnergyCost.Value); // 50 odometer miles x 0.1725
        Assert.Equal(91.375m, s.Net.Value);
        Assert.Equal(5m, s.ClockHours);
        Assert.Equal(3m, s.TripHours);
        Assert.True(s.Net.RestsOnDefault);
    }

    [Fact]
    public void FR19_Acceptance_Sunday0802_ShiftRate()
    {
        // Hand log, Sun 08/02: "Session 6:57-11:06. $147.32 in 4.16 hr = $35.45/hr".
        // 6:57 to 11:06 is 249 minutes = 4.15 hr, and 147.32 / 4.15 = $35.50/hr. The log
        // rounded the hours up to 4.16 before dividing, which moved the rate by 5 cents.
        // Investigated per SDD 9.3; the log's arithmetic is the one that is off.
        var trips = new TripRecord[]
        {
            new(34.89m, Run4), new(16.12m, Run5), new(27.84m, Run6),
            new(37.95m, new TripActuals(48, 3.5m, 0m)),  // run 7, chained, no return
            new(30.52m, new TripActuals(52, 12.4m, 0m)), // run 8, last trip, no return
        };
        var sunday = new ShiftSpan(ClockMinutes: 249, StartOdometer: 0m, EndOdometer: 43.2m);
        var s = Calculations.SummarizeShift(sunday, trips, Defaults);
        Assert.Equal(147.32m, s.Gross);
        Assert.Equal(35.50m, Cents(s.ShiftRate.Value));
    }

    [Fact]
    public void FR18_EndOdometerBelowStartIsRejected()
    {
        var backwards = new ShiftSpan(300, 1050m, 1000m);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Calculations.SummarizeShift(backwards, SyntheticTrips, Defaults));
    }
}
