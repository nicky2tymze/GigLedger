using GigLedger.Core;
using Microsoft.EntityFrameworkCore;

namespace GigLedger.Data;

/// <summary>
/// The Core service interfaces, over the ledger. The UI and the API both call these
/// (FR-34). They move data in and out; every calculation is delegated to Core.
/// </summary>
public sealed partial class LedgerServices(LedgerContext db, TimeProvider clock)
    : ISettingsService, IOfferService, ITripService, IShiftService, IChargeService, IPayoutService, IEntryCheckService
{
    // ---- Home rate (FR-5) ----

    public HomeRate HomeRateOn(DateOnly date)
    {
        // Among current versions (a rate re-entered for the same date supersedes the earlier entry),
        // the one with the latest effective date on or before the day.
        var row = Current(db.HomeRates.AsNoTracking())
            .Where(r => r.EffectiveFrom <= date)
            .MaxBy(r => r.EffectiveFrom) ?? throw new InvalidOperationException("No home rate is stored; the schema seed is missing.");
        return new HomeRate(row.PerKwh, row.EffectiveFrom, row.IsPlaceholder);
    }

    public void SetHomeRate(decimal perKwh, DateOnly effectiveFrom)
    {
        if (perKwh < 0)
            throw new ArgumentOutOfRangeException(nameof(perKwh), perKwh, "A rate cannot be negative.");
        var sameDate = Current(db.HomeRates.AsNoTracking()).SingleOrDefault(r => r.EffectiveFrom == effectiveFrom);
        db.HomeRates.Add(new HomeRateRow { RecordedAt = clock.GetUtcNow(), SupersedesId = sameDate?.Id, PerKwh = perKwh, EffectiveFrom = effectiveFrom });
        db.SaveChanges();
    }

    // ---- Charging (FR-5, FR-8, FR-9) ----

    public Guid Record(ChargeSession session) => AddCharge(session);


    public ImportResult Import(string csv)
    {
        // Parse validates every row before anything is stored (SDD 6.7).
        var sessions = ChargeImport.Parse(csv);

        // Any version counts: a corrected session is still the receipt it came from.
        var stored = db.ChargeSessions.AsNoTracking()
            .Where(r => r.ReceiptNumber != null)
            .Select(r => r.ReceiptNumber!)
            .ToHashSet();

        // FR-37: a charge the battery could not take stops the file, like any row FR-5 refuses.
        var battery = GetLimits().BatteryKwh;
        for (var i = 0; i < sessions.Count; i++)
        {
            try
            {
                EntryChecks.RefuseOverBattery(sessions[i].Kwh.Value, battery);
            }
            catch (ArgumentOutOfRangeException e)
            {
                throw new ArgumentException($"line {i + 2}: {e.Message.Split(" (Parameter")[0]}");
            }
        }

        var fresh = sessions.Where(s => stored.Add(s.ReceiptNumber!)).ToList();
        using var transaction = db.Database.BeginTransaction();
        db.ChargeSessions.AddRange(fresh.Select(s => ToRow(s)));
        db.SaveChanges();
        transaction.Commit();
        return new ImportResult(fresh.Count, sessions.Count - fresh.Count);
    }

    private Guid AddCharge(ChargeSession session, Guid? supersedes = null, string? reason = null)
    {
        EnergyCalculations.Validate(session);
        EntryChecks.RefuseOverBattery(session.Kwh.Value, GetLimits().BatteryKwh);
        var row = ToRow(session, supersedes, reason);
        db.ChargeSessions.Add(row);
        db.SaveChanges();
        return row.Id;
    }

    private ChargeSessionRow ToRow(ChargeSession session, Guid? supersedes = null, string? reason = null) => new()
    {
        RecordedAt = clock.GetUtcNow(),
        SupersedesId = supersedes,
        CorrectionReason = reason,
        At = session.At,
        Odometer = session.Odometer?.Value, OdometerGrade = session.Odometer?.Grade,
        Kwh = session.Kwh.Value, KwhGrade = session.Kwh.Grade,
        Cost = session.Cost?.Value, CostGrade = session.Cost?.Grade,
        StartSoc = session.StartSoc,
        EndSoc = session.EndSoc,
        Charger = session.Charger,
        Type = session.Type,
        Purpose = session.Purpose,
        ReceiptNumber = session.ReceiptNumber,
    };

    private static ChargeSession ToSession(ChargeSessionRow r) => new(
        r.At, r.Odometer is { } odometer ? new Graded<decimal>(odometer, r.OdometerGrade!.Value) : null, new(r.Kwh, r.KwhGrade),
        r.Cost is { } cost ? new Graded<decimal>(cost, r.CostGrade!.Value) : null,
        r.StartSoc, r.EndSoc, r.Charger, r.Type, r.Purpose, r.ReceiptNumber);

    public IReadOnlyList<CostedCharge> Between(DateTimeOffset from, DateTimeOffset to) =>
        // Current versions only (FR-25), filtered in memory: SQLite cannot compare DateTimeOffset in SQL.
        Current(db.ChargeSessions.AsNoTracking())
            .Where(r => r.At >= from && r.At < to)
            .OrderBy(r => r.At)
            .Select(r => (r.Id, Session: ToSession(r)))
            .Select(x => new CostedCharge(x.Session, EnergyCalculations.ChargeCost(x.Session, HomeRateOn(DateOnly.FromDateTime(x.Session.At.DateTime))), x.Id))
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

    public void RecordTip(Guid tripId, decimal amount, DateTimeOffset postedAt, Acknowledgement? acknowledgement = null)
    {
        FindTrip(tripId);
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "A tip cannot be negative.");
        CheckAndMark(MarkedRecord.Tip, tripId, acknowledgement, (EntryLimit.Tip, amount));
        db.Tips.Add(new TipRow { RecordedAt = clock.GetUtcNow(), TripId = tripId, Amount = amount, AmountGrade = Grade.Stated, PostedAt = postedAt });
        db.SaveChanges();
    }

    public void SetPromisedTip(Guid tripId, decimal amount, string? reason = null, Acknowledgement? acknowledgement = null)
    {
        var row = FindTrip(tripId);
        TipAccounting.RefuseImpossiblePromise(row.Pay, amount);
        var previous = CurrentPromisedTip(tripId);
        var why = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        // FR-25: a change carries a reason; so does filling the blank once the shift is closed (the Architect).
        if (why is null && (previous is not null || row.PromisedTip is not null))
            throw new ArgumentException("Changing the tip needs a one-line reason.", nameof(reason));
        if (why is null && FindClose(row.ShiftId) is not null)
            throw new ArgumentException("The shift is closed: adding the tip now needs a one-line reason.", nameof(reason));

        CheckAndMark(MarkedRecord.Trip, tripId, acknowledgement, (EntryLimit.Tip, amount));
        db.PromisedTips.Add(new PromisedTipRow
        {
            RecordedAt = clock.GetUtcNow(),
            SupersedesId = previous?.Id,
            CorrectionReason = why,
            TripId = tripId,
            Amount = amount,
            AmountGrade = Grade.Stated,
        });
        db.SaveChanges();
    }

    /// <summary>The newest promised tip set after accept that nothing supersedes, if any (SDD 6.11).</summary>
    private PromisedTipRow? CurrentPromisedTip(Guid tripId) =>
        Current(db.PromisedTips.AsNoTracking().Where(p => p.TripId == tripId)).SingleOrDefault();

    public void Cancel(Guid tripId, Cancellation cancellation, GradedActuals? actuals = null, decimal? promisedTip = null, Acknowledgement? acknowledgement = null)
    {
        var row = FindTrip(tripId);
        if (FindActuals(tripId) is not null)
            throw new InvalidOperationException($"Trip {tripId} already has actuals, so it was not cancelled.");
        if (db.Cancels.Any(c => c.TripId == tripId))
            throw new InvalidOperationException($"Trip {tripId} is already cancelled.");
        var cancel = CancelRules.Validate(cancellation);

        var paid = 0m;
        if (!cancel.Shopped)
        {
            if (actuals is not null)
                throw new ArgumentException("Cancelled before pickup: no miles or minutes to enter.", nameof(actuals));
        }
        else
        {
            if (actuals is null)
                throw new ArgumentException("A shopped cancel needs the minutes and miles you drove.", nameof(actuals));
            // FR-3a: paid is the pay minus the tip, so the tip must be known.
            var current = ToStored(row).Offer.PromisedTip?.Value;
            if (current is null && promisedTip is null)
                throw new ArgumentException("A shopped cancel pays the pay minus the tip: enter the tip from the offer.", nameof(promisedTip));
            var tip = current ?? promisedTip!.Value;
            TipAccounting.RefuseImpossiblePromise(row.Pay, tip);
            CheckActuals(tripId, actuals, acknowledgement);
            if (current is null)
                db.PromisedTips.Add(new PromisedTipRow
                {
                    RecordedAt = clock.GetUtcNow(), CorrectionReason = "given at cancel",
                    TripId = tripId, Amount = tip, AmountGrade = Grade.Stated,
                });
            db.TripActuals.Add(new TripActualsRow
            {
                RecordedAt = clock.GetUtcNow(),
                TripId = tripId,
                ElapsedMinutes = actuals.ElapsedMinutes.Value, ElapsedMinutesGrade = actuals.ElapsedMinutes.Grade,
                RouteMiles = actuals.RouteMiles.Value, RouteMilesGrade = actuals.RouteMiles.Grade,
                ReturnMiles = actuals.ReturnMiles.Value, ReturnMilesGrade = actuals.ReturnMiles.Grade,
            });
            paid = row.Pay - tip;
        }

        db.Cancels.Add(new CancelRow
        {
            RecordedAt = clock.GetUtcNow(),
            TripId = tripId,
            At = cancel.At,
            By = cancel.By,
            Stage = cancel.Stage,
            Reason = cancel.Reason,
            Note = cancel.Note,
            Paid = paid,
        });
        db.SaveChanges();
    }

    public CancelReport ReportCancels(DateOnly from, DateOnly to)
    {
        // The local date is the date in the cancel's own offset; filtered in memory (4.2).
        var cancels = db.Cancels.AsNoTracking().AsEnumerable()
            .Where(c => DateOnly.FromDateTime(c.At.DateTime) is var day && day >= from && day <= to)
            .Select(c => (Cancel: ToCancel(c), Miles: FindActuals(c.TripId) is { } a ? a.RouteMiles + a.ReturnMiles : 0m))
            .ToList();
        return CancelRules.Report(cancels);
    }

    private static StoredCancel ToCancel(CancelRow c) =>
        new(new Cancellation(c.At, c.By, c.Stage, c.Reason, c.Note), c.Paid);

    public void MarkAllTipsIn(Guid tripId, DateTimeOffset at)
    {
        var trip = ToStored(FindTrip(tripId));
        if (trip.Offer.PromisedTip is null)
            throw new InvalidOperationException($"Trip {tripId} has no promised tip, so its tips are not tracked.");
        if (trip.AllTipsIn)
            throw new InvalidOperationException($"Trip {tripId} is already marked all tips in.");
        if (at < trip.AllTipsInOpensAt)
            throw new InvalidOperationException(
                $"The customer can still change the tip until {trip.AllTipsInOpensAt:yyyy-MM-dd HH:mm}, 24 hours after the trip ended.");
        db.TipsIn.Add(new TipsInRow { RecordedAt = clock.GetUtcNow(), TripId = tripId, At = at });
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
            EnergyCalculations.TripRates(trip.Tips.Base, trip.Tips.Counted, actuals.Values, energy),
            EnergyCalculations.EstimateError(trip.Offer, actuals.Values),
            energy);
    }

    // ---- Settings ----

    public Settings Get()
    {
        // The version nothing supersedes, not the latest timestamp: two in one instant would tie.
        var current = Current(db.Settings.AsNoTracking()).SingleOrDefault()
            ?? throw new InvalidOperationException("No settings are stored; the schema seed is missing.");
        return new Settings(current.AcceptThreshold, current.DefaultMilesPerKwh, current.DefaultPricePerKwh);
    }

    public void Set(Settings settings)
    {
        db.Settings.Add(new SettingsRow
        {
            RecordedAt = clock.GetUtcNow(),
            SupersedesId = Current(db.Settings.AsNoTracking()).Single().Id,
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

    public Guid Accept(Guid shiftId, Offer offer, DateTimeOffset acceptedAt, Acknowledgement? acknowledgement = null)
    {
        FindShift(shiftId);
        if (FindClose(shiftId) is not null)
            throw new InvalidOperationException($"Shift {shiftId} is closed.");
        EntryChecks.RefuseNegative(offer.Pay.Value, "Pay");
        if (offer.PromisedTip is { } declinedPromise)
            TipAccounting.RefuseImpossiblePromise(offer.Pay.Value, declinedPromise.Value);
        if (offer.PromisedTip is { } promised)
            TipAccounting.RefuseImpossiblePromise(offer.Pay.Value, promised.Value);

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
            PromisedTip = offer.PromisedTip?.Value, PromisedTipGrade = offer.PromisedTip?.Grade,
        };
        CheckAndMark(MarkedRecord.Trip, row.Id, acknowledgement, OfferChecks(offer));
        db.Trips.Add(row);
        db.SaveChanges();
        return row.Id;
    }

    // ---- Declines (FR-2a, FR-21a) ----

    public Guid Decline(Guid shiftId, Offer offer, IReadOnlyList<DeclineReason> reasons, string? note, DateTimeOffset declinedAt, Acknowledgement? acknowledgement = null)
    {
        FindShift(shiftId);
        if (FindClose(shiftId) is not null)
            throw new InvalidOperationException($"Shift {shiftId} is closed; a decline needs an open shift.");
        var (ordered, trimmed) = DeclineRules.Validate(reasons, note);
        EntryChecks.RefuseNegative(offer.Pay.Value, "Pay");

        // The forecast and verdict at this moment (FR-2a), kept as they were.
        var evaluation = Evaluate(offer);
        var row = new DeclineRow
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
            PromisedTip = offer.PromisedTip?.Value, PromisedTipGrade = offer.PromisedTip?.Grade,
            DeclinedAt = declinedAt,
            ForecastNetPerHour = evaluation.Forecast.Value,
            ForecastAssumptions = string.Join("\n", evaluation.Forecast.Assumptions.Select(a => $"{a.Input}: {a.Source}")),
            Verdict = evaluation.Verdict,
            Threshold = evaluation.Threshold,
            Reasons = string.Join(",", ordered),
            Note = trimmed,
        };
        CheckAndMark(MarkedRecord.Decline, row.Id, acknowledgement, OfferChecks(offer));
        db.Declines.Add(row);
        db.SaveChanges();
        return row.Id;
    }

    public IReadOnlyList<StoredDecline> DeclinesOnShift(Guid shiftId)
    {
        FindShift(shiftId);
        // Ordered in memory: SQLite cannot order by DateTimeOffset in SQL.
        return db.Declines.AsNoTracking().Where(d => d.ShiftId == shiftId).AsEnumerable()
            .OrderBy(d => d.DeclinedAt)
            .Select(ToStored)
            .ToList();
    }

    public DeclineReport ReportDeclines(DateOnly from, DateOnly to)
    {
        // The local date is the date in the decline's own offset; filtered in memory (4.2).
        var inRange = db.Declines.AsNoTracking().AsEnumerable()
            .Where(d => DateOnly.FromDateTime(d.DeclinedAt.DateTime) is var day && day >= from && day <= to)
            .Select(ToStored)
            .ToList();
        return DeclineRules.Report(inRange);
    }

    private static StoredDecline ToStored(DeclineRow d)
    {
        var offer = new Offer(
            new(d.Pay, d.PayGrade),
            new(d.StatedMiles, d.StatedMilesGrade),
            d.Drops,
            d.Items,
            new(d.EstimatedMinutes, d.EstimatedMinutesGrade),
            d.OfferedAt,
            d.ReturnMilesOverride,
            d.PromisedTip is { } promised ? new Graded<decimal>(promised, d.PromisedTipGrade!.Value) : null);
        var assumptions = d.ForecastAssumptions
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(": ", 2))
            .Select(parts => new Assumption(parts[0], parts[1]))
            .ToList();
        var reasons = d.Reasons.Split(',').Select(Enum.Parse<DeclineReason>).ToList();
        return new StoredDecline(d.Id, d.ShiftId, offer, d.DeclinedAt,
            new Result(d.ForecastNetPerHour, assumptions), d.Verdict, d.Threshold, reasons, d.Note);
    }

    // ---- Trips ----

    public void RecordActuals(Guid tripId, GradedActuals actuals, Acknowledgement? acknowledgement = null)
    {
        FindTrip(tripId);
        if (FindActuals(tripId) is not null)
            throw new InvalidOperationException($"Trip {tripId} already has actuals; a change is a correction.");
        CheckActuals(tripId, actuals, acknowledgement);

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
            t.ReturnMilesOverride,
            CurrentPromisedTip(t.Id) is { } set ? new Graded<decimal>(set.Amount, set.AmountGrade)
                : t.PromisedTip is { } promised ? new Graded<decimal>(promised, t.PromisedTipGrade!.Value) : null);
        var a = FindActuals(t.Id);
        var actuals = a is null ? null : new GradedActuals(
            new(a.ElapsedMinutes, a.ElapsedMinutesGrade),
            new(a.RouteMiles, a.RouteMilesGrade),
            new(a.ReturnMiles, a.ReturnMilesGrade));
        // FR-6: every posted tip, summed; null when none has posted.
        var tips = db.Tips.AsNoTracking().Where(x => x.TripId == t.Id).ToList();
        var posted = tips.Count == 0 ? (Graded<decimal>?)null : new Graded<decimal>(tips.Sum(x => x.Amount), Grade.Stated);
        var cancel = db.Cancels.AsNoTracking().SingleOrDefault(c => c.TripId == t.Id);
        return new StoredTrip(t.Id, t.ShiftId, offer, t.AcceptedAt, actuals, posted, db.TipsIn.Any(x => x.TripId == t.Id),
            cancel is null ? null : ToCancel(cancel));
    }

    // ---- Shifts ----

    public Guid Start(string platform, DateTimeOffset startedAt, Graded<decimal> startOdometer)
    {
        if (Open() is { } open)
            throw new InvalidOperationException($"Shift {open.Id} is still open; end it before starting another.");
        EntryChecks.RefuseFutureStart(startedAt, clock.GetUtcNow());
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

    public void End(Guid shiftId, DateTimeOffset endedAt, Graded<decimal> endOdometer, Acknowledgement? acknowledgement = null)
    {
        var shift = FindShift(shiftId);
        if (FindClose(shiftId) is not null)
            throw new InvalidOperationException($"Shift {shiftId} is already closed.");
        if (endedAt <= shift.StartedAt)
            throw new ArgumentOutOfRangeException(nameof(endedAt), endedAt, "The shift must end after it starts.");
        if (endOdometer.Value < shift.StartOdometer)
            throw new ArgumentOutOfRangeException(nameof(endOdometer), endOdometer.Value, "End odometer is below the start reading.");
        EntryChecks.RefuseOverlongShift(shift.StartedAt, endedAt);
        CheckAndMark(MarkedRecord.ShiftClose, shiftId, acknowledgement, (EntryLimit.ShiftLength, EntryChecks.ShiftHours(shift.StartedAt, endedAt)));

        db.ShiftCloses.Add(new ShiftCloseRow
        {
            RecordedAt = clock.GetUtcNow(),
            ShiftId = shiftId,
            EndedAt = endedAt,
            EndOdometer = endOdometer.Value,
            EndOdometerGrade = endOdometer.Grade,
        });
        db.SaveChanges();

        // FR-26a: the shift's odometer span is a business drive. A shift that went nowhere logs none.
        if (endOdometer.Value > shift.StartOdometer)
            AddDrive(new Drive(
                DateOnly.FromDateTime(shift.StartedAt.DateTime),
                new(shift.StartOdometer, shift.StartOdometerGrade),
                endOdometer,
                Purpose.Work,
                $"{shift.Platform} delivery shift"), shiftId);
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
            // SDD 6.12: a not-shopped cancel adds nothing; its time is the shift's unpaid time.
            if (db.Cancels.Any(c => c.TripId == t.Id && c.Stage == CancelStage.BeforePickup)) continue;
            var a = FindActuals(t.Id)
                ?? throw new InvalidOperationException($"Trip {t.Id} has no actuals; the shift cannot be summarized yet.");
            // SDD 6.11: the base and the tip counted in gross, so a posted tip is never added on top of pay.
            var tip = ToStored(t).Tips;
            trips.Add(new TripRecord(tip.Base, new TripActuals(a.ElapsedMinutes, a.RouteMiles, a.ReturnMiles), tip.Counted));
        }

        var minutes = (int)Math.Round((close.EndedAt - shift.StartedAt).TotalMinutes);
        var span = new ShiftSpan(minutes, shift.StartOdometer, close.EndOdometer);
        return Calculations.SummarizeShift(span, trips, EnergyBefore(shift.StartedAt));
    }

    /// <summary>FR-37 refusals, then speed and trip length under one acknowledgement (SDD 6.10).</summary>
    private void CheckActuals(Guid tripId, GradedActuals actuals, Acknowledgement? acknowledgement)
    {
        EntryChecks.RefuseImpossibleActuals(actuals.Values);
        CheckAndMark(MarkedRecord.TripActuals, tripId, acknowledgement,
            (EntryLimit.Speed, EntryChecks.SpeedMph(actuals.Values)),
            (EntryLimit.TripLength, actuals.RouteMiles.Value + actuals.ReturnMiles.Value));
    }

    // ---- Lookups ----

    private ShiftRow FindShift(Guid id) =>
        db.Shifts.SingleOrDefault(s => s.Id == id) ?? throw new NotFoundException($"No shift {id}.");

    private ShiftCloseRow? FindClose(Guid shiftId) =>
        db.ShiftCloses.SingleOrDefault(c => c.ShiftId == shiftId);

    private TripRow FindTrip(Guid id) =>
        db.Trips.SingleOrDefault(t => t.Id == id) ?? throw new NotFoundException($"No trip {id}.");

    private TripActualsRow? FindActuals(Guid tripId) =>
        Current(db.TripActuals.AsNoTracking().Where(a => a.TripId == tripId)).SingleOrDefault();

    /// <summary>The values an offer is checked on at accept or decline: pay, and the promised tip if any.</summary>
    private static (EntryLimit, decimal)[] OfferChecks(Offer offer) =>
        offer.PromisedTip is { } promised
            ? [(EntryLimit.Pay, offer.Pay.Value), (EntryLimit.Tip, promised.Value)]
            : [(EntryLimit.Pay, offer.Pay.Value)];
}
