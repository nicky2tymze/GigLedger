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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GigLedger.Tests;

/// <summary>
/// Slice 3b pass 2: the tax summary and reconciliation (FR-29 to FR-31), written before the
/// code. Expected values are worked by hand from SDD 6.6. The method itself is the author's
/// reading of the IRS rules; these tests hold the code to the method, not the method to the law.
/// </summary>
public sealed class TaxTests : TestContext
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 9, 0, 0, TimeSpan.FromHours(-5));
    private static readonly DateOnly Sep25 = new(2026, 9, 25);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly LedgerContext _db;
    private readonly LedgerServices _ledger;
    private readonly TestClock _clock = new(Now);

    public TaxTests()
    {
        _connection.Open();
        _db = LedgerDatabase.Open(_connection);
        _ledger = new LedgerServices(_db, _clock);
        Services.AddSingleton<TimeProvider>(_clock);
        Services.AddSingleton<ITaxService>(_ledger);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }

    private ITaxService Taxes => _ledger;

    private static Expense Spent(ExpenseCategory category, decimal amount) =>
        new(Sep25, category, new(amount, Grade.Measured), category.ToString());

    private static readonly Expense[] YearOfExpenses =
    [
        Spent(ExpenseCategory.Parking, 10m), Spent(ExpenseCategory.Tolls, 5m),
        Spent(ExpenseCategory.Vehicle, 200m),
        Spent(ExpenseCategory.Phone, 50m), Spent(ExpenseCategory.Supplies, 18.47m),
    ];

    private static TaxSummary Worked(MileageRate? rate, MileageTotals? miles = null, Result? charging = null) => Tax.Summarize(
        2026, new Dictionary<string, decimal> { ["Spark"] = 12_000m },
        miles ?? new MileageTotals(1000m, 250m),
        charging ?? new Result(300m, []),
        YearOfExpenses, rate);

    // ---- Core: the two methods ----

    [Fact]
    public void FR29_StandardMileageIsMilesTimesTheRatePlusParkingAndTolls()
    {
        var s = Worked(new MileageRate(2026, 0.70m));
        Assert.Equal(700.00m, s.StandardMileage.Vehicle!.Value); // 1000 x 0.70
        Assert.Equal(15m, s.StandardMileage.ParkingAndTolls);
        Assert.Equal(715.00m, s.StandardMileage.Total!.Value);
    }

    [Fact]
    public void FR29_ActualExpensesAreVehicleCostsTimesTheBusinessSharePlusParkingAndTolls()
    {
        var s = Worked(new MileageRate(2026, 0.70m));
        Assert.Equal(0.8m, s.BusinessShare!.Value);          // 1000 / 1250
        Assert.Equal(400m, s.ActualExpenses.Vehicle!.Value); // (300 charging + 200 vehicle) x 0.8
        Assert.Equal(415m, s.ActualExpenses.Total!.Value);
    }

    [Fact]
    public void FR29_PhoneSuppliesAndOtherStandApartFromBothMethods()
    {
        Assert.Equal(68.47m, Worked(null).OtherBusinessExpenses);
    }

    [Fact]
    public void FR30_WithoutARateTheStandardMethodHasNoNumber()
    {
        var s = Worked(rate: null);
        Assert.Null(s.StandardMileage.Vehicle);
        Assert.Null(s.StandardMileage.Total);
        Assert.NotNull(s.ActualExpenses.Total); // the other method does not need the rate
    }

    [Fact]
    public void FR29_WithoutMilesThereIsNoShareAndNoActualVehicleCost()
    {
        var s = Worked(null, miles: new MileageTotals(0m, 0m));
        Assert.Null(s.BusinessShare);
        Assert.Null(s.ActualExpenses.Vehicle);
    }

    [Fact]
    public void FR29_ThePlaceholderHomeRateIsNamedInTheActualMethod()
    {
        var placeholder = new Result(300m, [new Assumption("home rate", "placeholder $0.15/kWh")]);
        var s = Worked(null, charging: placeholder);
        Assert.Contains(s.ActualExpenses.Total!.Assumptions, a => a.Input == "home rate");
    }

    [Fact]
    public void FR30_ARateMustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Tax.Validate(new MileageRate(2026, 0m)));
    }

    // ---- Core: reconciliation ----

    private static readonly decimal[] HundredAMonth = Enumerable.Repeat(100m, 12).ToArray();

    [Fact]
    public void FR31_A1099NecReconcilesTheYear()
    {
        var r = Tax.Reconcile(new PlatformForm(2026, "Spark", TaxForm.Form1099Nec, 1250m, null), HundredAMonth);
        Assert.Equal((1200m, 1250m, 50m), (r.Recorded, r.Reported, r.Difference));
        Assert.Null(r.Months);
    }

    [Fact]
    public void FR31_A1099KReconcilesMonthByMonth()
    {
        var reported = HundredAMonth.ToArray();
        reported[8] = 150m; // September
        var r = Tax.Reconcile(new PlatformForm(2026, "Spark", TaxForm.Form1099K, 1250m, reported), HundredAMonth);
        var september = Assert.Single(r.Months!, m => m.Difference != 0);
        Assert.Equal((9, 100m, 150m, 50m), (september.Month, september.Recorded, september.Reported, september.Difference));
    }

    [Theory]
    [InlineData(TaxForm.Form1099K, 11, 1100)]   // eleven months
    [InlineData(TaxForm.Form1099K, 12, 1000)]   // months do not add up to the annual total
    [InlineData(TaxForm.Form1099Nec, 12, 1200)] // a 1099-NEC has no months
    public void FR31_AFormThatCannotBeRightIsRefused(TaxForm form, int months, int annual)
    {
        var monthly = Enumerable.Repeat(100m, months).ToArray();
        Assert.Throws<ArgumentException>(() => Tax.Validate(new PlatformForm(2026, "Spark", form, annual, monthly)));
    }

    // ---- Service ----

    private Guid SparkTrip(DateTimeOffset accepted, decimal pay)
    {
        var shift = ((IShiftService)_ledger).Start("Spark", accepted, new(17_000m, Grade.Measured));
        var trip = _ledger.Accept(shift, new Offer(new(pay, Grade.Stated), new(6.6m, Grade.Stated), 2, 32, new(58, Grade.Stated), accepted), accepted);
        ((ITripService)_ledger).RecordActuals(trip, new(new(55, Grade.Entered), new(6.4m, Grade.Entered), new(5.7m, Grade.Entered)));
        ((IShiftService)_ledger).End(shift, accepted.AddMinutes(90), new(17_012.1m, Grade.Measured));
        return trip;
    }

    [Fact]
    public void FR31_PayCountsInTheMonthAcceptedAndTipsInTheMonthPosted()
    {
        var trip = SparkTrip(new(2026, 9, 30, 20, 0, 0, TimeSpan.FromHours(-5)), 34.89m);
        ((ITripService)_ledger).RecordTip(trip, 6.00m, new(2026, 10, 1, 8, 0, 0, TimeSpan.FromHours(-5)));
        var months = Taxes.RecordedByMonth(2026, "Spark");
        Assert.Equal(34.89m, months[8]); // September
        Assert.Equal(6.00m, months[9]);  // October
    }

    [Fact]
    public void FR29_TheServiceGathersTheYear()
    {
        SparkTrip(Now, 34.89m);
        ((IMileageService)_ledger).Log(new Drive(Sep25, new(16_990m, Grade.Measured), new(17_000m, Grade.Measured), Purpose.Personal, "Walmart for binders"));
        foreach (var e in YearOfExpenses) ((IExpenseService)_ledger).Record(e);
        Taxes.SetMileageRate(2026, 0.70m);

        var s = Taxes.Summary(2026);
        Assert.Equal(34.89m, s.GrossByPlatform["Spark"]);
        Assert.Equal(new MileageTotals(12.1m, 10m), s.Miles); // the shift's span, and the drive in by hand
        Assert.Equal(200m, s.ExpensesByCategory[ExpenseCategory.Vehicle]);
        Assert.Equal(0.70m, s.Rate!.PerMile);
        Assert.Equal(8.47m, s.StandardMileage.Vehicle!.Value); // 12.1 x 0.70
    }

    [Fact]
    public void FR30_TheLatestRateForAYearApplies()
    {
        Assert.Null(Taxes.RateFor(2026));
        Taxes.SetMileageRate(2026, 0.69m);
        Taxes.SetMileageRate(2026, 0.70m);
        Assert.Equal(0.70m, Taxes.RateFor(2026)!.PerMile);
        Assert.Null(Taxes.RateFor(2025));
    }

    [Fact]
    public void FR31_NoFormNoReconciliation_ThenTheFormAgainstTheLedger()
    {
        SparkTrip(Now, 34.89m);
        Assert.Null(Taxes.Reconcile(2026, "Spark"));
        Taxes.RecordForm(new PlatformForm(2026, "Spark", TaxForm.Form1099Nec, 40.00m, null));
        var r = Taxes.Reconcile(2026, "Spark")!;
        Assert.Equal((34.89m, 40.00m, 5.11m), (r.Recorded, r.Reported, r.Difference));
    }

    [Fact]
    public void FR25h_TheTaxTablesRefuseBulkChanges()
    {
        Taxes.SetMileageRate(2026, 0.70m);
        Taxes.RecordForm(new PlatformForm(2026, "Spark", TaxForm.Form1099Nec, 40m, null));
        Assert.Throws<SqliteException>(() => _db.MileageRates.ExecuteDelete());
        Assert.Throws<SqliteException>(() => _db.PlatformForms.ExecuteUpdate(s => s.SetProperty(f => f.AnnualTotal, 0m)));
    }

    // ---- Screen ----

    [Fact]
    public void UI_Tax_ShowsBothMethodsAndSaysTheRateIsMissing()
    {
        SparkTrip(Now, 34.89m);
        var page = RenderComponent<TaxPage>();
        Assert.Contains("rate not entered", page.Find("#standard-total").TextContent);
        Assert.Equal(Display.Money(Taxes.Summary(2026).ActualExpenses.Total!.Value), page.Find("#actual-total").TextContent.Trim());
        Assert.Contains("tax preparer", page.Markup);
    }

    [Fact]
    public void UI_Tax_EnteringTheRateFillsInTheStandardMethod()
    {
        SparkTrip(Now, 34.89m);
        var page = RenderComponent<TaxPage>();
        page.Find("#mileage-rate").Change("0.70");
        page.Find("#set-mileage-rate").Click();
        Assert.Equal(0.70m, Taxes.RateFor(2026)!.PerMile);
        Assert.Equal(Display.Money(Taxes.Summary(2026).StandardMileage.Total!.Value), page.Find("#standard-total").TextContent.Trim());
    }
}

/// <summary>Taxes through HTTP.</summary>
public sealed class TaxApiTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private readonly LedgerApp _app = new();
    private readonly HttpClient _http;

    public TaxApiTests() => _http = _app.CreateClient();

    public void Dispose()
    {
        _http.Dispose();
        _app.Dispose();
    }

    [Fact]
    public async Task FR33_RateSummaryFormAndReconciliation()
    {
        Assert.Equal(HttpStatusCode.NoContent,
            (await _http.PostAsJsonAsync("/api/tax/2026/mileage-rate", new MileageRateRequest(0.70m), Json)).StatusCode);
        var summary = await _http.GetFromJsonAsync<TaxSummary>("/api/tax/2026", Json);
        Assert.Equal(0.70m, summary!.Rate!.PerMile);

        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync("/api/tax/2026/reconcile/Spark")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _http.PostAsJsonAsync("/api/tax/2026/forms",
            new PlatformForm(2026, "Spark", TaxForm.Form1099Nec, 0m, null), Json)).StatusCode);
        var r = await _http.GetFromJsonAsync<Reconciliation>("/api/tax/2026/reconcile/Spark", Json);
        Assert.Equal(0m, r!.Difference);
    }

    [Fact]
    public async Task FR33_AFormThatCannotBeRightIs400()
    {
        var bad = new PlatformForm(2026, "Spark", TaxForm.Form1099Nec, 1200m, Enumerable.Repeat(100m, 12).ToArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await _http.PostAsJsonAsync("/api/tax/2026/forms", bad, Json)).StatusCode);
    }
}
