using System.Globalization;
using GigLedger.Core;
using Microsoft.EntityFrameworkCore;

namespace GigLedger.Data;

/// <summary>Slice 3b: the tax summary and reconciliation (FR-29 to FR-31).</summary>
public sealed partial class LedgerServices : ITaxService
{
    public void SetMileageRate(int year, decimal perMile)
    {
        Tax.Validate(new MileageRate(year, perMile));
        var current = CurrentRate(year);
        db.MileageRates.Add(new MileageRateRow { RecordedAt = clock.GetUtcNow(), SupersedesId = current?.Id, Year = year, PerMile = perMile });
        db.SaveChanges();
    }

    /// <summary>
    /// The rate in force for the year (FR-30): the one nothing supersedes. Not "the latest
    /// timestamp", which two entries in the same instant would tie.
    /// </summary>
    public MileageRate? RateFor(int year) =>
        CurrentRate(year) is { } row ? new MileageRate(row.Year, row.PerMile) : null;

    private MileageRateRow? CurrentRate(int year) =>
        Current(db.MileageRates.AsNoTracking().Where(r => r.Year == year)).SingleOrDefault();

    private PlatformFormRow? CurrentForm(int year, string platform) =>
        Current(db.PlatformForms.AsNoTracking().Where(f => f.Year == year && f.Platform == platform)).SingleOrDefault();

    TaxSummary ITaxService.Summary(int year)
    {
        // Gross per platform from one source each (FR-31): imported payouts, or else logged trips.
        var gross = new Dictionary<string, decimal>();
        var grossFrom = new Dictionary<string, PaymentSource>();
        var platforms = db.Shifts.AsNoTracking().Select(s => s.Platform)
            .Concat(db.Payouts.AsNoTracking().Select(p => p.Platform))
            .Distinct().ToList();
        foreach (var platform in platforms)
        {
            var (months, source, any) = Payments(year, platform);
            if (!any) continue;
            gross[platform] = months.Sum();
            grossFrom[platform] = source;
        }

        // All charging in the year: the actual method applies the business share to it (SDD 6.6).
        var charges = Current(db.ChargeSessions.AsNoTracking())
            .Where(r => r.At.DateTime.Year == year)
            .Select(ToSession)
            .Select(s => EnergyCalculations.ChargeCost(s, HomeRateOn(DateOnly.FromDateTime(s.At.DateTime))))
            .ToList();
        var charging = new Result(charges.Sum(c => c.Value), charges.SelectMany(c => c.Assumptions).Distinct().ToList());

        return Tax.Summarize(
            year, gross,
            ((IMileageService)this).Totals(year),
            charging,
            ((IExpenseService)this).Year(year).Select(e => e.Expense).ToList(),
            RateFor(year),
            grossFrom);
    }

    public void RecordForm(PlatformForm form)
    {
        Tax.Validate(form);
        db.PlatformForms.Add(new PlatformFormRow
        {
            RecordedAt = clock.GetUtcNow(),
            SupersedesId = CurrentForm(form.Year, form.Platform)?.Id,
            Year = form.Year,
            Platform = form.Platform,
            Form = form.Form,
            AnnualTotal = form.AnnualTotal,
            Monthly = form.Monthly is null ? null : string.Join(",", form.Monthly.Select(m => m.ToString(CultureInfo.InvariantCulture))),
        });
        db.SaveChanges();
    }

    public IReadOnlyList<decimal> RecordedByMonth(int year, string platform) => Payments(year, platform).Months;

    /// <summary>
    /// SDD 6.6: a platform's payments in a year by month, from one source. The imported payouts
    /// where any exist for the year, by transaction month; otherwise the logged trips (SRS 0.8 FR-31).
    /// Never both: the export already holds every logged trip.
    /// </summary>
    private (decimal[] Months, PaymentSource Source, bool Any) Payments(int year, string platform)
    {
        var months = new decimal[12];
        var payouts = Current(db.Payouts.AsNoTracking().Where(p => p.Platform == platform))
            .Where(p => p.At.Year == year)
            .ToList();
        if (payouts.Count > 0)
        {
            foreach (var p in payouts)
                months[p.At.Month - 1] += p.Amount;
            return (months, PaymentSource.ImportedPayouts, true);
        }

        var shifts = db.Shifts.AsNoTracking().Where(s => s.Platform == platform).Select(s => s.Id).ToHashSet();
        var trips = db.Trips.AsNoTracking().AsEnumerable().Where(t => shifts.Contains(t.ShiftId)).ToList();
        // SRS 0.8 FR-31: a trip's gross in the month accepted; on a tracked trip with all tips in, its base
        // in the month accepted and each posted tip in the month it posted. A posted tip on an untracked or
        // pending trip is inside the pay already, so it adds nothing.
        var allIn = db.TipsIn.AsNoTracking().Select(x => x.TripId).ToHashSet();
        var any = false;
        foreach (var t in trips)
        {
            // The current promised tip: one set after accept wins over the one stored at accept.
            var promised = ToStored(t).Offer.PromisedTip?.Value;
            var tracked = promised is not null && allIn.Contains(t.Id);
            if (t.AcceptedAt.DateTime.Year == year)
            {
                months[t.AcceptedAt.DateTime.Month - 1] += tracked ? t.Pay - promised!.Value : t.Pay;
                any = true;
            }
            if (!tracked) continue;
            // A tip that posts in January for a December trip is still that year's income.
            foreach (var tip in db.Tips.AsNoTracking().Where(x => x.TripId == t.Id).AsEnumerable().Where(x => x.PostedAt.DateTime.Year == year))
            {
                months[tip.PostedAt.DateTime.Month - 1] += tip.Amount;
                any = true;
            }
        }
        return (months, PaymentSource.LoggedTrips, any);
    }

    public Reconciliation? Reconcile(int year, string platform)
    {
        if (CurrentForm(year, platform) is not { } row) return null;
        var monthly = row.Monthly?.Split(',').Select(m => decimal.Parse(m, CultureInfo.InvariantCulture)).ToList();
        var (months, source, _) = Payments(year, platform);
        return Tax.Reconcile(new PlatformForm(row.Year, row.Platform, row.Form, row.AnnualTotal, monthly), months, source);
    }
}
