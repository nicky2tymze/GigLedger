using GigLedger.Core;

namespace GigLedger.Data;

/// <summary>
/// The Core service interfaces, over the ledger. The UI and the API both call these
/// (FR-34). They move data in and out; every calculation is delegated to Core.
/// </summary>
public sealed class LedgerServices(LedgerContext db, TimeProvider clock)
    : ISettingsService, IOfferService, ITripService, IShiftService
{
    // ---- Settings ----

    public Settings Get()
    {
        // SQLite cannot order by DateTimeOffset in SQL; the table is a handful of rows.
        var current = db.Settings.AsEnumerable().MaxBy(r => r.RecordedAt)
            ?? throw new InvalidOperationException("No settings are stored; the schema seed is missing.");
        return new Settings(current.AcceptThreshold, current.DefaultMilesPerKwh, current.DefaultPricePerKwh);
    }

    public void Set(Settings settings)
    {
        db.Settings.Add(new SettingsRow
        {
            RecordedAt = clock.GetUtcNow(),
            AcceptThreshold = settings.AcceptThreshold,
            DefaultMilesPerKwh = settings.DefaultMilesPerKwh,
            DefaultPricePerKwh = settings.DefaultPricePerKwh,
        });
        db.SaveChanges();
    }

    private static EnergyBasis Energy(Settings s) => EnergyBasis.FromDefaults(s.DefaultMilesPerKwh, s.DefaultPricePerKwh);

    // ---- Offers ----

    public OfferEvaluation Evaluate(Offer offer)
    {
        var settings = Get();
        var forecast = Calculations.ForecastNetPerHour(offer, Energy(settings));
        return new OfferEvaluation(forecast, Calculations.AcceptVerdict(forecast, settings.AcceptThreshold), settings.AcceptThreshold);
    }

    public Guid Accept(Guid shiftId, Offer offer, DateTimeOffset acceptedAt)
    {
        FindShift(shiftId);
        if (FindClose(shiftId) is not null)
            throw new InvalidOperationException($"Shift {shiftId} is closed.");

        var row = new TripRow
        {
            RecordedAt = clock.GetUtcNow(),
            ShiftId = shiftId,
            Pay = offer.Pay.Value, PayGrade = offer.Pay.Grade,
            StatedMiles = offer.StatedMiles.Value, StatedMilesGrade = offer.StatedMiles.Grade,
            Drops = offer.Drops,
            Items = offer.Items,
            EstimatedMinutes = offer.EstimatedMinutes.Value, EstimatedMinutesGrade = offer.EstimatedMinutes.Grade,
            OfferedAt = offer.OfferedAt,
            ReturnMilesOverride = offer.ReturnMilesOverride,
            AcceptedAt = acceptedAt,
        };
        db.Trips.Add(row);
        db.SaveChanges();
        return row.Id;
    }

    // ---- Trips ----

    public void RecordActuals(Guid tripId, GradedActuals actuals)
    {
        FindTrip(tripId);
        if (FindActuals(tripId) is not null)
            throw new InvalidOperationException($"Trip {tripId} already has actuals; a change is a correction.");

        db.TripActuals.Add(new TripActualsRow
        {
            RecordedAt = clock.GetUtcNow(),
            TripId = tripId,
            ElapsedMinutes = actuals.ElapsedMinutes.Value, ElapsedMinutesGrade = actuals.ElapsedMinutes.Grade,
            RouteMiles = actuals.RouteMiles.Value, RouteMilesGrade = actuals.RouteMiles.Grade,
            ReturnMiles = actuals.ReturnMiles.Value, ReturnMilesGrade = actuals.ReturnMiles.Grade,
        });
        db.SaveChanges();
    }

    StoredTrip ITripService.Get(Guid tripId) => ToStored(FindTrip(tripId));

    public IReadOnlyList<StoredTrip> OnShift(Guid shiftId) => throw new NotImplementedException();

    private StoredTrip ToStored(TripRow t)
    {
        var offer = new Offer(
            new(t.Pay, t.PayGrade),
            new(t.StatedMiles, t.StatedMilesGrade),
            t.Drops,
            t.Items,
            new(t.EstimatedMinutes, t.EstimatedMinutesGrade),
            t.OfferedAt,
            t.ReturnMilesOverride);
        var a = FindActuals(t.Id);
        var actuals = a is null ? null : new GradedActuals(
            new(a.ElapsedMinutes, a.ElapsedMinutesGrade),
            new(a.RouteMiles, a.RouteMilesGrade),
            new(a.ReturnMiles, a.ReturnMilesGrade));
        return new StoredTrip(t.Id, t.ShiftId, offer, t.AcceptedAt, actuals);
    }

    // ---- Shifts ----

    public Guid Start(string platform, DateTimeOffset startedAt, Graded<decimal> startOdometer)
    {
        var row = new ShiftRow
        {
            RecordedAt = clock.GetUtcNow(),
            Platform = platform,
            StartedAt = startedAt,
            StartOdometer = startOdometer.Value,
            StartOdometerGrade = startOdometer.Grade,
        };
        db.Shifts.Add(row);
        db.SaveChanges();
        return row.Id;
    }

    public void End(Guid shiftId, DateTimeOffset endedAt, Graded<decimal> endOdometer)
    {
        var shift = FindShift(shiftId);
        if (FindClose(shiftId) is not null)
            throw new InvalidOperationException($"Shift {shiftId} is already closed.");
        if (endedAt <= shift.StartedAt)
            throw new ArgumentOutOfRangeException(nameof(endedAt), endedAt, "The shift must end after it starts.");
        if (endOdometer.Value < shift.StartOdometer)
            throw new ArgumentOutOfRangeException(nameof(endOdometer), endOdometer.Value, "End odometer is below the start reading.");

        db.ShiftCloses.Add(new ShiftCloseRow
        {
            RecordedAt = clock.GetUtcNow(),
            ShiftId = shiftId,
            EndedAt = endedAt,
            EndOdometer = endOdometer.Value,
            EndOdometerGrade = endOdometer.Grade,
        });
        db.SaveChanges();
    }

    StoredShift IShiftService.Get(Guid shiftId)
    {
        var s = FindShift(shiftId);
        var close = FindClose(shiftId);
        return new StoredShift(
            s.Id, s.Platform, s.StartedAt, new(s.StartOdometer, s.StartOdometerGrade),
            close?.EndedAt,
            close is null ? null : new Graded<decimal>(close.EndOdometer, close.EndOdometerGrade));
    }

    public StoredShift? Open() => throw new NotImplementedException();

    public ShiftSummary Summary(Guid shiftId)
    {
        var shift = FindShift(shiftId);
        var close = FindClose(shiftId) ?? throw new InvalidOperationException($"Shift {shiftId} is still open.");

        var trips = new List<TripRecord>();
        foreach (var t in db.Trips.Where(t => t.ShiftId == shiftId).ToList())
        {
            var a = FindActuals(t.Id)
                ?? throw new InvalidOperationException($"Trip {t.Id} has no actuals; the shift cannot be summarized yet.");
            trips.Add(new TripRecord(t.Pay, new TripActuals(a.ElapsedMinutes, a.RouteMiles, a.ReturnMiles)));
        }

        var minutes = (int)Math.Round((close.EndedAt - shift.StartedAt).TotalMinutes);
        var span = new ShiftSpan(minutes, shift.StartOdometer, close.EndOdometer);
        return Calculations.SummarizeShift(span, trips, Energy(Get()));
    }

    // ---- Lookups ----

    private ShiftRow FindShift(Guid id) =>
        db.Shifts.SingleOrDefault(s => s.Id == id) ?? throw new NotFoundException($"No shift {id}.");

    private ShiftCloseRow? FindClose(Guid shiftId) =>
        db.ShiftCloses.SingleOrDefault(c => c.ShiftId == shiftId);

    private TripRow FindTrip(Guid id) =>
        db.Trips.SingleOrDefault(t => t.Id == id) ?? throw new NotFoundException($"No trip {id}.");

    private TripActualsRow? FindActuals(Guid tripId) =>
        db.TripActuals.SingleOrDefault(a => a.TripId == tripId);
}
