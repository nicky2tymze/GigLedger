namespace GigLedger.Core;

/// <summary>
/// Every formula in SDD section 6, as pure functions of their inputs. Nothing here
/// touches storage or the web, so the UI, the API, and the tests all get the same numbers.
/// Values are never rounded here; rounding is for display only.
/// </summary>
public static class Calculations
{
    /// <summary>Energy cost per mile = price per kWh / miles per kWh.</summary>
    public static Result EnergyCostPerMile(EnergyBasis energy)
    {
        if (energy.MilesPerKwh <= 0)
            throw new ArgumentOutOfRangeException(nameof(energy), energy.MilesPerKwh, "Efficiency must be positive.");
        return new Result(energy.PricePerKwh / energy.MilesPerKwh, energy.Assumptions);
    }

    /// <summary>FR-11, FR-13: (pay - cost per mile x (stated + return)) / estimated hours.</summary>
    public static Result ForecastNetPerHour(Offer offer, EnergyBasis energy)
    {
        var hours = Hours(offer.EstimatedMinutes.Value, nameof(offer));
        var costPerMile = EnergyCostPerMile(energy);
        var assumptions = new List<Assumption>(costPerMile.Assumptions);

        var returnMiles = offer.ReturnMilesOverride ?? offer.StatedMiles.Value;
        if (offer.ReturnMilesOverride is null)
            assumptions.Add(new Assumption("return miles", "estimated equal to stated miles"));

        var energyCost = costPerMile.Value * (offer.StatedMiles.Value + returnMiles);
        return new Result((offer.Pay.Value - energyCost) / hours, assumptions);
    }

    /// <summary>FR-12: clears when the forecast is at or above the threshold. "25+" includes 25.</summary>
    public static Verdict AcceptVerdict(Result forecast, decimal threshold) =>
        forecast.Value >= threshold ? Verdict.Clears : Verdict.DoesNotClear;

    /// <summary>FR-14: pay / actual hours.</summary>
    public static Result ActualGrossPerHour(decimal pay, TripActuals actuals) =>
        new(pay / Hours(actuals.ElapsedMinutes, nameof(actuals)), []);

    /// <summary>FR-14: (pay - energy cost of route plus return) / actual hours.</summary>
    public static Result ActualNetPerHour(decimal pay, TripActuals actuals, EnergyBasis energy)
    {
        var hours = Hours(actuals.ElapsedMinutes, nameof(actuals));
        var costPerMile = EnergyCostPerMile(energy);
        var energyCost = costPerMile.Value * TotalMiles(actuals);
        return new Result((pay - energyCost) / hours, costPerMile.Assumptions);
    }

    /// <summary>FR-14: pay / (route + return miles).</summary>
    public static Result TruePerMile(decimal pay, TripActuals actuals) =>
        new(pay / TotalMiles(actuals), []);

    /// <summary>FR-15: return miles / (route + return miles).</summary>
    public static Result DeadheadShare(TripActuals actuals) =>
        new(actuals.ReturnMiles / TotalMiles(actuals), []);

    /// <summary>FR-18 to FR-20.</summary>
    public static ShiftSummary SummarizeShift(ShiftSpan shift, IReadOnlyList<TripRecord> trips, EnergyBasis energy)
    {
        if (shift.EndOdometer < shift.StartOdometer)
            throw new ArgumentOutOfRangeException(nameof(shift), shift.EndOdometer, "End odometer is below the start reading.");

        var clockHours = Hours(shift.ClockMinutes, nameof(shift));
        var tripHours = Hours(trips.Sum(t => t.Actuals.ElapsedMinutes), nameof(trips));
        var shiftMiles = shift.EndOdometer - shift.StartOdometer;
        var paidRouteMiles = trips.Sum(t => t.Actuals.RouteMiles);
        var gross = trips.Sum(t => t.Pay);

        var costPerMile = EnergyCostPerMile(energy);
        var energyCost = new Result(costPerMile.Value * shiftMiles, costPerMile.Assumptions);
        var net = new Result(gross - energyCost.Value, costPerMile.Assumptions);
        var shiftRate = new Result(gross / clockHours, []);
        var tripRate = new Result(gross / tripHours, []);

        return new ShiftSummary(
            Trips: trips.Count,
            Gross: gross,
            EnergyCost: energyCost,
            Net: net,
            ClockHours: clockHours,
            TripHours: tripHours,
            ShiftMiles: shiftMiles,
            DeadheadMiles: shiftMiles - paidRouteMiles,
            ShiftRate: shiftRate,
            TripRate: tripRate,
            RateGap: tripRate.Value - shiftRate.Value);
    }

    private static decimal Hours(int minutes, string what)
    {
        if (minutes <= 0)
            throw new ArgumentOutOfRangeException(what, minutes, "Time must be positive.");
        return minutes / 60m;
    }

    private static decimal TotalMiles(TripActuals actuals)
    {
        var total = actuals.RouteMiles + actuals.ReturnMiles;
        if (total <= 0)
            throw new ArgumentOutOfRangeException(nameof(actuals), total, "Miles must be positive.");
        return total;
    }
}
