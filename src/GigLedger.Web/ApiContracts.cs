using GigLedger.Core;

namespace GigLedger.Web;

// Request and response bodies for the JSON API (SDD 7.2). Every number stays graded.

public sealed record StartShiftRequest(string Platform, DateTimeOffset StartedAt, Graded<decimal> StartOdometer);

public sealed record EndShiftRequest(DateTimeOffset EndedAt, Graded<decimal> EndOdometer);

public sealed record AcceptOfferRequest(Offer Offer, DateTimeOffset AcceptedAt);

public sealed record Created(Guid Id);
