using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GigLedger.Core;
using GigLedger.Data;
using GigLedger.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GigLedger.Tests;

/// <summary>The app, hosted in memory on its own in-memory SQLite database.</summary>
public sealed class LedgerApp : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public LedgerApp() => _connection.Open();

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<LedgerContext>>();
            services.AddDbContext<LedgerContext>(o => o.UseSqlite(_connection));
        });

    public int StoredTrips()
    {
        using var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<LedgerContext>().Trips.Count();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}

internal static class ServiceCollectionExtensions
{
    public static void RemoveAll<T>(this IServiceCollection services)
    {
        foreach (var d in services.Where(d => d.ServiceType == typeof(T)).ToList())
            services.Remove(d);
    }
}

/// <summary>
/// SRS FR-33, FR-34, NFR-2 through HTTP, written before the endpoints exist. Each test gets
/// its own app and database. The API's numbers are compared to Core's, never to constants,
/// because FR-34 says the API may add nothing of its own.
/// </summary>
public sealed class ApiTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly DateTimeOffset T0 = new(2026, 8, 2, 6, 57, 0, TimeSpan.FromHours(-5));
    private static readonly EnergyBasis Defaults = EnergyBasis.FromDefaults(4.0m, 0.69m);

    private readonly LedgerApp _app = new();
    private readonly HttpClient _http;

    public ApiTests() => _http = _app.CreateClient();

    public void Dispose()
    {
        _http.Dispose();
        _app.Dispose();
    }

    private static Offer Run4Offer() => new(
        new(34.89m, Grade.Stated), new(6.6m, Grade.Stated), 2, 32, new(58, Grade.Stated), T0.AddMinutes(1));

    private async Task<Guid> StartShift()
    {
        var response = await _http.PostAsJsonAsync("/api/shifts",
            new StartShiftRequest("Spark", T0, new(17_000m, Grade.Measured)), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Created>(Json))!.Id;
    }

    private async Task<Guid> AcceptRun4(Guid shift)
    {
        var response = await _http.PostAsJsonAsync($"/api/shifts/{shift}/trips",
            new AcceptOfferRequest(Run4Offer(), T0.AddMinutes(2)), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Created>(Json))!.Id;
    }

    private static readonly GradedActuals Run4Actuals =
        new(new(55, Grade.Entered), new(6.4m, Grade.Measured), new(5.7m, Grade.Entered));

    // ---- FR-33: evaluate ----

    [Fact]
    public async Task FR33_EvaluateReturnsCoresForecastAndVerdict()
    {
        var response = await _http.PostAsJsonAsync("/api/offers/evaluate", Run4Offer(), Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var evaluation = (await response.Content.ReadFromJsonAsync<OfferEvaluation>(Json))!;

        var expected = Calculations.ForecastNetPerHour(Run4Offer(), Defaults);
        Assert.Equal(expected.Value, evaluation.Forecast.Value);
        Assert.Equal(Calculations.AcceptVerdict(expected, 25.00m), evaluation.Verdict);
        Assert.Equal(25.00m, evaluation.Threshold);
    }

    [Fact]
    public async Task FR33_EvaluateStoresNothing()
    {
        var response = await _http.PostAsJsonAsync("/api/offers/evaluate", Run4Offer(), Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // it ran, and still stored nothing
        Assert.Equal(0, _app.StoredTrips());
    }

    [Fact]
    public async Task NFR2_TheResponseNamesItsAssumptions()
    {
        // The raw JSON, not a deserialized object: an assumption the client never sees does not count.
        var raw = await (await _http.PostAsJsonAsync("/api/offers/evaluate", Run4Offer(), Json)).Content.ReadAsStringAsync();
        Assert.Contains("\"assumptions\"", raw);
        Assert.Contains("efficiency", raw);
        Assert.Contains("energy price", raw);
        Assert.Contains("return miles", raw);
    }

    // ---- FR-33: a whole shift through the API ----

    [Fact]
    public async Task FR33_AWholeShiftThroughTheApiSummarizesAsCoreDoes()
    {
        var shift = await StartShift();
        var trip = await AcceptRun4(shift);
        Assert.Equal(HttpStatusCode.NoContent,
            (await _http.PostAsJsonAsync($"/api/trips/{trip}/actuals", Run4Actuals, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await _http.PostAsJsonAsync($"/api/shifts/{shift}/end",
                new EndShiftRequest(T0.AddMinutes(90), new(17_012.1m, Grade.Measured)), Json)).StatusCode);

        var response = await _http.GetAsync($"/api/shifts/{shift}/summary");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = (await response.Content.ReadFromJsonAsync<ShiftSummary>(Json))!;

        var expected = Calculations.SummarizeShift(
            new ShiftSpan(90, 17_000m, 17_012.1m),
            [new TripRecord(34.89m, new TripActuals(55, 6.4m, 5.7m))],
            Defaults);
        Assert.Equal(expected.Gross, summary.Gross);
        Assert.Equal(expected.ShiftRate.Value, summary.ShiftRate.Value);
        Assert.Equal(expected.TripRate!.Value, summary.TripRate!.Value);
        Assert.Equal(expected.Net.Value, summary.Net.Value);
        Assert.Equal(expected.DeadheadMiles, summary.DeadheadMiles);
    }

    [Fact]
    public async Task FR7_AStoredTripComesBackWithItsGradesByName()
    {
        var trip = await AcceptRun4(await StartShift());
        await _http.PostAsJsonAsync($"/api/trips/{trip}/actuals", Run4Actuals, Json);

        var raw = await _http.GetStringAsync($"/api/trips/{trip}");
        Assert.Contains("\"grade\":\"Stated\"", raw);
        Assert.Contains("\"grade\":\"Measured\"", raw);
        Assert.Contains("\"grade\":\"Entered\"", raw);

        var stored = JsonSerializer.Deserialize<StoredTrip>(raw, Json)!;
        Assert.Equal(Run4Actuals, stored.Actuals);
    }

    [Fact]
    public async Task FR33_OpenShiftIs204WhenNoneAnd200WhenOne()
    {
        Assert.Equal(HttpStatusCode.NoContent, (await _http.GetAsync("/api/shifts/open")).StatusCode);
        var shift = await StartShift();
        var open = await _http.GetFromJsonAsync<StoredShift>("/api/shifts/open", Json);
        Assert.Equal(shift, open!.Id);
    }

    [Fact]
    public async Task FR33_TripsOnAShift()
    {
        var shift = await StartShift();
        var trip = await AcceptRun4(shift);
        var trips = await _http.GetFromJsonAsync<List<StoredTrip>>($"/api/shifts/{shift}/trips", Json);
        Assert.Equal(trip, Assert.Single(trips!).Id);
    }

    // ---- Slice 2 through the API ----

    [Fact]
    public async Task FR33_AChargeSessionAndTheEnergyReport()
    {
        var session = new ChargeSession(T0, new(17_935m, Grade.Measured), new(24.3m, Grade.Measured), new(16.77m, Grade.Measured),
            36, 71, "Blink", ChargeType.DcFast, Purpose.Work);
        Assert.Equal(HttpStatusCode.Created, (await _http.PostAsJsonAsync("/api/charges", session, Json)).StatusCode);

        var report = await _http.GetFromJsonAsync<EnergyReport>(
            $"/api/energy?from={Uri.EscapeDataString(T0.AddDays(-1).ToString("o"))}&to={Uri.EscapeDataString(T0.AddDays(1).ToString("o"))}", Json);
        Assert.Equal(1, report!.Sessions);
        Assert.Equal(16.77m / 24.3m, report.Prices.FastOnly!.Value);
        Assert.Null(report.Efficiency); // one session cannot measure efficiency
    }

    [Fact]
    public async Task FR33_AnImpossibleChargeSessionIs400()
    {
        var bad = new ChargeSession(T0, new(17_935m, Grade.Measured), new(0m, Grade.Measured), new(0m, Grade.Measured),
            36, 71, "Blink", ChargeType.DcFast, Purpose.Work);
        Assert.Equal(HttpStatusCode.BadRequest, (await _http.PostAsJsonAsync("/api/charges", bad, Json)).StatusCode);
    }

    [Fact]
    public async Task FR33_TipsPostInPiecesAndAreNeverAddedOnTopOfPay()
    {
        // SRS 0.8 reversed "one tip per trip, added to pay".
        var trip = await AcceptRun4(await StartShift());
        await _http.PostAsJsonAsync($"/api/trips/{trip}/actuals", Run4Actuals, Json);
        var tip = new TipRequest(6.00m, T0.AddHours(12));
        Assert.Equal(HttpStatusCode.NoContent, (await _http.PostAsJsonAsync($"/api/trips/{trip}/tip", tip, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _http.PostAsJsonAsync($"/api/trips/{trip}/tip", tip, Json)).StatusCode);

        var report = await _http.GetFromJsonAsync<TripReport>($"/api/trips/{trip}/report", Json);
        Assert.Equal(12.00m, report!.Trip.Tip!.Value.Value);
        Assert.Equal(report.Rates!.GrossPerHourBeforeTip.Value, report.Rates.GrossPerHour.Value);
    }

    [Fact]
    public async Task FR33_TheHomeRateCanBeSet()
    {
        var response = await _http.PostAsJsonAsync("/api/settings/home-rate", new HomeRateRequest(0.13m, new DateOnly(2026, 9, 1)), Json);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var rate = await _http.GetFromJsonAsync<HomeRate>("/api/settings/home-rate?on=2026-09-25", Json);
        Assert.Equal(0.13m, rate!.PerKwh);
        Assert.False(rate.IsPlaceholder);
    }

    // ---- Refusals map to status codes ----

    [Fact]
    public async Task FR33_UnknownShiftIs404()
    {
        // A missing route is also a 404, so the body must name the shift: the endpoint ran.
        var id = Guid.NewGuid();
        var response = await _http.GetAsync($"/api/shifts/{id}/summary");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(id.ToString(), await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task FR33_UnknownTripIs404()
    {
        var id = Guid.NewGuid();
        var response = await _http.GetAsync($"/api/trips/{id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(id.ToString(), await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task FR33_SummaryOfAnOpenShiftIs409()
    {
        var response = await _http.GetAsync($"/api/shifts/{await StartShift()}/summary");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task FR33_SecondActualsIs409()
    {
        var trip = await AcceptRun4(await StartShift());
        await _http.PostAsJsonAsync($"/api/trips/{trip}/actuals", Run4Actuals, Json);
        var again = await _http.PostAsJsonAsync($"/api/trips/{trip}/actuals", Run4Actuals, Json);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task FR33_BackwardsOdometerIs400()
    {
        var shift = await StartShift();
        var response = await _http.PostAsJsonAsync($"/api/shifts/{shift}/end",
            new EndShiftRequest(T0.AddMinutes(60), new(16_999m, Grade.Measured)), Json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FR33_ZeroEstimateIs400()
    {
        var offer = Run4Offer() with { EstimatedMinutes = new(0, Grade.Stated) };
        var response = await _http.PostAsJsonAsync("/api/offers/evaluate", offer, Json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
