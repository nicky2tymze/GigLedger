using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bunit;
using GigLedger.Core;
using GigLedger.Data;
using GigLedger.Web;
using GigLedger.Web.Components.Pages;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace GigLedger.Tests;

/// <summary>Slice 3b pass 1, reports and CSV (FR-21, FR-24), written before the code.</summary>
public sealed class ReportTests : TestContext
{
    private static readonly DateTimeOffset Friday = new(2026, 9, 25, 6, 0, 0, TimeSpan.FromHours(-5));
    private static readonly EnergyBasis Defaults = EnergyBasis.FromDefaults(4.0m, 0.69m);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly LedgerServices _ledger;
    private readonly TestClock _clock = new(Friday);

    public ReportTests()
    {
        _connection.Open();
        _ledger = new LedgerServices(LedgerDatabase.Open(_connection), _clock);
        Services.AddSingleton<TimeProvider>(_clock);
        Services.AddSingleton<IReportService>(_ledger);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }

    private IShiftService Shifts => _ledger;
    private ITripService Trips => _ledger;
    private IReportService Reporting => _ledger;

    private static decimal Cents(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    // Two shifts from the Slice 1 tests: one with two trips, one with none.
    private static readonly ShiftSummary Busy = Calculations.SummarizeShift(
        new ShiftSpan(300, 1000m, 1050m),
        [new TripRecord(40m, new TripActuals(60, 10m, 8m)), new TripRecord(60m, new TripActuals(120, 15m, 12m))],
        Defaults);
    private static readonly ShiftSummary Idle = Calculations.SummarizeShift(new ShiftSpan(60, 1050m, 1062m), [], Defaults);

    // ---- Core ----

    [Theory]
    [InlineData("2026-09-25", "2026-09-21", "2026-09-27")] // Friday
    [InlineData("2026-09-21", "2026-09-21", "2026-09-27")] // Monday starts the week
    [InlineData("2026-09-27", "2026-09-21", "2026-09-27")] // Sunday ends it
    public void FR21_AWeekRunsMondayToSunday(string day, string from, string to)
    {
        Assert.Equal((DateOnly.Parse(from), DateOnly.Parse(to)), Reports.WeekOf(DateOnly.Parse(day)));
    }

    [Fact]
    public void FR21_APeriodSumsItsShifts()
    {
        var r = Reports.Combine(new(2026, 9, 21), new(2026, 9, 27), [Busy, Idle]);
        Assert.Equal((2, 2), (r.Shifts, r.Trips));
        Assert.Equal(100m, r.Gross);
        Assert.Equal(6m, r.ClockHours);
        Assert.Equal(3m, r.TripHours);
        Assert.Equal(62m, r.Miles);
        Assert.Equal(37m, r.UnpaidMiles);           // 25 on the busy shift, all 12 on the idle one
        Assert.Equal(10.695m, r.EnergyCost.Value);  // 62 x 0.1725
        Assert.Equal(89.305m, r.Net.Value);
        Assert.Equal(16.67m, Cents(r.ShiftRate!.Value));
        Assert.Equal(33.33m, Cents(r.TripRate!.Value));
        Assert.True(r.Net.RestsOnDefault);
    }

    [Fact]
    public void FR21_AnEmptyPeriodHasNoRates()
    {
        var r = Reports.Combine(new(2026, 9, 21), new(2026, 9, 27), []);
        Assert.Equal(0, r.Shifts);
        Assert.Null(r.ShiftRate);
        Assert.Null(r.TripRate);
    }

    [Fact]
    public void FR24_TheCsvHasAHeaderARowPerShiftAndATotal()
    {
        var total = Reports.Combine(new(2026, 9, 21), new(2026, 9, 27), [Busy, Idle]);
        var csv = Reports.ToCsv(total, [new(new(2026, 9, 22), "Spark", Busy), new(new(2026, 9, 23), "Spark, Walmart", Idle)]);
        var lines = csv.TrimEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

        Assert.Equal("date,platform,trips,gross,tips,energy_cost,net,clock_hours,trip_hours,miles,unpaid_miles,shift_rate,trip_rate", lines[0]);
        Assert.Equal(4, lines.Length);
        Assert.StartsWith("2026-09-22,Spark,2,100.00,0.00,8.63,91.38,5.00,3.00,50.0,25.0,20.00,33.33", lines[1]);
        Assert.StartsWith("2026-09-23,\"Spark, Walmart\",0,0.00,", lines[2]); // a comma in a name is quoted
        Assert.EndsWith(",0.00,", lines[2]);                                   // $0/hr shift rate; no trips, so no trip rate
        Assert.StartsWith("total,,2,100.00,0.00,10.70,89.31,6.00,3.00,62.0,37.0,16.67,33.33", lines[3]);
    }

    // ---- Service ----

    private Guid ClosedShift(DateTimeOffset start, decimal startOdo, decimal endOdo, decimal pay = 34.89m)
    {
        var shift = Shifts.Start("Spark", start, new(startOdo, Grade.Measured));
        var trip = _ledger.Accept(shift, new Offer(new(pay, Grade.Stated), new(6.6m, Grade.Stated), 2, 32, new(58, Grade.Stated), start), start);
        Trips.RecordActuals(trip, new(new(55, Grade.Entered), new(6.4m, Grade.Entered), new(5.7m, Grade.Entered)));
        Shifts.End(shift, start.AddMinutes(90), new(endOdo, Grade.Measured));
        return shift;
    }

    [Fact]
    public void FR21_TheReportTakesClosedShiftsThatStartedInThePeriod()
    {
        ClosedShift(Friday.AddDays(-7), 1000m, 1020m);          // the week before: out
        var mine = ClosedShift(Friday.AddDays(-2), 1020m, 1040m); // Wednesday: in
        Shifts.Start("Spark", Friday, new(1040m, Grade.Measured)); // open: out

        var (from, to) = Reports.WeekOf(DateOnly.FromDateTime(Friday.DateTime));
        var report = Reporting.Report(from, to);
        Assert.Equal(1, report.Shifts);
        Assert.Equal(Shifts.Summary(mine).Gross, report.Gross);
        Assert.Single(Reporting.Rows(from, to));
    }

    // ---- Screen ----

    [Fact]
    public void UI_Reports_ShowsThisWeekByDefault()
    {
        ClosedShift(Friday.AddDays(-2), 1020m, 1040m);
        var page = RenderComponent<ReportsPage>();
        var report = Reporting.Report(new(2026, 9, 21), new(2026, 9, 27));
        Assert.Equal(Display.Money(report.Gross), page.Find("#report-gross").TextContent.Trim());
        Assert.Equal(Display.Rate(report.ShiftRate!.Value), page.Find("#report-shift-rate").TextContent.Trim());
        Assert.Contains("from=2026-09-21&to=2026-09-27", page.Find("#export-csv").GetAttribute("href"));
    }

    [Fact]
    public void UI_Reports_AnEmptyPeriodSaysSo()
    {
        var page = RenderComponent<ReportsPage>();
        Assert.Contains("No closed shifts", page.Markup);
    }
}

/// <summary>Reports through HTTP.</summary>
public sealed class ReportApiTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private readonly LedgerApp _app = new();
    private readonly HttpClient _http;

    public ReportApiTests() => _http = _app.CreateClient();

    public void Dispose()
    {
        _http.Dispose();
        _app.Dispose();
    }

    [Fact]
    public async Task FR33_TheReportAndItsCsv()
    {
        var report = await _http.GetFromJsonAsync<PeriodReport>("/api/reports?from=2026-09-21&to=2026-09-27", Json);
        Assert.Equal(0, report!.Shifts);

        var csv = await _http.GetAsync("/api/reports.csv?from=2026-09-21&to=2026-09-27");
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("date,platform,", await csv.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task FR33_ABackwardsRangeIs400()
    {
        var response = await _http.GetAsync("/api/reports?from=2026-09-27&to=2026-09-21");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
