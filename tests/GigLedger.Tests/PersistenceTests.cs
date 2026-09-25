using GigLedger.Core;
using GigLedger.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GigLedger.Tests;

/// <summary>
/// Slice 1 storage and service tests, written from SRS 0.2 and SDD 0.2 before the code.
/// Each test gets its own in-memory SQLite database, migrated to the current schema.
/// </summary>
public sealed class PersistenceTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 2, 6, 57, 0, TimeSpan.FromHours(-5));

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly LedgerContext _db;
    private readonly LedgerServices _ledger;

    public PersistenceTests()
    {
        _connection.Open();
        _db = LedgerDatabase.Open(_connection);
        _ledger = new LedgerServices(_db, TimeProvider.System);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private IShiftService Shifts => _ledger;
    private ITripService Trips => _ledger;

    private static Offer Run4Offer() => new(
        Pay: new(34.89m, Grade.Stated),
        StatedMiles: new(6.6m, Grade.Stated),
        Drops: 2,
        Items: 32,
        EstimatedMinutes: new(58, Grade.Stated),
        OfferedAt: T0.AddMinutes(1));

    private Guid StartedShift() => Shifts.Start("Spark", T0, new(17_000m, Grade.Measured));

    /// <summary>A fresh context on the same database, so reads come from the file, not the cache.</summary>
    private LedgerContext Reopen() =>
        new(new DbContextOptionsBuilder<LedgerContext>().UseSqlite(_connection).Options);

    // ---- FR-1, FR-2: capture ----

    [Fact]
    public void FR1_AcceptedOfferIsStoredWithEveryField()
    {
        var shift = StartedShift();
        var id = _ledger.Accept(shift, Run4Offer(), T0.AddMinutes(2));

        var trip = Trips.Get(id);
        Assert.Equal(shift, trip.ShiftId);
        Assert.Equal(Run4Offer(), trip.Offer);
        Assert.Equal(T0.AddMinutes(2), trip.AcceptedAt);
        Assert.Null(trip.Actuals);
    }

    [Fact]
    public void FR2_EvaluatingAnOfferStoresNothing()
    {
        StartedShift();
        var before = _db.Trips.Count();

        var evaluation = _ledger.Evaluate(Run4Offer());

        Assert.Equal(before, _db.Trips.Count());
        Assert.Equal(25.00m, evaluation.Threshold);
        Assert.True(evaluation.Forecast.RestsOnDefault);
    }

    [Fact]
    public void FR11_ServiceForecastIsCoresForecast()
    {
        // FR-34: the service adds no arithmetic of its own.
        var expected = Calculations.ForecastNetPerHour(Run4Offer(), EnergyBasis.FromDefaults(4.0m, 0.69m));
        var evaluation = _ledger.Evaluate(Run4Offer());
        Assert.Equal(expected.Value, evaluation.Forecast.Value);
        Assert.Equal(Calculations.AcceptVerdict(expected, 25.00m), evaluation.Verdict);
    }

    [Fact]
    public void FR2_AcceptingOnAnUnknownShiftIsNotFound()
    {
        // Not found is its own refusal, so the API can answer 404 rather than 409.
        Assert.Throws<NotFoundException>(() => _ledger.Accept(Guid.NewGuid(), Run4Offer(), T0));
    }

    [Fact]
    public void FR3_AnUnknownTripIsNotFound()
    {
        Assert.Throws<NotFoundException>(() => Trips.Get(Guid.NewGuid()));
    }

    // ---- FR-3: actuals ----

    [Fact]
    public void FR3_ActualsAreRecordedOnceWithTheirGrades()
    {
        var trip = _ledger.Accept(StartedShift(), Run4Offer(), T0.AddMinutes(2));
        var actuals = new GradedActuals(new(55, Grade.Entered), new(6.4m, Grade.Measured), new(5.7m, Grade.Entered));

        Trips.RecordActuals(trip, actuals);

        Assert.Equal(actuals, Trips.Get(trip).Actuals);
        Assert.Throws<InvalidOperationException>(() => Trips.RecordActuals(trip, actuals));
    }

    // ---- FR-4: shifts ----

    [Fact]
    public void FR4_ShiftStartAndEndAreRecorded()
    {
        var id = StartedShift();
        Shifts.End(id, T0.AddMinutes(249), new(17_043.2m, Grade.Measured));

        var shift = Shifts.Get(id);
        Assert.Equal("Spark", shift.Platform);
        Assert.Equal(T0, shift.StartedAt);
        Assert.Equal(new Graded<decimal>(17_000m, Grade.Measured), shift.StartOdometer);
        Assert.Equal(T0.AddMinutes(249), shift.EndedAt);
        Assert.Equal(new Graded<decimal>(17_043.2m, Grade.Measured), shift.EndOdometer);
    }

    [Fact]
    public void FR4_AShiftClosesOnce()
    {
        var id = StartedShift();
        Shifts.End(id, T0.AddMinutes(60), new(17_010m, Grade.Measured));
        Assert.Throws<InvalidOperationException>(() => Shifts.End(id, T0.AddMinutes(90), new(17_020m, Grade.Measured)));
    }

    [Fact]
    public void FR4_EndOdometerBelowStartIsRejectedAtEntry()
    {
        var id = StartedShift();
        Assert.Throws<ArgumentOutOfRangeException>(() => Shifts.End(id, T0.AddMinutes(60), new(16_999m, Grade.Measured)));
        Assert.Null(Shifts.Get(id).EndedAt);
    }

    [Fact]
    public void FR4_EndTimeBeforeStartIsRejectedAtEntry()
    {
        var id = StartedShift();
        Assert.Throws<ArgumentOutOfRangeException>(() => Shifts.End(id, T0.AddMinutes(-1), new(17_010m, Grade.Measured)));
    }

    [Fact]
    public void FR4_OpenIsTheShiftThatHasNotEnded()
    {
        Assert.Null(Shifts.Open());
        var id = StartedShift();
        Assert.Equal(id, Shifts.Open()!.Id);
        Shifts.End(id, T0.AddMinutes(60), new(17_010m, Grade.Measured));
        Assert.Null(Shifts.Open());
    }

    [Fact]
    public void FR4_OnlyOneShiftIsOpenAtATime()
    {
        StartedShift();
        Assert.Throws<InvalidOperationException>(() => StartedShift());
    }

    [Fact]
    public void FR1_TripsOnAShiftComeBackInTheOrderAccepted()
    {
        var shift = StartedShift();
        var first = _ledger.Accept(shift, Run4Offer(), T0.AddMinutes(2));
        var second = _ledger.Accept(shift, Run4Offer() with { Items = 3 }, T0.AddMinutes(70));
        Assert.Equal([first, second], Trips.OnShift(shift).Select(t => t.Id));
    }

    [Fact]
    public void FR1_TripsOnAnUnknownShiftIsNotFound()
    {
        Assert.Throws<NotFoundException>(() => Trips.OnShift(Guid.NewGuid()));
    }

    // ---- FR-20 through the service ----

    [Fact]
    public void FR20_SummaryOfAStoredShiftIsCoresSummary()
    {
        var shift = StartedShift();
        var trip = _ledger.Accept(shift, Run4Offer(), T0.AddMinutes(2));
        Trips.RecordActuals(trip, new(new(55, Grade.Entered), new(6.4m, Grade.Measured), new(5.7m, Grade.Entered)));
        Shifts.End(shift, T0.AddMinutes(90), new(17_012.1m, Grade.Measured));

        var expected = Calculations.SummarizeShift(
            new ShiftSpan(90, 17_000m, 17_012.1m),
            [new TripRecord(34.89m, new TripActuals(55, 6.4m, 5.7m))],
            EnergyBasis.FromDefaults(4.0m, 0.69m));

        var summary = Shifts.Summary(shift);
        Assert.Equal(expected.Gross, summary.Gross);
        Assert.Equal(expected.ShiftRate.Value, summary.ShiftRate.Value);
        Assert.Equal(expected.TripRate!.Value, summary.TripRate!.Value);
        Assert.Equal(expected.DeadheadMiles, summary.DeadheadMiles);
        Assert.Equal(expected.Net.Value, summary.Net.Value);
    }

    [Fact]
    public void FR20_AnOpenShiftHasNoSummaryYet()
    {
        Assert.Throws<InvalidOperationException>(() => Shifts.Summary(StartedShift()));
    }

    [Fact]
    public void FR20_ATripWithoutActualsBlocksTheSummary()
    {
        // A summary that silently skipped an unfinished trip would understate the shift.
        var shift = StartedShift();
        _ledger.Accept(shift, Run4Offer(), T0.AddMinutes(2));
        Shifts.End(shift, T0.AddMinutes(90), new(17_012.1m, Grade.Measured));
        Assert.Throws<InvalidOperationException>(() => Shifts.Summary(shift));
    }

    // ---- Settings ----

    [Fact]
    public void Settings_StartAtTheConfirmedDefaults()
    {
        Assert.Equal(Settings.Initial, _ledger.Get());
    }

    [Fact]
    public void Settings_ANewVersionAppliesAndTheOldOneStays()
    {
        _ledger.Set(new Settings(28.00m, 4.0m, 0.69m));
        Assert.Equal(28.00m, _ledger.Get().AcceptThreshold);
        Assert.Equal(28.00m, _ledger.Evaluate(Run4Offer()).Threshold);
        Assert.Equal(2, _db.Settings.Count()); // the initial version and the new one
    }

    // ---- FR-7, NFR-5, NFR-6: what goes in comes out ----

    [Fact]
    public void FR7_GradesSurviveTheDatabase()
    {
        var id = StartedShift();
        using var fresh = Reopen();
        Assert.Equal(Grade.Measured, fresh.Shifts.Single(s => s.Id == id).StartOdometerGrade);
    }

    [Fact]
    public void NFR5_DecimalsSurviveSqliteExactly()
    {
        // SQLite has no decimal type; EF stores text. These must come back to the last digit.
        var offer = Run4Offer() with { Pay = new(12_345.6789m, Grade.Stated), StatedMiles = new(0.1725m, Grade.Stated) };
        var id = _ledger.Accept(StartedShift(), offer, T0);
        using var fresh = Reopen();
        var row = fresh.Trips.Single(t => t.Id == id);
        Assert.Equal(12_345.6789m, row.Pay);
        Assert.Equal(0.1725m, row.StatedMiles);
    }

    [Fact]
    public void NFR6_TimesKeepTheirOffset()
    {
        var id = StartedShift();
        using var fresh = Reopen();
        var started = fresh.Shifts.Single(s => s.Id == id).StartedAt;
        Assert.Equal(T0, started);
        Assert.Equal(TimeSpan.FromHours(-5), started.Offset);
    }

    // ---- FR-25 structure: nothing is overwritten or deleted (SDD 5.3) ----

    [Fact]
    public void FR25a_UpdatingAStoredRowIsRefused()
    {
        var id = _ledger.Accept(StartedShift(), Run4Offer(), T0);
        var row = _db.Trips.Single(t => t.Id == id);
        row.Pay = 99m;
        Assert.Throws<InvalidOperationException>(() => _db.SaveChanges());
    }

    [Fact]
    public void FR25b_DeletingAStoredRowIsRefused()
    {
        var id = _ledger.Accept(StartedShift(), Run4Offer(), T0);
        _db.Trips.Remove(_db.Trips.Single(t => t.Id == id));
        Assert.Throws<InvalidOperationException>(() => _db.SaveChanges());
    }

    [Fact]
    public void FR25c_BulkDeleteIsRefusedByTheDatabaseItself()
    {
        // ExecuteDelete goes straight to SQL and never passes through SaveChanges,
        // so only a guard inside the database can stop it.
        _ledger.Accept(StartedShift(), Run4Offer(), T0);
        Assert.Throws<SqliteException>(() => _db.Trips.ExecuteDelete());
        Assert.Equal(1, _db.Trips.Count());
    }

    [Fact]
    public void FR25d_BulkUpdateIsRefusedByTheDatabaseItself()
    {
        _ledger.Accept(StartedShift(), Run4Offer(), T0);
        Assert.Throws<SqliteException>(() => _db.Trips.ExecuteUpdate(s => s.SetProperty(t => t.Pay, 0m)));
        Assert.Equal(34.89m, _db.Trips.AsNoTracking().Single().Pay);
    }
}
