using GigLedger.Core;
using Microsoft.EntityFrameworkCore;

namespace GigLedger.Data;

/// <summary>Slice 3b: reports (FR-21, FR-24).</summary>
public sealed partial class LedgerServices : IReportService
{
    PeriodReport IReportService.Report(DateOnly from, DateOnly to) =>
        Reports.Combine(from, to, Rows(from, to).Select(r => r.Summary).ToList());

    /// <summary>Closed shifts that started in the period, by the date on the driver's clock.</summary>
    public IReadOnlyList<ReportRow> Rows(DateOnly from, DateOnly to)
    {
        if (to < from)
            throw new ArgumentOutOfRangeException(nameof(to), to, "The period ends before it starts.");
        var closed = db.ShiftCloses.AsNoTracking().Select(c => c.ShiftId).ToHashSet();
        return db.Shifts.AsNoTracking().AsEnumerable()
            .Where(s => closed.Contains(s.Id))
            .Select(s => (Shift: s, Date: DateOnly.FromDateTime(s.StartedAt.DateTime)))
            .Where(x => x.Date >= from && x.Date <= to)
            .OrderBy(x => x.Shift.StartedAt)
            .Select(x => new ReportRow(x.Date, x.Shift.Platform, Summary(x.Shift.Id)))
            .ToList();
    }

    public string Csv(DateOnly from, DateOnly to) =>
        Reports.ToCsv(((IReportService)this).Report(from, to), Rows(from, to));
}
