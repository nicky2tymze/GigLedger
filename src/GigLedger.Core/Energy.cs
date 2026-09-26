namespace GigLedger.Core;

public enum ChargeType { Home, DcFast }

public enum Purpose { Work, Personal }

/// <summary>One charging event (FR-5). Cost is null for a home session with no receipt.</summary>
public sealed record ChargeSession(
    DateTimeOffset At,
    Graded<decimal> Odometer,
    Graded<decimal> Kwh,
    Graded<decimal>? Cost,
    int StartSoc,
    int EndSoc,
    string Charger,
    ChargeType Type,
    Purpose Purpose);

/// <summary>The home electricity rate from a date on (FR-5). A placeholder is named wherever it is used.</summary>
public sealed record HomeRate(decimal PerKwh, DateOnly EffectiveFrom, bool IsPlaceholder)
{
    /// <summary>Decided 2026-09-25 (SRS 0.3): used until the rate is read from a bill.</summary>
    public static HomeRate Placeholder { get; } = new(0.15m, new DateOnly(2026, 1, 1), IsPlaceholder: true);
}

/// <summary>A charge session with its cost worked out.</summary>
public sealed record CostedCharge(ChargeSession Session, Result Cost);

/// <summary>FR-9: price per kWh three ways, and the fast-charging share of cost. Null where there is no energy of that kind.</summary>
public sealed record EnergyPrices(Result? HomeOnly, Result? FastOnly, Result? Blend, Result? FastShareOfCost);

/// <summary>FR-16: positive means the platform understated.</summary>
public sealed record EstimateError(int MinutesOver, decimal MilesOver);

/// <summary>FR-14, FR-17: a trip's rates before and after its tip.</summary>
public sealed record TripRates(
    Result GrossPerHourBeforeTip, Result GrossPerHour,
    Result NetPerHourBeforeTip, Result NetPerHour,
    Result TruePerMileBeforeTip, Result TruePerMile);

public static class EnergyCalculations
{
    /// <summary>Rejects a session no charger could produce.</summary>
    public static void Validate(ChargeSession session) => throw new NotImplementedException();

    /// <summary>SDD 6.5: the receipt, or kWh × the home rate for a home session without one.</summary>
    public static Result ChargeCost(ChargeSession session, HomeRate homeRate) => throw new NotImplementedException();

    /// <summary>FR-8: wall-to-wheel miles per kWh over the sessions given, or null when it cannot be measured.</summary>
    public static Result? MeasuredEfficiency(IReadOnlyList<ChargeSession> sessions) => throw new NotImplementedException();

    /// <summary>FR-9: price per kWh home only, fast only, and blended, and the fast share of cost.</summary>
    public static EnergyPrices Prices(IReadOnlyList<CostedCharge> charges) => throw new NotImplementedException();

    /// <summary>FR-9a: measured where the window allows, the configured default (named) where it does not.</summary>
    public static EnergyBasis ForWindow(IReadOnlyList<CostedCharge> window, Settings defaults) => throw new NotImplementedException();

    /// <summary>FR-16.</summary>
    public static EstimateError EstimateError(Offer offer, TripActuals actuals) => throw new NotImplementedException();

    /// <summary>FR-14, FR-17.</summary>
    public static TripRates TripRates(decimal pay, decimal tip, TripActuals actuals, EnergyBasis energy) => throw new NotImplementedException();
}
