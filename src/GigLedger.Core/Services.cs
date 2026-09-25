namespace GigLedger.Core;

/// <summary>The settings in force (SDD 4.1). Stored as versions; the newest one applies.</summary>
public sealed record Settings(decimal AcceptThreshold, decimal DefaultMilesPerKwh, decimal DefaultPricePerKwh)
{
    /// <summary>Confirmed 2026-09-25 (SDD section 11).</summary>
    public static Settings Initial { get; } = new(25.00m, 4.0m, 0.69m);
}

/// <summary>An offer's forecast and the accept rule's answer (FR-11, FR-12).</summary>
public sealed record OfferEvaluation(Result Forecast, Verdict Verdict, decimal Threshold);

/// <summary>A stored trip: the offer as accepted, and its actuals once recorded.</summary>
public sealed record StoredTrip(Guid Id, Guid ShiftId, Offer Offer, DateTimeOffset AcceptedAt, GradedActuals? Actuals);

/// <summary>Trip actuals with their grades (FR-3, FR-7).</summary>
public sealed record GradedActuals(Graded<int> ElapsedMinutes, Graded<decimal> RouteMiles, Graded<decimal> ReturnMiles)
{
    public TripActuals Values => new(ElapsedMinutes.Value, RouteMiles.Value, ReturnMiles.Value);
}

/// <summary>A stored shift: its start, and its end once closed (FR-4).</summary>
public sealed record StoredShift(
    Guid Id,
    string Platform,
    DateTimeOffset StartedAt,
    Graded<decimal> StartOdometer,
    DateTimeOffset? EndedAt,
    Graded<decimal>? EndOdometer);

public interface ISettingsService
{
    Settings Get();
    /// <summary>Stores a new version; the old one stays (SDD 5.3).</summary>
    void Set(Settings settings);
}

public interface IOfferService
{
    /// <summary>Forecast and verdict. Stores nothing (FR-2).</summary>
    OfferEvaluation Evaluate(Offer offer);
    /// <summary>Stores the offer as a trip on the shift, with the accept time (FR-1, FR-2).</summary>
    Guid Accept(Guid shiftId, Offer offer, DateTimeOffset acceptedAt);
}

public interface ITripService
{
    /// <summary>Written once, after the trip (FR-3, SDD 5.3).</summary>
    void RecordActuals(Guid tripId, GradedActuals actuals);
    StoredTrip Get(Guid tripId);
    /// <summary>Every trip accepted on the shift, in the order accepted.</summary>
    IReadOnlyList<StoredTrip> OnShift(Guid shiftId);
}

public interface IShiftService
{
    Guid Start(string platform, DateTimeOffset startedAt, Graded<decimal> startOdometer);
    /// <summary>Written once, when the shift ends (FR-4, SDD 5.3).</summary>
    void End(Guid shiftId, DateTimeOffset endedAt, Graded<decimal> endOdometer);
    StoredShift Get(Guid shiftId);
    /// <summary>The shift that has started and not ended, if there is one.</summary>
    StoredShift? Open();
    /// <summary>FR-18 to FR-20, on the current settings. The shift must be closed.</summary>
    ShiftSummary Summary(Guid shiftId);
}
