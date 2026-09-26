namespace GigLedger.Core;

public enum ChargeType { Home, DcFast }

public enum Purpose { Work, Personal }

/// <summary>
/// One charging event (FR-5). Cost is null for a home session with no receipt. Odometer and state
/// of charge are null when unknown, as on an imported receipt; unknown is never stored as zero.
/// </summary>
public sealed record ChargeSession(
    DateTimeOffset At,
    Graded<decimal>? Odometer,
    Graded<decimal> Kwh,
    Graded<decimal>? Cost,
    int? StartSoc,
    int? EndSoc,
    string Charger,
    ChargeType Type,
    Purpose? Purpose,
    string? ReceiptNumber = null);

/// <summary>The home electricity rate from a date on (FR-5). A placeholder is named wherever it is used.</summary>
public sealed record HomeRate(decimal PerKwh, DateOnly EffectiveFrom, bool IsPlaceholder)
{
    /// <summary>Decided 2026-09-25 (SRS 0.3): used until the rate is read from a bill.</summary>
    public static HomeRate Placeholder { get; } = new(0.15m, new DateOnly(2026, 1, 1), IsPlaceholder: true);
}

/// <summary>A charge session with its cost worked out. Id is the stored session's, when it has one.</summary>
public sealed record CostedCharge(ChargeSession Session, Result Cost, Guid Id = default);

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
    public static void Validate(ChargeSession session)
    {
        if (session.Kwh.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(session), session.Kwh.Value, "A session must deliver energy.");
        if (session.Cost is { Value: < 0 })
            throw new ArgumentOutOfRangeException(nameof(session), session.Cost.Value.Value, "Cost cannot be negative.");
        if (session.StartSoc is < 0 or > 100 || session.EndSoc is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(session), "State of charge is a percentage, 0 to 100.");
        if (session.EndSoc < session.StartSoc)
            throw new ArgumentOutOfRangeException(nameof(session), "A session cannot end below where it started.");
        if (session.Type == ChargeType.DcFast && session.Cost is null)
            throw new ArgumentException("A fast-charging session needs the cost from its receipt.", nameof(session));
    }

    /// <summary>SDD 6.5: the receipt, or kWh x the home rate for a home session without one.</summary>
    public static Result ChargeCost(ChargeSession session, HomeRate homeRate)
    {
        if (session.Cost is { } receipt)
            return new Result(receipt.Value, []);
        if (session.Type != ChargeType.Home)
            throw new ArgumentException("A fast-charging session needs the cost from its receipt.", nameof(session));

        List<Assumption> assumptions = homeRate.IsPlaceholder
            ? [new Assumption("home rate", $"placeholder ${homeRate.PerKwh}/kWh, not yet read from a bill")]
            : [];
        return new Result(session.Kwh.Value * homeRate.PerKwh, assumptions);
    }

    /// <summary>
    /// FR-8: wall-to-wheel miles per kWh over the sessions given, or null when it cannot be
    /// measured. Sessions are taken in time order. Miles run from the first known odometer reading
    /// to the last; the energy is everything bought from the first of those up to, not including,
    /// the last, since the last session's energy is not yet driven. A session with no odometer
    /// between them still counts its energy: it was bought and driven.
    /// </summary>
    public static Result? MeasuredEfficiency(IReadOnlyList<ChargeSession> sessions)
    {
        var ordered = sessions.OrderBy(s => s.At).ToList();
        var firstIndex = ordered.FindIndex(s => s.Odometer is not null);
        var lastIndex = ordered.FindLastIndex(s => s.Odometer is not null);
        if (firstIndex < 0 || lastIndex == firstIndex) return null;
        var first = ordered[firstIndex];
        var last = ordered[lastIndex];

        var miles = last.Odometer!.Value.Value - first.Odometer!.Value.Value;
        var kwh = ordered.Skip(firstIndex).Take(lastIndex - firstIndex).Sum(s => s.Kwh.Value);
        if (miles <= 0 || kwh <= 0) return null;

        List<Assumption> assumptions = (first.StartSoc, last.StartSoc) switch
        {
            (null, _) or (_, null) => [new Assumption("state of charge", "unknown on arrival at one or both ends, so bought and driven energy may differ")],
            var (a, b) when a != b => [new Assumption("state of charge", $"arrived at {a}% and at {b}%, so bought and driven energy differ")],
            _ => [],
        };
        return new Result(miles / kwh, assumptions);
    }

    /// <summary>FR-9: price per kWh home only, fast only, and blended, and the fast share of cost.</summary>
    public static EnergyPrices Prices(IReadOnlyList<CostedCharge> charges)
    {
        var home = charges.Where(c => c.Session.Type == ChargeType.Home).ToList();
        var fast = charges.Where(c => c.Session.Type == ChargeType.DcFast).ToList();

        var totalCost = charges.Sum(c => c.Cost.Value);
        Result? fastShare = totalCost > 0
            ? new Result(fast.Sum(c => c.Cost.Value) / totalCost, Assumptions(charges))
            : null;
        return new EnergyPrices(PricePerKwh(home), PricePerKwh(fast), PricePerKwh(charges), fastShare);
    }

    private static Result? PricePerKwh(IReadOnlyList<CostedCharge> charges)
    {
        var kwh = charges.Sum(c => c.Session.Kwh.Value);
        return kwh > 0 ? new Result(charges.Sum(c => c.Cost.Value) / kwh, Assumptions(charges)) : null;
    }

    private static List<Assumption> Assumptions(IEnumerable<CostedCharge> charges) =>
        charges.SelectMany(c => c.Cost.Assumptions).Distinct().ToList();

    /// <summary>FR-9a: measured where the window allows, the configured default (named) where it does not.</summary>
    public static EnergyBasis ForWindow(IReadOnlyList<CostedCharge> window, Settings defaults)
    {
        var fallback = EnergyBasis.FromDefaults(defaults.DefaultMilesPerKwh, defaults.DefaultPricePerKwh);
        var efficiency = MeasuredEfficiency(window.Select(c => c.Session).ToList());
        var price = Prices(window).Blend;

        var assumptions = new List<Assumption>();
        assumptions.AddRange(efficiency?.Assumptions ?? fallback.Assumptions.Where(a => a.Input == "efficiency"));
        assumptions.AddRange(price?.Assumptions ?? fallback.Assumptions.Where(a => a.Input == "energy price"));

        return new EnergyBasis(
            efficiency?.Value ?? fallback.MilesPerKwh,
            price?.Value ?? fallback.PricePerKwh,
            assumptions.Distinct().ToList());
    }

    /// <summary>FR-16: positive means the platform understated.</summary>
    public static EstimateError EstimateError(Offer offer, TripActuals actuals) =>
        new(actuals.ElapsedMinutes - offer.EstimatedMinutes.Value, actuals.RouteMiles - offer.StatedMiles.Value);

    /// <summary>FR-14, FR-17: every rate before the tip and with it.</summary>
    public static TripRates TripRates(decimal pay, decimal tip, TripActuals actuals, EnergyBasis energy) => new(
        Calculations.ActualGrossPerHour(pay, actuals), Calculations.ActualGrossPerHour(pay + tip, actuals),
        Calculations.ActualNetPerHour(pay, actuals, energy), Calculations.ActualNetPerHour(pay + tip, actuals, energy),
        Calculations.TruePerMile(pay, actuals), Calculations.TruePerMile(pay + tip, actuals));
}
