namespace GigLedger.Core;

/// <summary>
/// Every formula in SDD section 6, as pure functions of their inputs. Nothing here
/// touches storage or the web, so the UI, the API, and the tests all get the same numbers.
/// Values are never rounded here; rounding is for display only.
/// </summary>
public static class Calculations
{
    /// <summary>Energy cost per mile = price per kWh / miles per kWh.</summary>
    public static Result EnergyCostPerMile(EnergyBasis energy) =>
        throw new NotImplementedException();

    /// <summary>FR-11, FR-13: (pay - cost per mile x (stated + return)) / estimated hours.</summary>
    public static Result ForecastNetPerHour(Offer offer, EnergyBasis energy) =>
        throw new NotImplementedException();

    /// <summary>FR-12: clears when the forecast is at or above the threshold. "25+" includes 25.</summary>
    public static Verdict AcceptVerdict(Result forecast, decimal threshold) =>
        throw new NotImplementedException();

    /// <summary>FR-14: pay / actual hours.</summary>
    public static Result ActualGrossPerHour(decimal pay, TripActuals actuals) =>
        throw new NotImplementedException();

    /// <summary>FR-14: (pay - energy cost of route plus return) / actual hours.</summary>
    public static Result ActualNetPerHour(decimal pay, TripActuals actuals, EnergyBasis energy) =>
        throw new NotImplementedException();

    /// <summary>FR-14: pay / (route + return miles).</summary>
    public static Result TruePerMile(decimal pay, TripActuals actuals) =>
        throw new NotImplementedException();

    /// <summary>FR-15: return miles / (route + return miles).</summary>
    public static Result DeadheadShare(TripActuals actuals) =>
        throw new NotImplementedException();

    /// <summary>FR-18 to FR-20.</summary>
    public static ShiftSummary SummarizeShift(ShiftSpan shift, IReadOnlyList<TripRecord> trips, EnergyBasis energy) =>
        throw new NotImplementedException();
}
