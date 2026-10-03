using GigLedger.Core;
using GigLedger.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GigLedger.Tests;

/// <summary>
/// Slice 3a, the record, written from SRS 0.4 before the code: corrections with reasons (FR-25),
/// the mileage log (FR-26, FR-26a), expenses (FR-28), receipts (FR-27), and backup (NFR-7).
/// </summary>
public sealed class RecordTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 9, 0, 0, TimeSpan.FromHours(-5));
    private static readonly DateOnly Today = new(2026, 9, 25);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly LedgerContext _db;
    private readonly LedgerServices _ledger;
    private readonly TestClock _clock = new(Now);

    public RecordTests()
    {
        _connection.Open();
        _db = LedgerDatabase.Open(_connection);
        _ledger = new LedgerServices(_db, _clock);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private IMileageService Mileage => _ledger;
    private IExpenseService Expenses => _ledger;
    private ICorrectionService Corrections => _ledger;
    private IAttachmentService Attachments => _ledger;
    private IShiftService Shifts => _ledger;
    private ITripService Trips => _ledger;
    private IChargeService Charges => _ledger;

    private static Drive DriveIn(decimal start = 17_948m, decimal end = 17_958m) =>
        new(Today, new(start, Grade.Measured), new(end, Grade.Measured), Purpose.Work, "Home to Walmart 1102 for the 6:00 window");

    private static Expense Binders() =>
        new(Today, ExpenseCategory.Supplies, new(18.47m, Grade.Measured), "Binders and a three-hole punch");

    // ---- Core rules ----

    [Fact]
    public void FR26_MilesAreEndMinusStart()
    {
        Assert.Equal(10m, RecordKeeping.Miles(DriveIn()));
    }

    [Fact]
    public void FR26_ABackwardsDriveIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RecordKeeping.Validate(DriveIn(start: 17_958m, end: 17_948m)));
    }

    [Fact]
    public void FR26_ADriveMustSayWhereAndWhy()
    {
        Assert.Throws<ArgumentException>(() => RecordKeeping.Validate(DriveIn() with { Description = "  " }));
    }

    [Fact]
    public void FR26_TotalsSplitBusinessFromPersonal()
    {
        var totals = RecordKeeping.Totals([DriveIn(), DriveIn(17_958m, 17_968m) with { Purpose = Purpose.Personal }]);
        Assert.Equal(new MileageTotals(10m, 10m), totals);
    }

    [Fact]
    public void FR28_AnExpenseMustCostSomething()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RecordKeeping.Validate(Binders() with { Amount = new(0m, Grade.Measured) }));
    }

    [Fact]
    public void FR25_ACorrectionMustSayWhy()
    {
        Assert.Throws<ArgumentException>(() => RecordKeeping.ValidateReason(""));
        Assert.Throws<ArgumentException>(() => RecordKeeping.ValidateReason(null));
        RecordKeeping.ValidateReason("typed 17,958 as 17,985");
    }

    // ---- FR-26: the mileage log ----

    [Fact]
    public void FR26_ADriveIsLoggedAndCountsInItsYear()
    {
        var id = Mileage.Log(DriveIn());
        var stored = Assert.Single(Mileage.Year(2026));
        Assert.Equal((id, DriveIn(), (Guid?)null), (stored.Id, stored.Drive, stored.ShiftId));
        Assert.Empty(Mileage.Year(2025));
        Assert.Equal(new MileageTotals(10m, 0m), Mileage.Totals(2026));
    }

    [Fact]
    public void FR26a_EndingAShiftLogsItsSpanAsBusiness()
    {
        var shift = Shifts.Start("Spark", Now, new(17_958m, Grade.Measured));
        Shifts.End(shift, Now.AddHours(3), new(18_001.5m, Grade.Measured));

        var drive = Assert.Single(Mileage.Year(2026));
        Assert.Equal(shift, drive.ShiftId);
        Assert.Equal(Purpose.Work, drive.Drive.Purpose);
        Assert.Equal(43.5m, RecordKeeping.Miles(drive.Drive));
        Assert.Contains("Spark", drive.Drive.Description);
    }

    [Fact]
    public void FR26a_AShiftThatDroveNowhereLogsNoDrive()
    {
        var shift = Shifts.Start("Spark", Now, new(17_958m, Grade.Measured));
        Shifts.End(shift, Now.AddHours(1), new(17_958m, Grade.Measured));
        Assert.Empty(Mileage.Year(2026));
    }

    // ---- FR-25: corrections ----

    [Fact]
    public void FR25_ACorrectedDriveShowsTheNewValueAndKeepsTheOld()
    {
        var original = Mileage.Log(DriveIn(end: 17_985m));
        _clock.Now = Now.AddMinutes(5);
        var corrected = Mileage.Correct(original, DriveIn(), "typed 17,958 as 17,985");

        var current = Assert.Single(Mileage.Year(2026));
        Assert.Equal(corrected, current.Id);
        Assert.Equal(10m, RecordKeeping.Miles(current.Drive));

        var history = Mileage.History(corrected);
        Assert.Equal([original, corrected], history.Select(v => v.Id));
        Assert.Null(history[0].Reason);
        Assert.Equal("typed 17,958 as 17,985", history[1].Reason);
        Assert.Equal(37m, RecordKeeping.Miles(history[0].Value)); // the original is still readable
    }

    [Fact]
    public void FR25_OnlyTheCurrentVersionCanBeCorrected()
    {
        var original = Mileage.Log(DriveIn(end: 17_985m));
        Mileage.Correct(original, DriveIn(), "typo");
        Assert.Throws<InvalidOperationException>(() => Mileage.Correct(original, DriveIn(), "again"));
    }

    [Fact]
    public void FR25_ACorrectionWithoutAReasonIsRefused()
    {
        var original = Mileage.Log(DriveIn());
        Assert.Throws<ArgumentException>(() => Mileage.Correct(original, DriveIn(end: 17_959m), " "));
        Assert.Single(Mileage.History(original));
    }

    [Fact]
    public void FR25_CorrectingAnUnknownRecordIsNotFound()
    {
        Assert.Throws<NotFoundException>(() => Mileage.Correct(Guid.NewGuid(), DriveIn(), "typo"));
    }

    [Fact]
    public void FR25_CorrectedActualsFlowIntoTheTripAndTheShift()
    {
        var shift = Shifts.Start("Spark", Now, new(17_000m, Grade.Measured));
        var trip = _ledger.Accept(shift, new Offer(new(34.89m, Grade.Stated), new(6.6m, Grade.Stated), 2, 32, new(58, Grade.Stated), Now), Now);
        // A misread entry the entry checks let through (48 mph): 5 minutes would now be stopped at entry (FR-35).
        Trips.RecordActuals(trip, new(new(15, Grade.Entered), new(6.4m, Grade.Entered), new(5.7m, Grade.Entered)));
        Corrections.CorrectActuals(trip, new(new(55, Grade.Entered), new(6.4m, Grade.Entered), new(5.7m, Grade.Entered)), "misread: 55 minutes, not 15");

        Assert.Equal(55, Trips.Get(trip).Actuals!.ElapsedMinutes.Value);
        Assert.Equal(2, Corrections.ActualsHistory(trip).Count);
        Shifts.End(shift, Now.AddMinutes(90), new(17_012.1m, Grade.Measured));
        Assert.Equal(55m / 60m, Shifts.Summary(shift).TripHours);
    }

    [Fact]
    public void FR25_ACorrectedChargeReplacesTheOriginalInTheWindow()
    {
        // A transposed figure the battery check lets through; 243 kWh would now be refused at entry (FR-37).
        var session = new ChargeSession(Now.AddDays(-1), new(17_935m, Grade.Measured), new(34.2m, Grade.Measured), new(16.77m, Grade.Measured),
            36, 71, "Blink", ChargeType.DcFast, Purpose.Work);
        var original = Charges.Record(session);
        var corrected = Corrections.CorrectCharge(original, session with { Kwh = new(24.3m, Grade.Measured) }, "kWh read as 34.2, receipt says 24.3");

        var stored = Assert.Single(Charges.Between(Now.AddDays(-2), Now));
        Assert.Equal(corrected, stored.Id);
        Assert.Equal(24.3m, stored.Session.Kwh.Value);
        Assert.Equal(2, Corrections.ChargeHistory(corrected).Count);
    }

    // ---- FR-28: expenses ----

    [Fact]
    public void FR28_AnExpenseIsRecordedAndCorrectable()
    {
        var id = Expenses.Record(Binders());
        Assert.Equal(Binders(), Assert.Single(Expenses.Year(2026)).Expense);
        var corrected = Expenses.Correct(id, Binders() with { Amount = new(21.47m, Grade.Measured) }, "receipt total was 21.47 with tax");
        Assert.Equal(21.47m, Assert.Single(Expenses.Year(2026)).Expense.Amount.Value);
        Assert.Equal(2, Expenses.History(corrected).Count);
    }

    // ---- FR-27: receipts ----

    private static readonly byte[] ReceiptPdf = "%PDF-1.7 Blink receipt 24.3 kWh $16.77"u8.ToArray();

    [Fact]
    public void FR27_AReceiptIsStoredWithItsHash()
    {
        var expense = Expenses.Record(Binders());
        var id = Attachments.Attach(AttachedTo.Expense, expense, "walmart.pdf", "application/pdf", ReceiptPdf);

        var stored = Attachments.Get(id);
        Assert.Equal(ReceiptPdf, stored.Content);
        Assert.Equal(RecordKeeping.Sha256(ReceiptPdf), stored.Info.Sha256);
        Assert.Equal(id, Assert.Single(Attachments.For(AttachedTo.Expense, expense)).Id);
        Assert.True(Attachments.Verify(id));
    }

    [Fact]
    public void FR27_AChangedFileIsDetected()
    {
        // The table refuses updates, so the only way to change stored content is to remove the
        // guard first, which is what an attacker with the file would do. Verify must notice.
        var id = Attachments.Attach(AttachedTo.Expense, Expenses.Record(Binders()), "walmart.pdf", "application/pdf", ReceiptPdf);
        _db.Database.ExecuteSqlRaw("DROP TRIGGER Attachments_NoUpdate;");
        _db.Database.ExecuteSqlRaw("UPDATE Attachments SET Content = x'00' WHERE Id = {0};", id.ToString().ToUpperInvariant());
        _db.ChangeTracker.Clear();
        Assert.False(Attachments.Verify(id));
    }

    [Fact]
    public void FR27_AttachingToAnUnknownRecordIsNotFound()
    {
        Assert.Throws<NotFoundException>(() => Attachments.Attach(AttachedTo.Expense, Guid.NewGuid(), "x.pdf", "application/pdf", ReceiptPdf));
    }

    [Fact]
    public void FR27_AnEmptyFileIsRefused()
    {
        Assert.Throws<ArgumentException>(() => Attachments.Attach(AttachedTo.Expense, Expenses.Record(Binders()), "x.pdf", "application/pdf", []));
    }

    // ---- FR-25 on the new tables ----

    [Fact]
    public void FR25g_TheRecordTablesRefuseBulkChanges()
    {
        var expense = Expenses.Record(Binders());
        Mileage.Log(DriveIn());
        Attachments.Attach(AttachedTo.Expense, expense, "walmart.pdf", "application/pdf", ReceiptPdf);
        Assert.Throws<SqliteException>(() => _db.Drives.ExecuteDelete());
        Assert.Throws<SqliteException>(() => _db.Expenses.ExecuteUpdate(s => s.SetProperty(e => e.Amount, 0m)));
        Assert.Throws<SqliteException>(() => _db.Attachments.ExecuteDelete());
    }

    // ---- NFR-7: backup ----

    [Fact]
    public void NFR7_ABackupIsADatedCopyOfTheWholeLedger()
    {
        Mileage.Log(DriveIn());
        Expenses.Record(Binders());
        var folder = Directory.CreateTempSubdirectory("gigledger-backup-").FullName;
        try
        {
            var path = ((IBackupService)_ledger).Backup(folder);
            Assert.Equal("gigledger-2026-09-25-090000.db", Path.GetFileName(path));

            using var copy = new SqliteConnection($"Data Source={path};Pooling=False");
            copy.Open();
            using var count = copy.CreateCommand();
            count.CommandText = "SELECT (SELECT COUNT(*) FROM Drives) + (SELECT COUNT(*) FROM Expenses);";
            Assert.Equal(2L, count.ExecuteScalar());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void NFR7_ASecondBackupNeverOverwritesTheFirst()
    {
        var folder = Directory.CreateTempSubdirectory("gigledger-backup-").FullName;
        try
        {
            var first = ((IBackupService)_ledger).Backup(folder);
            var second = ((IBackupService)_ledger).Backup(folder);
            Assert.NotEqual(first, second);
            Assert.Equal(2, Directory.GetFiles(folder).Length);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
