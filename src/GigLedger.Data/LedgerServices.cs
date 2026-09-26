using GigLedger.Core;

namespace GigLedger.Data;

/// <summary>
/// The Core service interfaces, over the ledger. The UI and the API both call these
/// (FR-34). They move data in and out; every calculation is delegated to Core.
/// </summary>
public sealed partial class LedgerServices(LedgerContext db, TimeProvider clock)
    : ISettingsService, IOfferService, ITripService, IShiftService, IChargeService
{
    // ---- Home rate (FR-5) ----

    public HomeRate HomeRateOn(DateOnly date)
    {
        var row = db.HomeRates.AsEnumerable()
            .Where(r => r.EffectiveFrom <= date)
            .OrderByDescending(r => r.EffectiveFrom).ThenByDescending(r => r.RecordedAt)
            .FirstOrDefault() ?? throw new InvalidOperationException("No home rate is stored; the schema seed is missing.");
        return new HomeRate(row.PerKwh, row.EffectiveFrom, row.IsPlaceholder);
    }

    public void SetHomeRate(decimal perKwh, DateOnly effectiveFrom)
    {
        if (perKwh < 0)
            throw new ArgumentOutOfRangeException(nameof(perKwh), perKwh, "A rate cannot be negative.");
        db.HomeRates.Add(new HomeRateRow { RecordedAt = clock.GetUtcNow(), PerKwh = perKwh, EffectiveFrom = effectiveFrom });
        db.SaveChanges();
    }

    // ---- Charging (FR-5, FR-8, FR-9) ----

    public Guid Record(ChargeSession session)
    {
        EnergyCalculations.Validate(session);
        var row = new ChargeSessionRow
        {
            RecordedAt = clock.GetUtcNow(),
            At = session.At,
            Odometer = session.Odometer.Value, OdometerGrade = session.Odometer.Grade,
            Kwh = session.Kwh.Value, KwhGrade = session.Kwh.Grade,
            Cost = session.Cost?.Value, CostGrade = session.Cost?.Grade,
            StartSoc = session.StartSoc,
            EndSoc = session.EndSoc,
            Charger = session.Charger,
            Type = session.Type,
            Purpose = session.Purpose,
        };
        db.ChargeSessions.Add(row);
        db.SaveChanges();
        return row.Id;
    }

    public IReadOnlyList<CostedCharge> Between(DateTimeOffset from, DateTimeOffset to) =>
        // Filtered in memory: SQLite cannot compare DateTimeOffset in SQL.
        db.ChargeSessions.AsEnumerable()
            .Where(r => r.At >= from && r.At < to)
            .OrderBy(r => r.At)
            .Select(r => new ChargeSession(
                r.At, new(r.Odometer, r.OdometerGrade), new(r.Kwh, r.KwhGrade),
                r.Cost is { } cost ? new Graded<decimal>(cost, r.CostGrade!.Value) : null,
                r.StartSoc, r.EndSoc, r.Charger, r.Type, r.Purpose))
            .Select(s => new CostedCharge(s, EnergyCalculations.ChargeCost(s, HomeRateOn(DateOnly.FromDateTime(s.At.DateTime)))))
            .ToList();

    EnergyReport IChargeService.Report(DateTimeOffset from, DateTimeOffset to)
    {
        var charges = Between(from, to);
        return new EnergyReport(
            charges.Count,
            EnergyCalculations.MeasuredEfficiency(charges.Select(c => c.Session).ToList()),
            EnergyCalculations.Prices(charges));
    }

    /// <summary>FR-9a: the energy basis from the 30 days of charging before a moment.</summary>
    private EnergyBasis EnergyBefore(DateTimeOffset moment) =>
        EnergyCalculations.ForWindow(Between(moment.AddDays(-30), moment), Get());

    // ---- Tips (FR-6, FR-17) and trip reports (FR-14, FR-16) ----

    public void RecordTip(Guid tripId, decimal amount, DateTimeOffset postedAt)
    {
        FindTrip(tripId);
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "A tip cannot be negative.");
        if (FindTip(tripId) is not null)
            throw new InvalidOperationException($"Trip {tripId} already has its tip; a tip is the whole trip's tip.");
        db.Tips.Add(new TipRow { RecordedAt = clock.GetUtcNow(), TripId = tripId, Amount = amount, AmountGrade = Grade.Stated, PostedAt = postedAt });
        db.SaveChanges();
    }

    TripReport ITripService.Report(Guid tripId)
    {
        var trip = ToStored(FindTrip(tripId));
        var energy = EnergyBefore(FindShift(trip.ShiftId).StartedAt);
        if (trip.Actuals is not { } actuals)
            return new TripReport(trip, null, null, energy);
        return new TripReport(
            trip,
            EnergyCalculations.TripRates(trip.Offer.Pay.Value, trip.Tip?.Value ?? 0m, actuals.Values, energy),
            EnergyCalculations.EstimateError(trip.Offer, actuals.Values),
            energy);
    }

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

    // ---- Offers ----

    public OfferEvaluation Evaluate(Offer offer)
    {
        var settings = Get();
        var forecast = Calculations.ForecastNetPerHour(offer, EnergyBefore(clock.GetUtcNow()));
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

    public IReadOnlyList<StoredTrip> OnShift(Guid shiftId)
    {
        FindShift(shiftId);
        // Ordered in memory: SQLite cannot order by DateTimeOffset in SQL.
        return db.Trips.Where(t => t.ShiftId == shiftId).AsEnumerable()
            .OrderBy(t => t.AcceptedAt)
            .Select(ToStored)
            .ToList();
    }

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
        var tip = FindTip(t.Id);
        return new StoredTrip(t.Id, t.ShiftId, offer, t.AcceptedAt, actuals,
            tip is null ? null : new Graded<decimal>(tip.Amount, tip.AmountGrade));
    }

    // ---- Shifts ----

    public Guid Start(string platform, DateTimeOffset startedAt, Graded<decimal> startOdometer)
    {
        if (Open() is { } open)
            throw new InvalidOperationException($"Shift {open.Id} is still open; end it before starting another.");
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

    public StoredShift? Open()
    {
        var open = db.Shifts.SingleOrDefault(s => !db.ShiftCloses.Any(c => c.ShiftId == s.Id));
        return open is null ? null : ((IShiftService)this).Get(open.Id);
    }

    public ShiftSummary Summary(Guid shiftId)
    {
        var shift = FindShift(shiftId);
        var close = FindClose(shiftId) ?? throw new InvalidOperationException($"Shift {shiftId} is still open.");

        var trips = new List<TripRecord>();
        foreach (var t in db.Trips.Where(t => t.ShiftId == shiftId).ToList())
        {
            var a = FindActuals(t.Id)
                ?? throw new InvalidOperationException($"Trip {t.Id} has no actuals; the shift cannot be summarized yet.");
            trips.Add(new TripRecord(t.Pay, new TripActuals(a.ElapsedMinutes, a.RouteMiles, a.ReturnMiles), FindTip(t.Id)?.Amount ?? 0m));
        }

        var minutes = (int)Math.Round((close.EndedAt - shift.StartedAt).TotalMinutes);
        var span = new ShiftSpan(minutes, shift.StartOdometer, close.EndOdometer);
        return Calculations.SummarizeShift(span, trips, EnergyBefore(shift.StartedAt));
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

    private TipRow? FindTip(Guid tripId) =>
        db.Tips.SingleOrDefault(t => t.TripId == tripId);
}
