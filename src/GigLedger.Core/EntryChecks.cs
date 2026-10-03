namespace GigLedger.Core;

/// <summary>A value checked at entry against a limit (FR-36).</summary>
public enum EntryLimit { Pay, Speed, TripLength, Tip, ShiftLength }

/// <summary>What a value past a limit needs before it is stored (FR-35).</summary>
public enum CheckLevel { NeedsConfirm, NeedsExplanation }

/// <summary>How an acknowledged value is marked (FR-35).</summary>
public enum MarkLevel { Confirmed, Explained }

/// <summary>The kind of record a mark belongs to.</summary>
public enum MarkedRecord { Trip, Decline, TripActuals, Tip, ShiftClose }

/// <summary>A limit's two figures: above Confirm needs a confirm, above Document an explanation.</summary>
public sealed record LimitPair(decimal Confirm, decimal Document);

/// <summary>The limits in force (FR-36). Stored as versions; the newest one applies.</summary>
public sealed record Limits(LimitPair Pay, LimitPair Speed, LimitPair TripLength, LimitPair Tip, LimitPair ShiftLength, decimal BatteryKwh)
{
    /// <summary>SRS 0.7 FR-36 and FR-37, set by the Architect 2026-10-03.</summary>
    public static Limits Initial { get; } = new(
        Pay: new(80m, 250m),
        Speed: new(60m, 90m),
        TripLength: new(50m, 150m),
        Tip: new(40m, 150m),
        ShiftLength: new(12m, 16m),
        BatteryKwh: 64.8m);

    public LimitPair For(EntryLimit limit) => limit switch
    {
        EntryLimit.Pay => Pay,
        EntryLimit.Speed => Speed,
        EntryLimit.TripLength => TripLength,
        EntryLimit.Tip => Tip,
        EntryLimit.ShiftLength => ShiftLength,
        _ => throw new ArgumentOutOfRangeException(nameof(limit), limit, "Not an entry limit."),
    };
}

/// <summary>One value past its limit: the value, the figure it passed, and what it needs.</summary>
public sealed record LimitCheck(EntryLimit Limit, decimal Value, decimal Passed, CheckLevel Level);

/// <summary>The driver's answer to a check: a confirm, and for a document level, the explanation.</summary>
public sealed record Acknowledgement(bool Confirmed, string? Explanation = null);

/// <summary>A stored mark on a record (FR-35, FR-38).</summary>
public sealed record EntryMark(
    Guid Id,
    MarkedRecord Record,
    Guid RecordId,
    EntryLimit Limit,
    decimal Value,
    decimal Passed,
    MarkLevel Level,
    string? Explanation,
    DateTimeOffset At);

/// <summary>
/// Thrown before anything is stored when a value is past a limit and the acknowledgement is missing or
/// short (SDD 6.10). The screens catch it and show the confirm step.
/// </summary>
public sealed class NeedsAcknowledgementException(IReadOnlyList<LimitCheck> checks)
    : Exception(EntryChecks.Describe(checks))
{
    public IReadOnlyList<LimitCheck> Checks { get; } = checks;
    public bool NeedsExplanation => Checks.Any(c => c.Level == CheckLevel.NeedsExplanation);
}

/// <summary>The entry checks (SDD 6.10). Pure functions, like Calculations.</summary>
public static class EntryChecks
{
    private static readonly System.Globalization.CultureInfo Us = System.Globalization.CultureInfo.GetCultureInfo("en-US");

    /// <summary>Nothing at or below the confirm figure; otherwise the check and what it needs.</summary>
    public static LimitCheck? Check(EntryLimit limit, decimal value, Limits limits)
    {
        var pair = limits.For(limit);
        if (value > pair.Document) return new LimitCheck(limit, value, pair.Document, CheckLevel.NeedsExplanation);
        if (value > pair.Confirm) return new LimitCheck(limit, value, pair.Confirm, CheckLevel.NeedsConfirm);
        return null;
    }

    /// <summary>The checks that apply to one value each, in the order given; the ones within their limit drop out.</summary>
    public static IReadOnlyList<LimitCheck> CheckAll(Limits limits, params (EntryLimit Limit, decimal Value)[] values) =>
        values.Select(v => Check(v.Limit, v.Value, limits)).OfType<LimitCheck>().ToList();

    /// <summary>Miles per hour over a trip: route plus return miles over elapsed time.</summary>
    public static decimal SpeedMph(TripActuals actuals)
    {
        if (actuals.ElapsedMinutes <= 0)
            throw new ArgumentOutOfRangeException(nameof(actuals), actuals.ElapsedMinutes, "A trip takes time: minutes must be more than zero.");
        return (actuals.RouteMiles + actuals.ReturnMiles) * 60m / actuals.ElapsedMinutes;
    }

    /// <summary>Hours from start to end.</summary>
    public static decimal ShiftHours(DateTimeOffset start, DateTimeOffset end) =>
        (decimal)(end - start).Ticks / TimeSpan.TicksPerHour;

    /// <summary>
    /// The marks to store for these checks under this acknowledgement, or NeedsAcknowledgementException
    /// when the acknowledgement does not cover them.
    /// </summary>
    public static IReadOnlyList<(LimitCheck Check, MarkLevel Level, string? Explanation)> Resolve(
        IReadOnlyList<LimitCheck> checks, Acknowledgement? acknowledgement)
    {
        if (checks.Count == 0) return [];
        var explanation = string.IsNullOrWhiteSpace(acknowledgement?.Explanation) ? null : acknowledgement.Explanation.Trim();
        var needsExplanation = checks.Any(c => c.Level == CheckLevel.NeedsExplanation);
        if (acknowledgement is not { Confirmed: true } || (needsExplanation && explanation is null))
            throw new NeedsAcknowledgementException(checks);
        return checks
            .Select(c => c.Level == CheckLevel.NeedsExplanation
                ? (c, MarkLevel.Explained, explanation)
                : (c, MarkLevel.Confirmed, (string?)null))
            .ToList();
    }

    /// <summary>The checks in words, for the confirm step and the API.</summary>
    public static string Describe(IReadOnlyList<LimitCheck> checks) =>
        string.Join(" ", checks.Select(c =>
            $"{Name(c.Limit)} {Format(c.Limit, c.Value)} is above {Format(c.Limit, c.Passed)}" +
            (c.Level == CheckLevel.NeedsExplanation ? ": record it only with an explanation." : ": confirm it is right.")));

    public static string Name(EntryLimit limit) => limit switch
    {
        EntryLimit.Pay => "Pay",
        EntryLimit.Speed => "Speed",
        EntryLimit.TripLength => "Trip length",
        EntryLimit.Tip => "Tip",
        EntryLimit.ShiftLength => "Shift length",
        _ => limit.ToString(),
    };

    public static string Format(EntryLimit limit, decimal value) => limit switch
    {
        EntryLimit.Pay or EntryLimit.Tip => value.ToString("C2", Us),
        EntryLimit.Speed => value.ToString("0.#", Us) + " mph",
        EntryLimit.TripLength => value.ToString("0.#", Us) + " mi",
        EntryLimit.ShiftLength => value.ToString("0.##", Us) + " h",
        _ => value.ToString(Us),
    };

    // ---- Refusals (FR-37) ----

    public static void RefuseNegative(decimal value, string what)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), value, $"{what} cannot be negative.");
    }

    /// <summary>Refuses negative miles or minutes, and zero minutes: speed divides by them.</summary>
    public static void RefuseImpossibleActuals(TripActuals actuals)
    {
        if (actuals.ElapsedMinutes <= 0)
            throw new ArgumentOutOfRangeException(nameof(actuals), actuals.ElapsedMinutes, "A trip takes time: minutes must be more than zero.");
        RefuseNegative(actuals.RouteMiles, "Route miles");
        RefuseNegative(actuals.ReturnMiles, "Return miles");
    }

    /// <summary>Refuses a start more than 5 minutes after now.</summary>
    public static void RefuseFutureStart(DateTimeOffset start, DateTimeOffset now)
    {
        if (start > now.AddMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(start), start, "A shift cannot start in the future.");
    }

    /// <summary>Refuses a shift longer than 24 hours.</summary>
    public static void RefuseOverlongShift(DateTimeOffset start, DateTimeOffset end)
    {
        if (end - start > TimeSpan.FromHours(24))
            throw new ArgumentOutOfRangeException(nameof(end), end, "A shift cannot run longer than 24 hours.");
    }

    /// <summary>Refuses a charge over the battery size plus 25% for charging losses.</summary>
    public static void RefuseOverBattery(decimal kwh, decimal batteryKwh)
    {
        if (kwh > batteryKwh * 1.25m)
            throw new ArgumentOutOfRangeException(nameof(kwh), kwh,
                $"{kwh} kWh is more than the {batteryKwh} kWh battery can take, even allowing 25% for charging losses.");
    }
}

public interface IEntryCheckService
{
    Limits GetLimits();
    /// <summary>Stores a new version; the old one stays (FR-25).</summary>
    void SetLimits(Limits limits);
    /// <summary>FR-38: explained marks whose local date is from..to, inclusive, oldest first.</summary>
    IReadOnlyList<EntryMark> ExplainedValues(DateOnly from, DateOnly to);
    /// <summary>Every mark on one record.</summary>
    IReadOnlyList<EntryMark> MarksOn(Guid recordId);
}
