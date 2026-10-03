namespace GigLedger.Core;

/// <summary>
/// Why an offer was turned down (FR-2a). The declaration order is the display order, the
/// driver's order of consideration. Stored by name, so a reorder cannot change a stored decline.
/// </summary>
public enum DeclineReason
{
    PayTooLow,
    TooFar,
    TooManyItems,
    Pharmacy,
    Alcohol,
    Heavy,
    Stairs,
    Apartment,
    TooManyDrops,
    LowCharge,
    EndingShift,
    Other,
}

/// <summary>A declined offer as stored: the offer, the forecast at that moment, and why (FR-2a).</summary>
public sealed record StoredDecline(
    Guid Id,
    Guid ShiftId,
    Offer Offer,
    DateTimeOffset DeclinedAt,
    Result Forecast,
    Verdict Verdict,
    decimal Threshold,
    IReadOnlyList<DeclineReason> Reasons,
    string? Note);

/// <summary>How many declines named one reason.</summary>
public sealed record ReasonCount(DeclineReason Reason, int Count);

/// <summary>
/// Declines over a range (FR-21a). ByReason lists every reason in display order, zeros included;
/// a decline with two reasons counts under both, so the counts can sum past Declines.
/// </summary>
public sealed record DeclineReport(int Declines, IReadOnlyList<ReasonCount> ByReason, int RuleSaidClears, int RuleSaidDoesNotClear);

/// <summary>The rules for declining (SDD 6.9). Pure functions, like Calculations.</summary>
public static class DeclineRules
{
    /// <summary>The words the screen shows for a reason.</summary>
    public static string Label(DeclineReason reason) => reason switch
    {
        DeclineReason.PayTooLow => "Pay too low",
        DeclineReason.TooFar => "Too far (includes bad geometry)",
        DeclineReason.TooManyItems => "Too many items",
        DeclineReason.Pharmacy => "Pharmacy",
        DeclineReason.Alcohol => "Alcohol",
        DeclineReason.Heavy => "Heavy",
        DeclineReason.Stairs => "Stairs",
        DeclineReason.Apartment => "Apartment",
        DeclineReason.TooManyDrops => "Too many drops (batched orders)",
        DeclineReason.LowCharge => "Low charge",
        DeclineReason.EndingShift => "Ending shift",
        DeclineReason.Other => "Other",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Not a decline reason."),
    };

    /// <summary>
    /// Checks a decline's reasons and note and returns them as stored: reasons in display order,
    /// the note trimmed, a blank note as none. Refuses no reason, a reason twice, and Other
    /// without a note.
    /// </summary>
    public static (IReadOnlyList<DeclineReason> Reasons, string? Note) Validate(IReadOnlyList<DeclineReason> reasons, string? note)
    {
        if (reasons.Count == 0)
            throw new ArgumentException("Choose at least one reason for declining.", nameof(reasons));
        if (reasons.Distinct().Count() != reasons.Count)
            throw new ArgumentException("A reason was given twice.", nameof(reasons));
        foreach (var reason in reasons)
            Label(reason);

        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (reasons.Contains(DeclineReason.Other) && trimmed is null)
            throw new ArgumentException("\"Other\" needs a note saying what it was.", nameof(note));

        return (reasons.Order().ToList(), trimmed);
    }

    /// <summary>FR-21a over the declines given. The caller chooses the range.</summary>
    public static DeclineReport Report(IReadOnlyList<StoredDecline> declines) => new(
        declines.Count,
        Enum.GetValues<DeclineReason>().Select(r => new ReasonCount(r, declines.Count(d => d.Reasons.Contains(r)))).ToList(),
        declines.Count(d => d.Verdict == Verdict.Clears),
        declines.Count(d => d.Verdict == Verdict.DoesNotClear));
}
