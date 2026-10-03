using GigLedger.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GigLedger.Data;

/// <summary>Slice 3a: the record. Corrections, mileage, expenses, receipts, and backup.</summary>
public sealed partial class LedgerServices
    : IMileageService, IExpenseService, ICorrectionService, IAttachmentService, IBackupService
{
    // ---- Versions (FR-25) ----

    /// <summary>The rows nothing supersedes: the current version of each record.</summary>
    private static List<T> Current<T>(IEnumerable<T> rows) where T : LedgerRecord
    {
        var all = rows.ToList();
        var superseded = all.Where(r => r.SupersedesId is not null).Select(r => r.SupersedesId!.Value).ToHashSet();
        return all.Where(r => !superseded.Contains(r.Id)).ToList();
    }

    /// <summary>Every version of the record that id belongs to, oldest first.</summary>
    private static List<T> Chain<T>(IEnumerable<T> rows, Guid id) where T : LedgerRecord
    {
        var all = rows.ToDictionary(r => r.Id);
        if (!all.TryGetValue(id, out var row))
            throw new NotFoundException($"No record {id}.");
        while (row.SupersedesId is { } earlier && all.TryGetValue(earlier, out var previous))
            row = previous;
        var chain = new List<T> { row };
        var next = all.Values.ToLookup(r => r.SupersedesId);
        while (next[chain[^1].Id].FirstOrDefault() is { } later)
            chain.Add(later);
        return chain;
    }

    /// <summary>A correction may only replace the current version, and must say why.</summary>
    private static T ToCorrect<T>(IEnumerable<T> rows, Guid id, string reason) where T : LedgerRecord
    {
        RecordKeeping.ValidateReason(reason);
        var all = rows.ToList();
        var row = all.SingleOrDefault(r => r.Id == id) ?? throw new NotFoundException($"No record {id}.");
        if (all.Any(r => r.SupersedesId == id))
            throw new InvalidOperationException($"Record {id} has already been corrected; correct its current version.");
        return row;
    }

    // ---- Mileage (FR-26, FR-26a) ----

    public Guid Log(Drive drive) => AddDrive(drive, shiftId: null);

    private Guid AddDrive(Drive drive, Guid? shiftId, Guid? supersedes = null, string? reason = null)
    {
        RecordKeeping.Validate(drive);
        var row = new DriveRow
        {
            RecordedAt = clock.GetUtcNow(),
            SupersedesId = supersedes,
            CorrectionReason = reason,
            Date = drive.Date,
            StartOdometer = drive.StartOdometer.Value, StartOdometerGrade = drive.StartOdometer.Grade,
            EndOdometer = drive.EndOdometer.Value, EndOdometerGrade = drive.EndOdometer.Grade,
            Purpose = drive.Purpose,
            Description = drive.Description,
            ShiftId = shiftId,
        };
        db.Drives.Add(row);
        db.SaveChanges();
        return row.Id;
    }

    private static Drive ToDrive(DriveRow r) =>
        new(r.Date, new(r.StartOdometer, r.StartOdometerGrade), new(r.EndOdometer, r.EndOdometerGrade), r.Purpose, r.Description);

    IReadOnlyList<StoredDrive> IMileageService.Year(int year) =>
        Current(db.Drives.AsNoTracking())
            .Where(r => r.Date.Year == year)
            .OrderBy(r => r.Date).ThenBy(r => r.StartOdometer)
            .Select(r => new StoredDrive(r.Id, ToDrive(r), r.ShiftId))
            .ToList();

    MileageTotals IMileageService.Totals(int year) =>
        RecordKeeping.Totals(((IMileageService)this).Year(year).Select(d => d.Drive));

    Guid IMileageService.Correct(Guid driveId, Drive corrected, string reason)
    {
        var row = ToCorrect(db.Drives.AsNoTracking(), driveId, reason);
        return AddDrive(corrected, row.ShiftId, driveId, reason);
    }

    IReadOnlyList<Version<Drive>> IMileageService.History(Guid driveId) =>
        Chain(db.Drives.AsNoTracking(), driveId).Select(r => new Version<Drive>(r.Id, ToDrive(r), r.RecordedAt, r.CorrectionReason)).ToList();

    // ---- Expenses (FR-28) ----

    public Guid Record(Expense expense) => AddExpense(expense);

    private Guid AddExpense(Expense expense, Guid? supersedes = null, string? reason = null)
    {
        RecordKeeping.Validate(expense);
        var row = new ExpenseRow
        {
            RecordedAt = clock.GetUtcNow(),
            SupersedesId = supersedes,
            CorrectionReason = reason,
            Date = expense.Date,
            Category = expense.Category,
            Amount = expense.Amount.Value, AmountGrade = expense.Amount.Grade,
            Description = expense.Description,
        };
        db.Expenses.Add(row);
        db.SaveChanges();
        return row.Id;
    }

    private static Expense ToExpense(ExpenseRow r) => new(r.Date, r.Category, new(r.Amount, r.AmountGrade), r.Description);

    IReadOnlyList<StoredExpense> IExpenseService.Year(int year) =>
        Current(db.Expenses.AsNoTracking())
            .Where(r => r.Date.Year == year)
            .OrderBy(r => r.Date)
            .Select(r => new StoredExpense(r.Id, ToExpense(r)))
            .ToList();

    Guid IExpenseService.Correct(Guid expenseId, Expense corrected, string reason)
    {
        ToCorrect(db.Expenses.AsNoTracking(), expenseId, reason);
        return AddExpense(corrected, expenseId, reason);
    }

    IReadOnlyList<Version<Expense>> IExpenseService.History(Guid expenseId) =>
        Chain(db.Expenses.AsNoTracking(), expenseId).Select(r => new Version<Expense>(r.Id, ToExpense(r), r.RecordedAt, r.CorrectionReason)).ToList();

    // ---- Corrections to Slice 1 and 2 records (FR-25) ----

    public void CorrectActuals(Guid tripId, GradedActuals corrected, string reason, Acknowledgement? acknowledgement = null)
    {
        FindTrip(tripId);
        var current = FindActuals(tripId) ?? throw new InvalidOperationException($"Trip {tripId} has no actuals to correct.");
        ToCorrect(db.TripActuals.AsNoTracking().Where(a => a.TripId == tripId), current.Id, reason);
        CheckActuals(tripId, corrected, acknowledgement);
        db.TripActuals.Add(new TripActualsRow
        {
            RecordedAt = clock.GetUtcNow(),
            SupersedesId = current.Id,
            CorrectionReason = reason,
            TripId = tripId,
            ElapsedMinutes = corrected.ElapsedMinutes.Value, ElapsedMinutesGrade = corrected.ElapsedMinutes.Grade,
            RouteMiles = corrected.RouteMiles.Value, RouteMilesGrade = corrected.RouteMiles.Grade,
            ReturnMiles = corrected.ReturnMiles.Value, ReturnMilesGrade = corrected.ReturnMiles.Grade,
        });
        db.SaveChanges();
    }

    public IReadOnlyList<Version<GradedActuals>> ActualsHistory(Guid tripId)
    {
        var current = FindActuals(tripId) ?? throw new InvalidOperationException($"Trip {tripId} has no actuals.");
        return Chain(db.TripActuals.AsNoTracking().Where(a => a.TripId == tripId), current.Id)
            .Select(a => new Version<GradedActuals>(a.Id,
                new(new(a.ElapsedMinutes, a.ElapsedMinutesGrade), new(a.RouteMiles, a.RouteMilesGrade), new(a.ReturnMiles, a.ReturnMilesGrade)),
                a.RecordedAt, a.CorrectionReason))
            .ToList();
    }

    public Guid CorrectCharge(Guid chargeId, ChargeSession corrected, string reason)
    {
        ToCorrect(db.ChargeSessions.AsNoTracking(), chargeId, reason);
        return AddCharge(corrected, chargeId, reason);
    }

    public IReadOnlyList<Version<ChargeSession>> ChargeHistory(Guid chargeId) =>
        Chain(db.ChargeSessions.AsNoTracking(), chargeId)
            .Select(r => new Version<ChargeSession>(r.Id, ToSession(r), r.RecordedAt, r.CorrectionReason))
            .ToList();

    // ---- Receipts (FR-27) ----

    public Guid Attach(AttachedTo owner, Guid ownerId, string fileName, string contentType, byte[] content)
    {
        if (content.Length == 0)
            throw new ArgumentException("The file is empty.", nameof(content));
        var exists = owner switch
        {
            AttachedTo.Expense => db.Expenses.Any(e => e.Id == ownerId),
            AttachedTo.ChargeSession => db.ChargeSessions.Any(c => c.Id == ownerId),
            AttachedTo.Tip => db.Tips.Any(t => t.Id == ownerId),
            _ => false,
        };
        if (!exists)
            throw new NotFoundException($"No {owner} {ownerId} to attach a file to.");

        var row = new AttachmentRow
        {
            RecordedAt = clock.GetUtcNow(),
            Owner = owner,
            OwnerId = ownerId,
            FileName = fileName,
            ContentType = contentType,
            Content = content,
            Sha256 = RecordKeeping.Sha256(content),
        };
        db.Attachments.Add(row);
        db.SaveChanges();
        return row.Id;
    }

    private static AttachmentInfo ToInfo(AttachmentRow r) =>
        new(r.Id, r.Owner, r.OwnerId, r.FileName, r.ContentType, r.Sha256, r.RecordedAt);

    private AttachmentRow FindAttachment(Guid id) =>
        db.Attachments.AsNoTracking().SingleOrDefault(a => a.Id == id) ?? throw new NotFoundException($"No attachment {id}.");

    StoredAttachment IAttachmentService.Get(Guid attachmentId)
    {
        var row = FindAttachment(attachmentId);
        return new StoredAttachment(ToInfo(row), row.Content);
    }

    public IReadOnlyList<AttachmentInfo> For(AttachedTo owner, Guid ownerId) =>
        db.Attachments.AsNoTracking().Where(a => a.Owner == owner && a.OwnerId == ownerId).AsEnumerable()
            .OrderBy(a => a.RecordedAt).Select(ToInfo).ToList();

    /// <summary>Hashes the content as stored now and compares it to the hash taken at attach time.</summary>
    public bool Verify(Guid attachmentId)
    {
        var row = FindAttachment(attachmentId);
        return RecordKeeping.Sha256(row.Content) == row.Sha256;
    }

    // ---- Backup (NFR-7) ----

    public string Backup(string folder)
    {
        Directory.CreateDirectory(folder);
        var stamp = clock.GetLocalNow().ToString("yyyy-MM-dd-HHmmss");
        var path = Path.Combine(folder, $"gigledger-{stamp}.db");
        for (var n = 2; File.Exists(path); n++)
            path = Path.Combine(folder, $"gigledger-{stamp}-{n}.db");

        var source = (SqliteConnection)db.Database.GetDbConnection();
        if (source.State != System.Data.ConnectionState.Open)
            source.Open();
        using (var target = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            target.Open();
            source.BackupDatabase(target);
        }
        return path;
    }
}
