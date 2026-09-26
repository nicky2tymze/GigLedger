using GigLedger.Core;

namespace GigLedger.Tests;

/// <summary>
/// Slice 2 calculation tests, written from SRS 0.3 and SDD 6.5 before the code. Expected values
/// are worked by hand, or come from the driving log's 07/20 to 08/06 charging accounting.
/// </summary>
public class EnergyCalculationTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 1, 8, 0, 0, TimeSpan.FromHours(-5));
    private static readonly HomeRate RealRate = new(0.13m, new DateOnly(2026, 1, 1), IsPlaceholder: false);

    private static decimal Cents(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static ChargeSession Fast(decimal odometer, decimal kwh, decimal cost, int startSoc = 20, int day = 0) => new(
        T0.AddDays(day), new(odometer, Grade.Measured), new(kwh, Grade.Measured), new(cost, Grade.Measured),
        startSoc, 80, "Blink", ChargeType.DcFast, Purpose.Work);

    private static ChargeSession Home(decimal odometer, decimal kwh, int startSoc = 20, int day = 0) => new(
        T0.AddDays(day), new(odometer, Grade.Measured), new(kwh, Grade.Entered), null,
        startSoc, 80, "Home", ChargeType.Home, Purpose.Personal);

    private static CostedCharge Costed(ChargeSession s, HomeRate? rate = null) =>
        new(s, EnergyCalculations.ChargeCost(s, rate ?? HomeRate.Placeholder));

    // ---- FR-5: sessions and their cost ----

    [Fact]
    public void FR5_AFastSessionCostsItsReceipt()
    {
        var cost = EnergyCalculations.ChargeCost(Fast(1000m, 30m, 20.70m), HomeRate.Placeholder);
        Assert.Equal(20.70m, cost.Value);
        Assert.False(cost.RestsOnDefault);
    }

    [Fact]
    public void FR5_AHomeSessionWithoutAReceiptIsKwhTimesTheRate_AndNamesThePlaceholder()
    {
        var cost = EnergyCalculations.ChargeCost(Home(1000m, 20m), HomeRate.Placeholder);
        Assert.Equal(3.00m, cost.Value); // 20 kWh x $0.15
        Assert.Contains(cost.Assumptions, a => a.Input == "home rate");
    }

    [Fact]
    public void FR5_ARealHomeRateIsNotAnAssumption()
    {
        var cost = EnergyCalculations.ChargeCost(Home(1000m, 20m), RealRate);
        Assert.Equal(2.60m, cost.Value); // 20 x 0.13
        Assert.False(cost.RestsOnDefault);
    }

    [Theory]
    [InlineData(0, 30, 20, 80)]    // no energy
    [InlineData(30, -1, 20, 80)]   // negative cost
    [InlineData(30, 20, -1, 80)]   // state of charge below 0
    [InlineData(30, 20, 20, 101)]  // above 100
    [InlineData(30, 20, 80, 20)]   // ended lower than it started
    public void FR5_ImpossibleSessionsAreRejected(double kwh, double cost, int startSoc, int endSoc)
    {
        var s = Fast(1000m, (decimal)kwh, (decimal)cost) with { StartSoc = startSoc, EndSoc = endSoc };
        Assert.Throws<ArgumentOutOfRangeException>(() => EnergyCalculations.Validate(s));
    }

    [Fact]
    public void FR5_AFastSessionWithoutACostIsRejected()
    {
        var s = Fast(1000m, 30m, 20.70m) with { Cost = null };
        Assert.Throws<ArgumentException>(() => EnergyCalculations.Validate(s));
    }

    [Fact]
    public void FR5_UnknownOdometerAndStateOfChargeAreAccepted()
    {
        // A receipt carries neither, so an imported session has neither (SRS 0.5).
        var s = Fast(1000m, 30m, 20.70m) with { Odometer = null, StartSoc = null, EndSoc = null };
        EnergyCalculations.Validate(s);
    }

    [Fact]
    public void FR5_AKnownStateOfChargeIsStillChecked_WhenTheOtherEndIsUnknown()
    {
        var s = Fast(1000m, 30m, 20.70m) with { StartSoc = null, EndSoc = 101 };
        Assert.Throws<ArgumentOutOfRangeException>(() => EnergyCalculations.Validate(s));
    }

    // ---- FR-8: wall-to-wheel efficiency ----

    [Fact]
    public void FR8_ASessionWithUnknownOdometerStillCountsItsEnergy()
    {
        // 1000 -> 1240 is 240 miles, on the 40 + 20 kWh bought before the last known reading.
        // The middle session has no odometer, but its energy was bought and driven.
        var eff = EnergyCalculations.MeasuredEfficiency(
            [Fast(1000m, 40m, 27.60m), Fast(0m, 20m, 13.80m, day: 1) with { Odometer = null }, Fast(1240m, 50m, 34.50m, day: 3)]);
        Assert.Equal(4.0m, eff!.Value);
    }

    [Fact]
    public void FR8_OrderComesFromTheClock()
    {
        // Out of order in the list; time puts the unknown-odometer session between the readings.
        var eff = EnergyCalculations.MeasuredEfficiency(
            [Fast(1240m, 50m, 34.50m, day: 3), Fast(0m, 20m, 13.80m, day: 1) with { Odometer = null }, Fast(1000m, 40m, 27.60m)]);
        Assert.Equal(4.0m, eff!.Value);
    }

    [Fact]
    public void FR8_EnergyOutsideTheKnownReadingsDoesNotCount()
    {
        // Sessions before the first reading and after the last are outside the measured miles.
        var eff = EnergyCalculations.MeasuredEfficiency(
            [Fast(0m, 99m, 60m) with { Odometer = null }, Fast(1000m, 40m, 27.60m, day: 1),
             Fast(1160m, 30m, 20.70m, day: 2), Fast(0m, 99m, 60m, day: 3) with { Odometer = null }]);
        Assert.Equal(4.0m, eff!.Value); // 160 / 40
    }

    [Fact]
    public void FR8_FewerThanTwoKnownReadingsCannotBeMeasured()
    {
        Assert.Null(EnergyCalculations.MeasuredEfficiency(
            [Fast(1000m, 40m, 27.60m), Fast(0m, 35m, 24.15m, day: 2) with { Odometer = null }]));
    }

    [Fact]
    public void FR8_AnUnknownArrivalChargeIsNamed()
    {
        var eff = EnergyCalculations.MeasuredEfficiency(
            [Fast(1000m, 40m, 27.60m) with { StartSoc = null }, Fast(1160m, 35m, 24.15m, day: 2)]);
        Assert.Equal(4.0m, eff!.Value);
        Assert.Contains(eff.Assumptions, a => a.Input == "state of charge" && a.Source.Contains("unknown"));
    }

    [Fact]
    public void FR8_MilesBetweenFirstAndLastOverKwhBoughtBeforeTheLast()
    {
        // 1000 -> 1300 is 300 miles, on the 40 + 35 kWh bought at the first two sessions.
        // The last session's 50 kWh has not been driven yet and must not count.
        var eff = EnergyCalculations.MeasuredEfficiency(
            [Fast(1000m, 40m, 27.60m), Fast(1150m, 35m, 24.15m, day: 2), Fast(1300m, 50m, 34.50m, day: 4)]);
        Assert.Equal(4.0m, eff!.Value);
        Assert.False(eff.RestsOnDefault);
    }

    [Fact]
    public void FR8_OrderComesFromTheOdometer()
    {
        var eff = EnergyCalculations.MeasuredEfficiency(
            [Fast(1300m, 50m, 34.50m, day: 4), Fast(1000m, 40m, 27.60m), Fast(1150m, 35m, 24.15m, day: 2)]);
        Assert.Equal(4.0m, eff!.Value);
    }

    [Fact]
    public void FR8_ADifferentArrivalChargeIsNamed()
    {
        // Arriving at 20% then 35% means some bought energy is still in the battery: the result
        // understates efficiency, and must say why.
        var eff = EnergyCalculations.MeasuredEfficiency([Fast(1000m, 40m, 27.60m, startSoc: 20), Fast(1150m, 35m, 24.15m, startSoc: 35, day: 2)]);
        Assert.Equal(3.75m, eff!.Value); // 150 / 40
        Assert.Contains(eff.Assumptions, a => a.Input == "state of charge");
    }

    [Fact]
    public void FR8_FewerThanTwoSessionsCannotBeMeasured()
    {
        Assert.Null(EnergyCalculations.MeasuredEfficiency([]));
        Assert.Null(EnergyCalculations.MeasuredEfficiency([Fast(1000m, 40m, 27.60m)]));
    }

    [Fact]
    public void FR8_NoMilesCannotBeMeasured()
    {
        Assert.Null(EnergyCalculations.MeasuredEfficiency([Fast(1000m, 40m, 27.60m), Fast(1000m, 5m, 3.45m, day: 1)]));
    }

    // ---- FR-9: price per kWh, three ways ----

    [Fact]
    public void FR9_Acceptance_TheLogsTwoPriceErasReproduceTheirRates()
    {
        // Driving log, "THE FULL ACCOUNTING, 07/20 - 08/06": $130.82 for 267.0 kWh in the $0.49
        // era, $169.94 for 246.3 kWh in the $0.69 era.
        var early = EnergyCalculations.Prices([Costed(Fast(1000m, 267.0m, 130.82m))]);
        var late = EnergyCalculations.Prices([Costed(Fast(2000m, 246.3m, 169.94m))]);
        Assert.Equal(0.49m, Cents(early.FastOnly!.Value));
        Assert.Equal(0.69m, Cents(late.FastOnly!.Value));
    }

    [Fact]
    public void FR9_HomeFastAndBlend_AndTheFastShareOfCost()
    {
        // Home: 20 kWh at the $0.15 placeholder = $3.00. Fast: 30 kWh for $20.70.
        var prices = EnergyCalculations.Prices([Costed(Home(1000m, 20m)), Costed(Fast(1100m, 30m, 20.70m, day: 1))]);
        Assert.Equal(0.15m, prices.HomeOnly!.Value);
        Assert.Equal(0.69m, prices.FastOnly!.Value);
        Assert.Equal(0.474m, prices.Blend!.Value);                     // 23.70 / 50
        Assert.Equal(0.87m, Cents(prices.FastShareOfCost!.Value));     // 20.70 / 23.70
        Assert.Contains(prices.Blend.Assumptions, a => a.Input == "home rate");
        Assert.DoesNotContain(prices.FastOnly.Assumptions, a => a.Input == "home rate");
    }

    [Fact]
    public void FR9_NoHomeChargingMeansNoHomePrice()
    {
        var prices = EnergyCalculations.Prices([Costed(Fast(1000m, 30m, 20.70m))]);
        Assert.Null(prices.HomeOnly);
        Assert.Equal(1m, prices.FastShareOfCost!.Value);
    }

    [Fact]
    public void FR9_AnEmptyWindowHasNoPrices()
    {
        var prices = EnergyCalculations.Prices([]);
        Assert.Null(prices.Blend);
        Assert.Null(prices.FastShareOfCost);
    }

    // ---- FR-9a: the window replaces the defaults where it can ----

    [Fact]
    public void FR9a_AMeasuredWindowReplacesBothDefaults()
    {
        var window = new[] { Costed(Fast(1000m, 40m, 27.60m)), Costed(Fast(1150m, 35m, 24.15m, day: 2)), Costed(Fast(1300m, 50m, 34.50m, day: 4)) };
        var basis = EnergyCalculations.ForWindow(window, Settings.Initial);
        Assert.Equal(4.0m, basis.MilesPerKwh);
        Assert.Equal(0.69m, basis.PricePerKwh); // 86.25 / 125
        Assert.Empty(basis.Assumptions);
    }

    [Fact]
    public void FR9a_OneSessionMeasuresPriceButNotEfficiency()
    {
        var basis = EnergyCalculations.ForWindow([Costed(Fast(1000m, 30m, 14.70m))], Settings.Initial);
        Assert.Equal(4.0m, basis.MilesPerKwh);   // default
        Assert.Equal(0.49m, basis.PricePerKwh);  // measured
        Assert.Contains(basis.Assumptions, a => a.Input == "efficiency");
        Assert.DoesNotContain(basis.Assumptions, a => a.Input == "energy price");
    }

    [Fact]
    public void FR9a_AnEmptyWindowIsTheDefaults()
    {
        // Compared field by field: record equality on a list compares references, not contents.
        var basis = EnergyCalculations.ForWindow([], Settings.Initial);
        Assert.Equal(4.0m, basis.MilesPerKwh);
        Assert.Equal(0.69m, basis.PricePerKwh);
        Assert.Equal(["efficiency", "energy price"], basis.Assumptions.Select(a => a.Input));
    }

    [Fact]
    public void FR9a_ThePlaceholderRateTravelsIntoTheBasis()
    {
        var window = new[] { Costed(Home(1000m, 40m)), Costed(Home(1150m, 35m, day: 2)) };
        var basis = EnergyCalculations.ForWindow(window, Settings.Initial);
        Assert.Contains(basis.Assumptions, a => a.Input == "home rate");
    }

    // ---- FR-16: estimate error ----

    [Fact]
    public void FR16_Acceptance_Run4BeatTheirEstimate()
    {
        // Sun 08/02 run 4: their estimate 58 min and 6.6 mi; actual 55 min and 6.4 mi.
        var offer = new Offer(new(34.89m, Grade.Stated), new(6.6m, Grade.Stated), 2, 32, new(58, Grade.Stated), T0);
        var error = EnergyCalculations.EstimateError(offer, new TripActuals(55, 6.4m, 5.7m));
        Assert.Equal(-3, error.MinutesOver);
        Assert.Equal(-0.2m, error.MilesOver);
    }

    // ---- FR-17: tips recompute the rates, and the before-tip figures stay ----

    [Fact]
    public void FR17_ATipRaisesTheRatesAndKeepsTheOnesBefore()
    {
        var run4 = new TripActuals(55, 6.4m, 5.7m);
        var energy = EnergyBasis.FromDefaults(4.0m, 0.69m);
        var rates = EnergyCalculations.TripRates(34.89m, 6.00m, run4, energy);

        Assert.Equal(38.06m, Cents(rates.GrossPerHourBeforeTip.Value)); // 34.89 / (55/60)
        Assert.Equal(44.61m, Cents(rates.GrossPerHour.Value));          // 40.89 / (55/60)
        Assert.Equal(35.78m, Cents(rates.NetPerHourBeforeTip.Value));
        Assert.Equal(42.33m, Cents(rates.NetPerHour.Value));            // (40.89 - 2.08725) / (55/60)
        Assert.Equal(2.88m, Cents(rates.TruePerMileBeforeTip.Value));
        Assert.Equal(3.38m, Cents(rates.TruePerMile.Value));            // 40.89 / 12.1
        Assert.True(rates.NetPerHour.RestsOnDefault);
    }

    [Fact]
    public void FR17_ShiftGrossIncludesTipsAndShowsThem()
    {
        var shift = new ShiftSpan(300, 1000m, 1050m);
        var trips = new[] { new TripRecord(40m, new TripActuals(60, 10m, 8m), Tip: 10m), new TripRecord(60m, new TripActuals(120, 15m, 12m)) };
        var s = Calculations.SummarizeShift(shift, trips, EnergyBasis.FromDefaults(4.0m, 0.69m));
        Assert.Equal(110m, s.Gross);
        Assert.Equal(10m, s.Tips);
        Assert.Equal(22.00m, Cents(s.ShiftRate.Value)); // 110 / 5 h
    }
}
