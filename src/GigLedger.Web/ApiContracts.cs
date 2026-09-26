using GigLedger.Core;

namespace GigLedger.Web;

// Request and response bodies for the JSON API (SDD 7.2). Every number stays graded.

public sealed record StartShiftRequest(string Platform, DateTimeOffset StartedAt, Graded<decimal> StartOdometer);

public sealed record EndShiftRequest(DateTimeOffset EndedAt, Graded<decimal> EndOdometer);

public sealed record AcceptOfferRequest(Offer Offer, DateTimeOffset AcceptedAt);

public sealed record Created(Guid Id);

public sealed record TipRequest(decimal Amount, DateTimeOffset PostedAt);

public sealed record HomeRateRequest(decimal PerKwh, DateOnly EffectiveFrom);

public sealed record CorrectDriveRequest(Drive Drive, string Reason);

public sealed record CorrectExpenseRequest(Expense Expense, string Reason);

public sealed record CorrectActualsRequest(GradedActuals Actuals, string Reason);

public sealed record CorrectChargeRequest(ChargeSession Session, string Reason);

/// <summary>A receipt sent as JSON; the content is base64.</summary>
public sealed record AttachRequest(AttachedTo Owner, Guid OwnerId, string FileName, string ContentType, string ContentBase64);

public sealed record Verification(bool Intact);

public sealed record BackupResult(string Path);

/// <summary>The one folder backups are written to, from configuration. Callers cannot choose a path.</summary>
public sealed record BackupFolder(string Path);
