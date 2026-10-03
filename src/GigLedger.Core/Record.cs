using System.Security.Cryptography;

namespace GigLedger.Core;

/// <summary>One drive in the mileage log (FR-26): where and why, in the driver's words.</summary>
public sealed record Drive(DateOnly Date, Graded<decimal> StartOdometer, Graded<decimal> EndOdometer, Purpose Purpose, string Description);

/// <summary>A stored drive. ShiftId is set when the drive was logged from a shift (FR-26a).</summary>
public sealed record StoredDrive(Guid Id, Drive Drive, Guid? ShiftId);

/// <summary>Business and personal miles for a year (FR-26, FR-29).</summary>
public sealed record MileageTotals(decimal BusinessMiles, decimal PersonalMiles);

public enum ExpenseCategory { Phone, Parking, Tolls, Supplies, Vehicle, Other }

/// <summary>A business expense beyond charging (FR-28).</summary>
public sealed record Expense(DateOnly Date, ExpenseCategory Category, Graded<decimal> Amount, string Description);

public sealed record StoredExpense(Guid Id, Expense Expense);

/// <summary>One version of a corrected record, with the reason it was corrected (FR-25).</summary>
public sealed record Version<T>(Guid Id, T Value, DateTimeOffset RecordedAt, string? Reason);

/// <summary>What a receipt or export file is attached to (FR-27).</summary>
public enum AttachedTo { ChargeSession, Tip, Expense }

public sealed record AttachmentInfo(Guid Id, AttachedTo Owner, Guid OwnerId, string FileName, string ContentType, string Sha256, DateTimeOffset RecordedAt);

public sealed record StoredAttachment(AttachmentInfo Info, byte[] Content);

public static class RecordKeeping
{
    /// <summary>FR-26: rejects a drive no odometer could produce, or one with no stated purpose.</summary>
    public static void Validate(Drive drive)
    {
        if (drive.EndOdometer.Value < drive.StartOdometer.Value)
            throw new ArgumentOutOfRangeException(nameof(drive), drive.EndOdometer.Value, "End odometer is below the start reading.");
        if (string.IsNullOrWhiteSpace(drive.Description))
            throw new ArgumentException("Say where the drive went and why; the log needs both.", nameof(drive));
    }

    /// <summary>FR-26: end reading minus start reading.</summary>
    public static decimal Miles(Drive drive) => drive.EndOdometer.Value - drive.StartOdometer.Value;

    /// <summary>FR-26: business and personal miles across the drives given.</summary>
    public static MileageTotals Totals(IEnumerable<Drive> drives)
    {
        var list = drives.ToList();
        return new MileageTotals(
            list.Where(d => d.Purpose == Purpose.Work).Sum(Miles),
            list.Where(d => d.Purpose == Purpose.Personal).Sum(Miles));
    }

    /// <summary>FR-28.</summary>
    public static void Validate(Expense expense)
    {
        if (expense.Amount.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(expense), expense.Amount.Value, "An expense must cost something.");
    }

    /// <summary>FR-25: a correction must say why.</summary>
    public static void ValidateReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A correction must say why it was made.", nameof(reason));
    }

    /// <summary>FR-27: the SHA-256 of the content, lowercase hex.</summary>
    public static string Sha256(byte[] content) => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
}

public interface IMileageService
{
    Guid Log(Drive drive);
    IReadOnlyList<StoredDrive> Year(int year);
    MileageTotals Totals(int year);
    Guid Correct(Guid driveId, Drive corrected, string reason);
    IReadOnlyList<Version<Drive>> History(Guid driveId);
}

public interface IExpenseService
{
    Guid Record(Expense expense);
    IReadOnlyList<StoredExpense> Year(int year);
    Guid Correct(Guid expenseId, Expense corrected, string reason);
    IReadOnlyList<Version<Expense>> History(Guid expenseId);
}

/// <summary>Corrections to records captured in Slices 1 and 2 (FR-25).</summary>
public interface ICorrectionService
{
    void CorrectActuals(Guid tripId, GradedActuals corrected, string reason, Acknowledgement? acknowledgement = null);
    IReadOnlyList<Version<GradedActuals>> ActualsHistory(Guid tripId);
    Guid CorrectCharge(Guid chargeId, ChargeSession corrected, string reason);
    IReadOnlyList<Version<ChargeSession>> ChargeHistory(Guid chargeId);
}

public interface IAttachmentService
{
    Guid Attach(AttachedTo owner, Guid ownerId, string fileName, string contentType, byte[] content);
    StoredAttachment Get(Guid attachmentId);
    IReadOnlyList<AttachmentInfo> For(AttachedTo owner, Guid ownerId);
    /// <summary>True when the stored content still matches the hash taken when it was attached.</summary>
    bool Verify(Guid attachmentId);
}

public interface IBackupService
{
    /// <summary>NFR-7: writes a dated copy of the whole ledger into the folder and returns its path.</summary>
    string Backup(string folder);
}
