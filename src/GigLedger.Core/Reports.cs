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
    public static (DateOnly From, DateOnly To) WeekOf(DateOnly day)
    {
        var sinceMonday = ((int)day.DayOfWeek + 6) % 7; // Monday 0 ... Sunday 6
        var monday = day.AddDays(-sinceMonday);
        return (monday, monday.AddDays(6));
    }

    /// <summary>FR-21: sums shift summaries into one period report.</summary>
    public static PeriodReport Combine(DateOnly from, DateOnly to, IReadOnlyList<ShiftSummary> shifts)
    {
        var gross = shifts.Sum(s => s.Gross);
        var clock = shifts.Sum(s => s.ClockHours);
        var onTrips = shifts.Sum(s => s.TripHours);
        var assumptions = shifts.SelectMany(s => s.Net.Assumptions).Distinct().ToList();
        return new PeriodReport(
            from, to,
            shifts.Count, shifts.Sum(s => s.Trips),
            gross, shifts.Sum(s => s.Tips),
            new Result(shifts.Sum(s => s.EnergyCost.Value), assumptions),
            new Result(shifts.Sum(s => s.Net.Value), assumptions),
            clock, onTrips,
            shifts.Sum(s => s.ShiftMiles), shifts.Sum(s => s.DeadheadMiles),
            clock > 0 ? new Result(gross / clock, []) : null,
            onTrips > 0 ? new Result(gross / onTrips, []) : null);
    }

    /// <summary>FR-24: one row per shift and a total row. Invariant culture; money and rates to the cent.</summary>
    public static string ToCsv(PeriodReport total, IReadOnlyList<ReportRow> rows)
    {
        var csv = new StringBuilder("date,platform,trips,gross,tips,energy_cost,net,clock_hours,trip_hours,miles,unpaid_miles,shift_rate,trip_rate\r\n");
        foreach (var r in rows)
        {
            var s = r.Summary;
            Line(csv, r.Date.ToString("yyyy-MM-dd", Invariant), r.Platform, s.Trips, s.Gross, s.Tips, s.EnergyCost.Value, s.Net.Value,
                s.ClockHours, s.TripHours, s.ShiftMiles, s.DeadheadMiles, s.ShiftRate.Value, s.TripRate?.Value);
        }
        Line(csv, "total", "", total.Trips, total.Gross, total.Tips, total.EnergyCost.Value, total.Net.Value,
            total.ClockHours, total.TripHours, total.Miles, total.UnpaidMiles, total.ShiftRate?.Value, total.TripRate?.Value);
        return csv.ToString();
    }

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static void Line(StringBuilder csv, string date, string platform, int trips, decimal gross, decimal tips,
        decimal energy, decimal net, decimal clock, decimal onTrips, decimal miles, decimal unpaid, decimal? shiftRate, decimal? tripRate)
    {
        string[] fields =
        [
            date, Quote(platform), trips.ToString(Invariant),
            Cents(gross), Cents(tips), Cents(energy), Cents(net),
            Cents(clock), Cents(onTrips), Tenths(miles), Tenths(unpaid),
            shiftRate is { } sr ? Cents(sr) : "", tripRate is { } tr ? Cents(tr) : "",
        ];
        csv.Append(string.Join(',', fields)).Append("\r\n");
    }

    private static string Cents(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero).ToString("0.00", Invariant);
    private static string Tenths(decimal v) => Math.Round(v, 1, MidpointRounding.AwayFromZero).ToString("0.0", Invariant);

    /// <summary>RFC 4180: a field with a comma, quote, or line break is quoted, and its quotes doubled.</summary>
    private static string Quote(string field) =>
        field.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + field.Replace("\"", "\"\"") + "\"" : field;
}

public interface IReportService
{
    /// <summary>Closed shifts that started on or after from and on or before to.</summary>
    PeriodReport Report(DateOnly from, DateOnly to);
    IReadOnlyList<ReportRow> Rows(DateOnly from, DateOnly to);
    string Csv(DateOnly from, DateOnly to);
}
