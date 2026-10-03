using GigLedger.Core;

namespace GigLedger.Data;

/// <summary>
/// Every ledger row: inserted once, never updated, never deleted (SRS FR-25, SDD 5.3).
/// A correction is a new row whose SupersedesId points at the row it replaces.
/// </summary>
public abstract class LedgerRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset RecordedAt { get; set; }
    public Guid? SupersedesId { get; set; }
    /// <summary>Why this row corrects the one it supersedes (FR-25). Null on an original.</summary>
    public string? CorrectionReason { get; set; }
}

// Graded numbers are stored as two columns, the value and its grade (SDD 5.1).

public sealed class ShiftRow : LedgerRecord
{
    public string Platform { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public decimal StartOdometer { get; set; }
    public Grade StartOdometerGrade { get; set; }
}

public sealed class ShiftCloseRow : LedgerRecord
{
    public Guid ShiftId { get; set; }
    public DateTimeOffset EndedAt { get; set; }
    public decimal EndOdometer { get; set; }
    public Grade EndOdometerGrade { get; set; }
}

public sealed class TripRow : LedgerRecord
{
    public Guid ShiftId { get; set; }
    public decimal Pay { get; set; }
    public Grade PayGrade { get; set; }
    public decimal StatedMiles { get; set; }
    public Grade StatedMilesGrade { get; set; }
    public int Drops { get; set; }
    public int Items { get; set; }
    public int EstimatedMinutes { get; set; }
    public Grade EstimatedMinutesGrade { get; set; }
    public DateTimeOffset OfferedAt { get; set; }
    public decimal? ReturnMilesOverride { get; set; }
    public DateTimeOffset AcceptedAt { get; set; }
    /// <summary>FR-6a: the tip inside the pay; null when untracked.</summary>
    public decimal? PromisedTip { get; set; }
    public Grade? PromisedTipGrade { get; set; }
}

/// <summary>FR-6a: the promised tip added or changed after accept. Each supersedes the trip's previous one.</summary>
public sealed class PromisedTipRow : LedgerRecord
{
    public Guid TripId { get; set; }
    public decimal Amount { get; set; }
    public Grade AmountGrade { get; set; }
}

/// <summary>FR-6a: the moment a trip's tips were marked final.</summary>
public sealed class TipsInRow : LedgerRecord
{
    public Guid TripId { get; set; }
    public DateTimeOffset At { get; set; }
}

/// <summary>A declined offer (FR-2a, SDD 6.9): the offer as presented, the forecast then, and why.</summary>
public sealed class DeclineRow : LedgerRecord
{
    public Guid ShiftId { get; set; }
    public decimal Pay { get; set; }
    public Grade PayGrade { get; set; }
    public decimal StatedMiles { get; set; }
    public Grade StatedMilesGrade { get; set; }
    public int Drops { get; set; }
    public int Items { get; set; }
    public int EstimatedMinutes { get; set; }
    public Grade EstimatedMinutesGrade { get; set; }
    public DateTimeOffset OfferedAt { get; set; }
    public decimal? ReturnMilesOverride { get; set; }
    public decimal? PromisedTip { get; set; }
    public Grade? PromisedTipGrade { get; set; }
    public DateTimeOffset DeclinedAt { get; set; }
    public decimal ForecastNetPerHour { get; set; }
    /// <summary>What the forecast rested on, one "input: source" per line; empty when nothing (NFR-2).</summary>
    public string ForecastAssumptions { get; set; } = "";
    public Verdict Verdict { get; set; }
    public decimal Threshold { get; set; }
    /// <summary>Reason names, comma separated, in display order.</summary>
    public string Reasons { get; set; } = "";
    public string? Note { get; set; }
}

public sealed class TripActualsRow : LedgerRecord
{
    public Guid TripId { get; set; }
    public int ElapsedMinutes { get; set; }
    public Grade ElapsedMinutesGrade { get; set; }
    public decimal RouteMiles { get; set; }
    public Grade RouteMilesGrade { get; set; }
    public decimal ReturnMiles { get; set; }
    public Grade ReturnMilesGrade { get; set; }
}

/// <summary>The entry limits in force (FR-36), stored as versions like the settings.</summary>
public sealed class LimitsRow : LedgerRecord
{
    public decimal PayConfirm { get; set; }
    public decimal PayDocument { get; set; }
    public decimal SpeedConfirm { get; set; }
    public decimal SpeedDocument { get; set; }
    public decimal TripLengthConfirm { get; set; }
    public decimal TripLengthDocument { get; set; }
    public decimal TipConfirm { get; set; }
    public decimal TipDocument { get; set; }
    public decimal ShiftLengthConfirm { get; set; }
    public decimal ShiftLengthDocument { get; set; }
    public decimal BatteryKwh { get; set; }
}

/// <summary>A value past a limit that the driver confirmed or explained (FR-35, SDD 6.10).</summary>
public sealed class EntryMarkRow : LedgerRecord
{
    public MarkedRecord Record { get; set; }
    public Guid RecordId { get; set; }
    public EntryLimit Limit { get; set; }
    public decimal Value { get; set; }
    public decimal Passed { get; set; }
    public MarkLevel Level { get; set; }
    public string? Explanation { get; set; }
    public DateTimeOffset At { get; set; }
}

public sealed class SettingsRow : LedgerRecord
{
    public decimal AcceptThreshold { get; set; }
    public decimal DefaultMilesPerKwh { get; set; }
    public decimal DefaultPricePerKwh { get; set; }
    /// <summary>
    /// Retired (SRS 0.9 dropped the tip-tracking setting). The column stays, always false: the migration that
    /// added it is published and the table is append-only, so it is not rewritten.
    /// </summary>
    public bool TrackTips { get; set; }
}

public sealed class ChargeSessionRow : LedgerRecord
{
    public DateTimeOffset At { get; set; }
    public decimal? Odometer { get; set; }
    public Grade? OdometerGrade { get; set; }
    public decimal Kwh { get; set; }
    public Grade KwhGrade { get; set; }
    public decimal? Cost { get; set; }
    public Grade? CostGrade { get; set; }
    public int? StartSoc { get; set; }
    public int? EndSoc { get; set; }
    public string Charger { get; set; } = "";
    public ChargeType Type { get; set; }
    public Purpose? Purpose { get; set; }
    /// <summary>The receipt this session was imported from (FR-22); null when entered by hand.</summary>
    public string? ReceiptNumber { get; set; }
}

/// <summary>One payment from a platform's earnings export (FR-23).</summary>
public sealed class PayoutRow : LedgerRecord
{
    public string Platform { get; set; } = "";
    public string TripId { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public string Zone { get; set; } = "";
    public PayoutType Type { get; set; }
    public decimal Amount { get; set; }
    public Grade AmountGrade { get; set; }
    public string DepositStatus { get; set; } = "";
    public DateOnly? DepositedOn { get; set; }
}

public sealed class TipRow : LedgerRecord
{
    public Guid TripId { get; set; }
    public decimal Amount { get; set; }
    public Grade AmountGrade { get; set; }
    public DateTimeOffset PostedAt { get; set; }
}

public sealed class HomeRateRow : LedgerRecord
{
    public decimal PerKwh { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public bool IsPlaceholder { get; set; }
}

public sealed class DriveRow : LedgerRecord
{
    public DateOnly Date { get; set; }
    public decimal StartOdometer { get; set; }
    public Grade StartOdometerGrade { get; set; }
    public decimal EndOdometer { get; set; }
    public Grade EndOdometerGrade { get; set; }
    public Purpose Purpose { get; set; }
    public string Description { get; set; } = "";
    public Guid? ShiftId { get; set; }
}

public sealed class ExpenseRow : LedgerRecord
{
    public DateOnly Date { get; set; }
    public ExpenseCategory Category { get; set; }
    public decimal Amount { get; set; }
    public Grade AmountGrade { get; set; }
    public string Description { get; set; } = "";
}

public sealed class AttachmentRow : LedgerRecord
{
    public AttachedTo Owner { get; set; }
    public Guid OwnerId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public byte[] Content { get; set; } = [];
    public string Sha256 { get; set; } = "";
}

public sealed class MileageRateRow : LedgerRecord
{
    public int Year { get; set; }
    public decimal PerMile { get; set; }
}

public sealed class PlatformFormRow : LedgerRecord
{
    public int Year { get; set; }
    public string Platform { get; set; } = "";
    public TaxForm Form { get; set; }
    public decimal AnnualTotal { get; set; }
    /// <summary>Twelve amounts, comma separated, invariant culture; null when the form has no months.</summary>
    public string? Monthly { get; set; }
}
