using System.Globalization;
using System.Text;

namespace GigLedger.Core;

/// <summary>FR-21: the closed shifts that started in a period, summed. Rates are null when there is no time to divide by.</summary>
public sealed record PeriodReport(
    DateOnly From, DateOnly To,
    int Shifts, int Trips,
    decimal Gross, decimal Tips,
    Result EnergyCost, Result Net,
    decimal ClockHours, decimal TripHours,
    decimal Miles, decimal UnpaidMiles,
    Result? ShiftRate, Result? TripRate);

/// <summary>One shift as a report row.</summary>
public sealed record ReportRow(DateOnly Date, string Platform, ShiftSummary Summary);

public static class Reports
{
    /// <summary>The Monday-to-Sunday week that contains the day (SDD 6.6).</summary>
    public static (DateOnly From, DateOnly To) WeekOf(DateOnly day) => throw new NotImplementedException();

    /// <summary>FR-21: sums shift summaries into one period report.</summary>
    public static PeriodReport Combine(DateOnly from, DateOnly to, IReadOnlyList<ShiftSummary> shifts) => throw new NotImplementedException();

    /// <summary>FR-24: one row per shift and a total row. Invariant culture; money and rates to the cent.</summary>
    public static string ToCsv(PeriodReport total, IReadOnlyList<ReportRow> rows) => throw new NotImplementedException();
}

public interface IReportService
{
    /// <summary>Closed shifts that started on or after from and on or before to.</summary>
    PeriodReport Report(DateOnly from, DateOnly to);
    IReadOnlyList<ReportRow> Rows(DateOnly from, DateOnly to);
    string Csv(DateOnly from, DateOnly to);
}
