namespace GigLedger.Core;

/// <summary>
/// A trip's tip, as gross needs it (SDD 6.11). Base plus Counted is the trip's gross. Adjustment is
/// posted minus promised, once all tips are in.
/// </summary>
public sealed record TipState(bool Tracked, decimal Base, decimal Counted, bool Pending, decimal Posted, decimal? Adjustment)
{
    public decimal Gross => Base + Counted;
}

/// <summary>Tip tracking (FR-6, FR-6a). Pure functions, like Calculations.</summary>
public static class TipAccounting
{
    /// <summary>How long a customer can change a tip after delivery.</summary>
    public static readonly TimeSpan ChangeWindow = TimeSpan.FromHours(24);

    public static TipState State(decimal pay, decimal? promised, decimal postedSum, bool allIn)
    {
        if (promised is not { } p)
            return new TipState(false, pay, 0m, false, postedSum, null);
        return allIn
            ? new TipState(true, pay - p, postedSum, false, postedSum, postedSum - p)
            : new TipState(true, pay - p, p, true, postedSum, null);
    }

    /// <summary>The trip's end (accept time plus actual minutes, or the accept time) plus 24 hours.</summary>
    public static DateTimeOffset AllTipsInOpensAt(DateTimeOffset acceptedAt, int? actualMinutes) =>
        acceptedAt.AddMinutes(actualMinutes ?? 0) + ChangeWindow;

    /// <summary>Refuses a negative promised tip, or one larger than the pay it sits inside.</summary>
    public static void RefuseImpossiblePromise(decimal pay, decimal promised)
    {
        if (promised < 0)
            throw new ArgumentOutOfRangeException(nameof(promised), promised, "A promised tip cannot be negative.");
        if (promised > pay)
            throw new ArgumentOutOfRangeException(nameof(promised), promised, "The promised tip is part of the pay, so it cannot be more than the pay.");
    }
}
