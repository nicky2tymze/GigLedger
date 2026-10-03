using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GigLedger.Core;
using GigLedger.Data;
using GigLedger.Web;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GigLedger.Tests;

/// <summary>
/// Slice 4, story 2: entry checks (SRS 0.7 FR-35 to FR-38; SDD 0.8 section 6.10), written before the
/// code. The rules are tested as pure functions first, then where each limit is checked.
/// </summary>
public sealed class EntryRuleTests
{
    private static readonly Limits L = Limits.Initial;

    // ---- The limits (FR-36) ----

    [Fact]
    public void FR36_TheInitialLimitsAreTheArchitects()
    {
        Assert.Equal(new LimitPair(80m, 250m), L.Pay);
        Assert.Equal(new LimitPair(60m, 90m), L.Speed);
        Assert.Equal(new LimitPair(50m, 150m), L.TripLength);
        Assert.Equal(new LimitPair(40m, 150m), L.Tip);
        Assert.Equal(new LimitPair(12m, 16m), L.ShiftLength);
        Assert.Equal(64.8m, L.BatteryKwh);
    }

    // ---- Three levels (FR-35) ----

    [Fact]
    public void FR35_AtTheConfirmFigureNothingIsNeeded() =>
        Assert.Null(EntryChecks.Check(EntryLimit.Pay, 80.00m, L));

    [Fact]
    public void FR35_AboveTheConfirmFigureNeedsAConfirm() =>
        Assert.Equal(new LimitCheck(EntryLimit.Pay, 80.01m, 80m, CheckLevel.NeedsConfirm), EntryChecks.Check(EntryLimit.Pay, 80.01m, L));

    [Fact]
    public void FR35_AtTheDocumentFigureAConfirmIsEnough() =>
        Assert.Equal(CheckLevel.NeedsConfirm, EntryChecks.Check(EntryLimit.Pay, 250m, L)!.Level);

    [Fact]
    public void FR35_AboveTheDocumentFigureNeedsAnExplanation() =>
        Assert.Equal(new LimitCheck(EntryLimit.Pay, 250.01m, 250m, CheckLevel.NeedsExplanation), EntryChecks.Check(EntryLimit.Pay, 250.01m, L));

    [Fact]
    public void FR36_SpeedIsRouteAndReturnMilesOverElapsedTime() =>
        // 20 + 10 miles in 20 minutes is 90 mph.
        Assert.Equal(90m, EntryChecks.SpeedMph(new TripActuals(20, 20m, 10m)));

    [Fact]
    public void FR36_ShiftLengthIsInHours()
    {
        var start = new DateTimeOffset(2026, 8, 2, 6, 0, 0, TimeSpan.FromHours(-5));
        Assert.Equal(12.5m, EntryChecks.ShiftHours(start, start.AddMinutes(750)));
    }

    // ---- The acknowledgement (SDD 6.10) ----

    private static readonly LimitCheck Confirm = new(EntryLimit.Pay, 95m, 80m, CheckLevel.NeedsConfirm);
    private static readonly LimitCheck Explain = new(EntryLimit.Speed, 96m, 90m, CheckLevel.NeedsExplanation);

    [Fact]
    public void FR35_NoChecksNeedNothing() => Assert.Empty(EntryChecks.Resolve([], null));

    [Fact]
    public void FR35_AConfirmLevelWithoutAConfirmIsRefusedWithTheChecks()
    {
        var e = Assert.Throws<NeedsAcknowledgementException>(() => EntryChecks.Resolve([Confirm], null));
        Assert.Equal([Confirm], e.Checks);
        Assert.False(e.NeedsExplanation);
    }

    [Fact]
    public void FR35_AConfirmedValueIsMarkedConfirmed()
    {
        var mark = Assert.Single(EntryChecks.Resolve([Confirm], new Acknowledgement(true)));
        Assert.Equal((Confirm, MarkLevel.Confirmed, (string?)null), mark);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void FR35_ADocumentLevelNeedsAnExplanation(string? explanation)
    {
        var e = Assert.Throws<NeedsAcknowledgementException>(() => EntryChecks.Resolve([Explain], new Acknowledgement(true, explanation)));
        Assert.True(e.NeedsExplanation);
    }

    [Fact]
    public void FR35_AnExplanationWithoutTheConfirmIsNotEnough() =>
        Assert.Throws<NeedsAcknowledgementException>(() => EntryChecks.Resolve([Explain], new Acknowledgement(false, "detour")));

    [Fact]
    public void FR35_AnExplainedValueIsMarkedExplainedWithTheExplanationTrimmed()
    {
        var mark = Assert.Single(EntryChecks.Resolve([Explain], new Acknowledgement(true, "  highway detour, road closed ")));
        Assert.Equal((Explain, MarkLevel.Explained, "highway detour, road closed"), mark);
    }

    [Fact]
    public void FR35_OneAcknowledgementCoversEveryCheckEachAtItsOwnLevel()
    {
        var marks = EntryChecks.Resolve([Confirm, Explain], new Acknowledgement(true, "detour"));
        Assert.Equal(MarkLevel.Confirmed, marks.Single(m => m.Check == Confirm).Level);
        Assert.Equal(MarkLevel.Explained, marks.Single(m => m.Check == Explain).Level);
    }

    [Fact]
    public void FR35_TheChecksAreDescribedInWords()
    {
        var words = EntryChecks.Describe([Confirm, Explain]);
        Assert.Contains("Pay $95.00 is above $80.00", words);
        Assert.Contains("Speed 96 mph is above 90 mph", words);
    }

    // ---- Refusals (FR-37) ----

    [Fact]
    public void FR37_NegativeIsRefusedWithASentence()
    {
        var e = Assert.Throws<ArgumentOutOfRangeException>(() => EntryChecks.RefuseNegative(-1m, "Pay"));
        Assert.Contains("Pay cannot be negative", e.Message);
    }

    [Theory]
    [InlineData(-5, 3, 1)]
    [InlineData(20, -3, 1)]
    [InlineData(20, 3, -1)]
    [InlineData(0, 3, 1)]
    public void FR37_ImpossibleActualsAreRefused(int minutes, double route, double back) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => EntryChecks.RefuseImpossibleActuals(new TripActuals(minutes, (decimal)route, (decimal)back)));

    [Fact]
    public void FR37_AStartFiveMinutesAheadIsAllowedOneSecondMoreIsNot()
    {
        var now = new DateTimeOffset(2026, 8, 2, 6, 0, 0, TimeSpan.FromHours(-5));
        EntryChecks.RefuseFutureStart(now.AddMinutes(5), now);
        var e = Assert.Throws<ArgumentOutOfRangeException>(() => EntryChecks.RefuseFutureStart(now.AddMinutes(5).AddSeconds(1), now));
        Assert.Contains("future", e.Message);
    }

    [Fact]
    public void FR37_TwentyFourHoursIsAllowedAMinuteMoreIsNot()
    {
        var start = new DateTimeOffset(2026, 8, 2, 6, 0, 0, TimeSpan.FromHours(-5));
        EntryChecks.RefuseOverlongShift(start, start.AddHours(24));
        var e = Assert.Throws<ArgumentOutOfRangeException>(() => EntryChecks.RefuseOverlongShift(start, start.AddHours(24).AddMinutes(1)));
        Assert.Contains("24 hours", e.Message);
    }

    [Fact]
    public void FR37_AChargeUpToTheBatteryPlus25PercentIsAllowedMoreIsNot()
    {
        EntryChecks.RefuseOverBattery(81.0m, 64.8m);
        var e = Assert.Throws<ArgumentOutOfRangeException>(() => EntryChecks.RefuseOverBattery(81.01m, 64.8m));
        Assert.Contains("battery", e.Message);
    }
}

/// <summary>Where each limit is checked (FR-36), the refusals in place (FR-37), marks, and the report (FR-38).</summary>
public sealed class EntryCheckStorageTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 2, 6, 57, 0, TimeSpan.FromHours(-5));

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly LedgerContext _db;
    private readonly LedgerServices _ledger;
    private readonly TestClock _clock = new(T0.AddDays(1));

    public EntryCheckStorageTests()
    {
        _connection.Open();
        _db = LedgerDatabase.Open(_connection);
        _ledger = new LedgerServices(_db, _clock);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private IShiftService Shifts => _ledger;
    private IOfferService Offers => _ledger;
    private ITripService Trips => _ledger;
    private IChargeService Charges => _ledger;
    private ICorrectionService Corrections => _ledger;
    private IEntryCheckService Checks => _ledger;

    private static Offer Paying(decimal pay) => new(
        new(pay, Grade.Stated), new(6.6m, Grade.Stated), 2, 32, new(58, Grade.Stated), T0.AddMinutes(1));

    private static GradedActuals Actuals(int minutes, decimal route, decimal back) =>
        new(new(minutes, Grade.Entered), new(route, Grade.Entered), new(back, Grade.Entered));

    private static readonly Acknowledgement Yes = new(true);

    private Guid OpenShift() => Shifts.Start("Spark", T0, new(17_000m, Grade.Measured));

    private Guid Trip() => Offers.Accept(OpenShift(), Paying(30m), T0.AddMinutes(2));

    // ---- Pay, on the offer ----

    [Fact]
    public void FR35_AcceptPastThePayLimitWithoutAConfirmStoresNothing()
    {
        var shift = OpenShift();
        Assert.Throws<NeedsAcknowledgementException>(() => Offers.Accept(shift, Paying(95m), T0.AddMinutes(2)));
        Assert.Empty(Trips.OnShift(shift));
    }

    [Fact]
    public void FR35_AConfirmedAcceptIsStoredAndMarked()
    {
        var trip = Offers.Accept(OpenShift(), Paying(95m), T0.AddMinutes(2), Yes);

        var mark = Assert.Single(Checks.MarksOn(trip));
        Assert.Equal((MarkedRecord.Trip, EntryLimit.Pay, 95m, 80m, MarkLevel.Confirmed), (mark.Record, mark.Limit, mark.Value, mark.Passed, mark.Level));
    }

    [Fact]
    public void FR35_AnAcceptWithinTheLimitIsNotMarked()
    {
        var trip = Offers.Accept(OpenShift(), Paying(80m), T0.AddMinutes(2));
        Assert.Empty(Checks.MarksOn(trip));
    }

    [Fact]
    public void FR35_DeclineChecksPayToo()
    {
        var shift = OpenShift();
        Assert.Throws<NeedsAcknowledgementException>(() => Offers.Decline(shift, Paying(300m), [DeclineReason.Heavy], null, T0.AddMinutes(2), Yes));

        var id = Offers.Decline(shift, Paying(300m), [DeclineReason.Heavy], null, T0.AddMinutes(2), new(true, "holiday surge"));
        Assert.Equal(MarkLevel.Explained, Assert.Single(Checks.MarksOn(id)).Level);
    }

    [Fact]
    public void FR37_NegativePayIsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Offers.Accept(OpenShift(), Paying(-1m), T0.AddMinutes(2), Yes));

    // ---- Speed and trip length, on actuals ----

    [Fact]
    public void FR35_ActualsWithinTheLimitsNeedNothing()
    {
        var trip = Trip();
        Trips.RecordActuals(trip, Actuals(60, 20m, 10m));
        Assert.Empty(Checks.MarksOn(trip));
    }

    [Fact]
    public void FR35_OneAcknowledgementCoversSpeedAndLengthOnActuals()
    {
        var trip = Trip();
        // 100 + 60 miles in 100 minutes: 96 mph and 160 miles, both past the document level.
        Assert.Throws<NeedsAcknowledgementException>(() => Trips.RecordActuals(trip, Actuals(100, 100m, 60m), Yes));
        Assert.Null(Trips.Get(trip).Actuals);

        Trips.RecordActuals(trip, Actuals(100, 100m, 60m), new(true, "interstate run to a far store"));

        var marks = Checks.MarksOn(trip);
        Assert.Equal([EntryLimit.Speed, EntryLimit.TripLength], marks.Select(m => m.Limit).Order());
        Assert.All(marks, m => Assert.Equal((MarkedRecord.TripActuals, MarkLevel.Explained), (m.Record, m.Level)));
    }

    [Fact]
    public void FR37_ZeroMinutesOnActualsIsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Trips.RecordActuals(Trip(), Actuals(0, 5m, 5m), Yes));

    [Fact]
    public void FR35_ACorrectionIsCheckedLikeTheEntry()
    {
        var trip = Trip();
        Trips.RecordActuals(trip, Actuals(60, 20m, 10m));

        Assert.Throws<NeedsAcknowledgementException>(() => Corrections.CorrectActuals(trip, Actuals(60, 40m, 20m), "typo"));
        Corrections.CorrectActuals(trip, Actuals(60, 40m, 20m), "typo", Yes);

        Assert.Equal(EntryLimit.TripLength, Assert.Single(Checks.MarksOn(trip)).Limit);
    }

    // ---- Tip ----

    [Fact]
    public void FR35_ATipPastTheLimitNeedsAConfirm()
    {
        var trip = Trip();
        Assert.Throws<NeedsAcknowledgementException>(() => Trips.RecordTip(trip, 45m, T0.AddHours(3)));
        Assert.Null(Trips.Get(trip).Tip);

        Trips.RecordTip(trip, 45m, T0.AddHours(3), Yes);
        Assert.Equal((MarkedRecord.Tip, EntryLimit.Tip), (Assert.Single(Checks.MarksOn(trip)).Record, Checks.MarksOn(trip)[0].Limit));
    }

    // ---- Shift length ----

    [Fact]
    public void FR35_AShiftLongerThanTwelveHoursNeedsAConfirm()
    {
        var shift = OpenShift();
        Assert.Throws<NeedsAcknowledgementException>(() => Shifts.End(shift, T0.AddHours(13), new(17_100m, Grade.Measured)));
        Assert.Null(Shifts.Get(shift).EndedAt);

        Shifts.End(shift, T0.AddHours(13), new(17_100m, Grade.Measured), Yes);
        Assert.Equal((MarkedRecord.ShiftClose, EntryLimit.ShiftLength, 13m), (Checks.MarksOn(shift)[0].Record, Checks.MarksOn(shift)[0].Limit, Checks.MarksOn(shift)[0].Value));
    }

    [Fact]
    public void FR37_AShiftLongerThan24HoursIsRefused()
    {
        var shift = OpenShift();
        Assert.Throws<ArgumentOutOfRangeException>(() => Shifts.End(shift, T0.AddHours(25), new(17_100m, Grade.Measured), new(true, "x")));
    }

    [Fact]
    public void FR37_AShiftStartInTheFutureIsRefused()
    {
        _clock.Now = T0;
        Assert.Throws<ArgumentOutOfRangeException>(() => Shifts.Start("Spark", T0.AddMinutes(6), new(17_000m, Grade.Measured)));
        Assert.Null(Shifts.Open());
    }

    // ---- Charging ----

    private static ChargeSession Charge(decimal kwh) =>
        new(T0, null, new(kwh, Grade.Measured), new(10m, Grade.Measured), null, null, "Test", ChargeType.DcFast, null);

    [Fact]
    public void FR37_AChargeOverTheBatteryPlus25PercentIsRefused()
    {
        Charges.Record(Charge(81.0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => Charges.Record(Charge(81.01m)));
    }

    [Fact]
    public void FR37_ACorrectedChargeIsRefusedTheSameWay()
    {
        var id = Charges.Record(Charge(20m));
        Assert.Throws<ArgumentOutOfRangeException>(() => Corrections.CorrectCharge(id, Charge(90m), "typo"));
    }

    [Fact]
    public void FR37_AnImportedChargeOverTheBatteryStopsTheFile()
    {
        const string csv =
            "receipt_number,start_local,timezone,kwh,net_total,station_id,location_name\r\n" +
            "CP-1,2026-09-24T08:18:17,CDT,24.2880,16.76,T1,Test Plaza\r\n" +
            "CP-2,2026-09-25T08:18:17,CDT,95.0000,60.00,T1,Test Plaza\r\n";
        var e = Assert.Throws<ArgumentException>(() => Charges.Import(csv));
        Assert.Contains("battery", e.Message);
        Assert.Empty(Charges.Between(T0.AddYears(-1), T0.AddYears(1)));
    }

    // ---- The limits are data (FR-36) ----

    [Fact]
    public void FR36_TheLimitsAreStoredAndChangingThemChangesTheCheck()
    {
        Assert.Equal(Limits.Initial, Checks.GetLimits());
        Checks.SetLimits(Limits.Initial with { Pay = new(100m, 300m) });

        Offers.Accept(OpenShift(), Paying(95m), T0.AddMinutes(2));

        Assert.Equal(new LimitPair(100m, 300m), Checks.GetLimits().Pay);
    }

    // ---- The report (FR-38) ----

    [Fact]
    public void FR38_TheReportListsExplainedValuesOnlyInRangeOldestFirst()
    {
        var shift = OpenShift();
        _clock.Now = T0.AddDays(2);
        Offers.Accept(shift, Paying(95m), T0.AddMinutes(2), Yes);                          // confirmed: not listed
        Offers.Accept(shift, Paying(300m), T0.AddMinutes(3), new(true, "surge"));          // explained
        _clock.Now = T0.AddDays(1);
        Offers.Accept(shift, Paying(260m), T0.AddMinutes(4), new(true, "earlier surge"));  // explained, earlier

        var listed = Checks.ExplainedValues(DateOnly.FromDateTime(T0.AddDays(1).DateTime), DateOnly.FromDateTime(T0.AddDays(2).DateTime));

        Assert.Equal(["earlier surge", "surge"], listed.Select(m => m.Explanation));
        Assert.Empty(Checks.ExplainedValues(DateOnly.FromDateTime(T0.DateTime), DateOnly.FromDateTime(T0.DateTime)));
    }

    // ---- Nothing is deleted (FR-25) ----

    [Fact]
    public void FR25_MarksAndLimitsAreNeverUpdatedOrDeleted()
    {
        Offers.Accept(OpenShift(), Paying(95m), T0.AddMinutes(2), Yes);
        Checks.SetLimits(Limits.Initial with { Tip = new(50m, 150m) });
        foreach (var table in new[] { "EntryMarks", "Limits" })
        {
            Assert.Contains("never deleted", Assert.Throws<SqliteException>(() => _db.Database.ExecuteSqlRaw($"DELETE FROM {table}")).Message);
            Assert.Contains("never updated", Assert.Throws<SqliteException>(() => _db.Database.ExecuteSqlRaw($"UPDATE {table} SET RecordedAt = RecordedAt")).Message);
        }
    }
}

/// <summary>FR-35 through HTTP: a missing acknowledgement is 409 with the checks, so an agent can ask and retry.</summary>
public sealed class EntryCheckApiTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly DateTimeOffset T0 = new(2026, 8, 2, 6, 57, 0, TimeSpan.FromHours(-5));

    private readonly LedgerApp _app = new();
    private readonly HttpClient _http;

    public EntryCheckApiTests() => _http = _app.CreateClient();

    public void Dispose()
    {
        _http.Dispose();
        _app.Dispose();
    }

    private async Task<Guid> StartShift()
    {
        var response = await _http.PostAsJsonAsync("/api/shifts", new StartShiftRequest("Spark", T0, new(17_000m, Grade.Measured)), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Created>(Json))!.Id;
    }

    private static Offer Paying(decimal pay) => new(
        new(pay, Grade.Stated), new(6.6m, Grade.Stated), 2, 32, new(58, Grade.Stated), T0.AddMinutes(1));

    [Fact]
    public async Task API_AMissingAcknowledgementIsAConflictNamingTheCheck()
    {
        var shift = await StartShift();

        var response = await _http.PostAsJsonAsync($"/api/shifts/{shift}/trips", new AcceptOfferRequest(Paying(95m), T0.AddMinutes(2)), Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Pay $95.00 is above $80.00", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task API_WithTheAcknowledgementTheTripIsStored()
    {
        var shift = await StartShift();

        var response = await _http.PostAsJsonAsync($"/api/shifts/{shift}/trips",
            new AcceptOfferRequest(Paying(95m), T0.AddMinutes(2), new Acknowledgement(true)), Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task API_TheExplainedValuesReport()
    {
        var shift = await StartShift();
        (await _http.PostAsJsonAsync($"/api/shifts/{shift}/trips",
            new AcceptOfferRequest(Paying(300m), T0.AddMinutes(2), new Acknowledgement(true, "surge")), Json)).EnsureSuccessStatusCode();
        var today = DateOnly.FromDateTime(DateTime.Now);

        var listed = await _http.GetFromJsonAsync<List<EntryMark>>($"/api/explained?from={today.AddDays(-1):yyyy-MM-dd}&to={today.AddDays(1):yyyy-MM-dd}", Json);

        Assert.Equal("surge", Assert.Single(listed!).Explanation);
    }

    [Fact]
    public async Task API_TheLimitsCanBeReadAndSet()
    {
        Assert.Equal(Limits.Initial, await _http.GetFromJsonAsync<Limits>("/api/limits", Json));
        (await _http.PostAsJsonAsync("/api/limits", Limits.Initial with { Tip = new(50m, 150m) }, Json)).EnsureSuccessStatusCode();
        Assert.Equal(50m, (await _http.GetFromJsonAsync<Limits>("/api/limits", Json))!.Tip.Confirm);
    }
}
