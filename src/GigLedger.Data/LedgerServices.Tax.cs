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
        var platforms = db.Shifts.AsNoTracking().ToDictionary(s => s.Id, s => s.Platform);
        var trips = db.Trips.AsNoTracking().ToList();
        var gross = new Dictionary<string, decimal>();
        foreach (var t in trips.Where(t => t.AcceptedAt.DateTime.Year == year))
            gross[platforms[t.ShiftId]] = gross.GetValueOrDefault(platforms[t.ShiftId]) + t.Pay;
        var shiftOfTrip = trips.ToDictionary(t => t.Id, t => t.ShiftId);
        foreach (var tip in db.Tips.AsNoTracking().AsEnumerable().Where(t => t.PostedAt.DateTime.Year == year))
        {
            var platform = platforms[shiftOfTrip[tip.TripId]];
            gross[platform] = gross.GetValueOrDefault(platform) + tip.Amount;
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
            RateFor(year));
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

    public IReadOnlyList<decimal> RecordedByMonth(int year, string platform)
    {
        var months = new decimal[12];
        var shifts = db.Shifts.AsNoTracking().Where(s => s.Platform == platform).Select(s => s.Id).ToHashSet();
        var trips = db.Trips.AsNoTracking().AsEnumerable().Where(t => shifts.Contains(t.ShiftId)).ToList();
        foreach (var t in trips.Where(t => t.AcceptedAt.DateTime.Year == year))
            months[t.AcceptedAt.DateTime.Month - 1] += t.Pay;
        var tripIds = trips.Select(t => t.Id).ToHashSet();
        foreach (var tip in db.Tips.AsNoTracking().AsEnumerable().Where(t => tripIds.Contains(t.TripId) && t.PostedAt.DateTime.Year == year))
            months[tip.PostedAt.DateTime.Month - 1] += tip.Amount;
        return months;
    }

    public Reconciliation? Reconcile(int year, string platform)
    {
        if (CurrentForm(year, platform) is not { } row) return null;
        var monthly = row.Monthly?.Split(',').Select(m => decimal.Parse(m, CultureInfo.InvariantCulture)).ToList();
        return Tax.Reconcile(new PlatformForm(row.Year, row.Platform, row.Form, row.AnnualTotal, monthly), RecordedByMonth(year, platform));
    }
}
