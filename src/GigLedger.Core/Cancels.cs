namespace GigLedger.Core;

/// <summary>Who cancelled a trip (FR-3a).</summary>
public enum CancelledBy { Customer, Store, Platform, Driver }

/// <summary>When in the trip it was cancelled (FR-3a). Anything after pickup was shopped.</summary>
public enum CancelStage { BeforePickup, AfterPickup, AtTheDoor }

/// <summary>
/// Why the driver cancelled (FR-3a). The declaration order is the display order, the driver's own.
/// Stored by name, so a reorder cannot change a stored cancel.
/// </summary>
public enum CancelReason { OrderNotReady, Unreachable, WrongAddress, CustomerIssue, Emergency, CarProblems, Other }

/// <summary>A cancel as the driver reports it. Reason only on a driver cancel; Other needs the note.</summary>
public sealed record Cancellation(DateTimeOffset At, CancelledBy By, CancelStage Stage, CancelReason? Reason = null, string? Note = null)
{
    /// <summary>After pickup the order was shopped, and the cancel pays (FR-3a).</summary>
    public bool Shopped => Stage != CancelStage.BeforePickup;
}

/// <summary>A stored cancel and what it paid.</summary>
public sealed record StoredCancel(Cancellation Cancellation, decimal Paid);

/// <summary>How many cancels had one value of a field.</summary>
public sealed record CountOf<T>(T Value, int Count);

/// <summary>Cancels over a range (FR-21b). Each breakdown lists every value in order, zeros included.</summary>
public sealed record CancelReport(
    int Cancels,
    IReadOnlyList<CountOf<CancelledBy>> ByWho,
    IReadOnlyList<CountOf<CancelStage>> ByWhen,
    IReadOnlyList<CountOf<CancelReason>> ByReason,
    decimal Paid,
    decimal ShoppedMiles);

/// <summary>The rules for cancelling (SDD 6.12). Pure functions, like Calculations.</summary>
public static class CancelRules
{
    public static string Label(CancelReason reason) => reason switch
    {
        CancelReason.OrderNotReady => "Order not ready",
        CancelReason.Unreachable => "Unreachable",
        CancelReason.WrongAddress => "Wrong address",
        CancelReason.CustomerIssue => "Customer issue (dogs, attitude)",
        CancelReason.Emergency => "Emergency",
        CancelReason.CarProblems => "Car problems",
        CancelReason.Other => "Other",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Not a cancel reason."),
    };

    public static string Label(CancelledBy by) => by switch
    {
        CancelledBy.Customer => "Customer",
        CancelledBy.Store => "Store",
        CancelledBy.Platform => "Platform",
        CancelledBy.Driver => "Me",
        _ => throw new ArgumentOutOfRangeException(nameof(by), by, "Not a canceller."),
    };

    public static string Label(CancelStage stage) => stage switch
    {
        CancelStage.BeforePickup => "Before pickup (not shopped)",
        CancelStage.AfterPickup => "After pickup",
        CancelStage.AtTheDoor => "At the door",
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Not a cancel stage."),
    };

    /// <summary>Checks the cancel and returns it as stored: the note trimmed, a blank note as none.</summary>
    public static Cancellation Validate(Cancellation cancellation)
    {
        Label(cancellation.By);
        Label(cancellation.Stage);
        if (cancellation.By == CancelledBy.Driver && cancellation.Reason is null)
            throw new ArgumentException("A cancel of your own needs a reason.", nameof(cancellation));
        if (cancellation.By != CancelledBy.Driver && cancellation.Reason is not null)
            throw new ArgumentException("Only your own cancel takes a reason; who cancelled says the rest.", nameof(cancellation));
        if (cancellation.Reason is { } reason)
            Label(reason);
        var note = string.IsNullOrWhiteSpace(cancellation.Note) ? null : cancellation.Note.Trim();
        if (cancellation.Reason == CancelReason.Other && note is null)
            throw new ArgumentException("\"Other\" needs a note saying what it was.", nameof(cancellation));
        return cancellation with { Note = note };
    }

    /// <summary>FR-21b over the cancels given, with the miles of the shopped ones.</summary>
    public static CancelReport Report(IReadOnlyList<(StoredCancel Cancel, decimal Miles)> cancels) => new(
        cancels.Count,
        Enum.GetValues<CancelledBy>().Select(v => new CountOf<CancelledBy>(v, cancels.Count(c => c.Cancel.Cancellation.By == v))).ToList(),
        Enum.GetValues<CancelStage>().Select(v => new CountOf<CancelStage>(v, cancels.Count(c => c.Cancel.Cancellation.Stage == v))).ToList(),
        Enum.GetValues<CancelReason>().Select(v => new CountOf<CancelReason>(v, cancels.Count(c => c.Cancel.Cancellation.Reason == v))).ToList(),
        cancels.Sum(c => c.Cancel.Paid),
        cancels.Where(c => c.Cancel.Cancellation.Shopped).Sum(c => c.Miles));
}
