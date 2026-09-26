using GigLedger.Core;

namespace GigLedger.Data;

/// <summary>Slice 3b: reports (FR-21, FR-24).</summary>
public sealed partial class LedgerServices : IReportService
{
    PeriodReport IReportService.Report(DateOnly from, DateOnly to) => throw new NotImplementedException();
    public IReadOnlyList<ReportRow> Rows(DateOnly from, DateOnly to) => throw new NotImplementedException();
    public string Csv(DateOnly from, DateOnly to) => throw new NotImplementedException();
}
