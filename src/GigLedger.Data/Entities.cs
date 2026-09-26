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

public sealed class SettingsRow : LedgerRecord
{
    public decimal AcceptThreshold { get; set; }
    public decimal DefaultMilesPerKwh { get; set; }
    public decimal DefaultPricePerKwh { get; set; }
}

public sealed class ChargeSessionRow : LedgerRecord
{
    public DateTimeOffset At { get; set; }
    public decimal Odometer { get; set; }
    public Grade OdometerGrade { get; set; }
    public decimal Kwh { get; set; }
    public Grade KwhGrade { get; set; }
    public decimal? Cost { get; set; }
    public Grade? CostGrade { get; set; }
    public int StartSoc { get; set; }
    public int EndSoc { get; set; }
    public string Charger { get; set; } = "";
    public ChargeType Type { get; set; }
    public Purpose Purpose { get; set; }
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
