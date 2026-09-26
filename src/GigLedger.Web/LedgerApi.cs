using GigLedger.Core;

namespace GigLedger.Web;

/// <summary>
/// SDD 7.2: the JSON API, one endpoint per service operation (FR-33). Each handler makes
/// exactly one service call and returns what it returns; numbers are never touched here.
/// </summary>
public static class LedgerApi
{
    public static void MapLedgerApi(this WebApplication app)
    {
        var api = app.MapGroup("/api").AddEndpointFilter(RefusalsAsStatusCodes);

        api.MapPost("/offers/evaluate", (Offer offer, IOfferService offers) =>
            Results.Ok(offers.Evaluate(offer)));

        api.MapPost("/shifts", (StartShiftRequest request, IShiftService shifts) =>
        {
            var id = shifts.Start(request.Platform, request.StartedAt, request.StartOdometer);
            return Results.Created($"/api/shifts/{id}", new Created(id));
        });

        api.MapPost("/shifts/{id:guid}/end", (Guid id, EndShiftRequest request, IShiftService shifts) =>
        {
            shifts.End(id, request.EndedAt, request.EndOdometer);
            return Results.NoContent();
        });

        api.MapGet("/shifts/{id:guid}/summary", (Guid id, IShiftService shifts) =>
            Results.Ok(shifts.Summary(id)));

        api.MapGet("/shifts/open", (IShiftService shifts) =>
            shifts.Open() is { } open ? Results.Ok(open) : Results.NoContent());

        api.MapGet("/shifts/{id:guid}/trips", (Guid id, ITripService trips) =>
            Results.Ok(trips.OnShift(id)));

        api.MapPost("/shifts/{id:guid}/trips", (Guid id, AcceptOfferRequest request, IOfferService offers) =>
        {
            var trip = offers.Accept(id, request.Offer, request.AcceptedAt);
            return Results.Created($"/api/trips/{trip}", new Created(trip));
        });

        api.MapPost("/trips/{id:guid}/actuals", (Guid id, GradedActuals actuals, ITripService trips) =>
        {
            trips.RecordActuals(id, actuals);
            return Results.NoContent();
        });

        api.MapGet("/trips/{id:guid}", (Guid id, ITripService trips) =>
            Results.Ok(trips.Get(id)));

        api.MapPost("/trips/{id:guid}/tip", (Guid id, TipRequest request, ITripService trips) =>
        {
            trips.RecordTip(id, request.Amount, request.PostedAt);
            return Results.NoContent();
        });

        api.MapGet("/trips/{id:guid}/report", (Guid id, ITripService trips) =>
            Results.Ok(trips.Report(id)));

        api.MapPost("/charges", (ChargeSession session, IChargeService charges) =>
        {
            var id = charges.Record(session);
            return Results.Created($"/api/charges/{id}", new Created(id));
        });

        api.MapGet("/energy", (DateTimeOffset from, DateTimeOffset to, IChargeService charges) =>
            Results.Ok(charges.Report(from, to)));

        api.MapGet("/settings/home-rate", (DateOnly on, ISettingsService settings) =>
            Results.Ok(settings.HomeRateOn(on)));

        api.MapPost("/settings/home-rate", (HomeRateRequest request, ISettingsService settings) =>
        {
            settings.SetHomeRate(request.PerKwh, request.EffectiveFrom);
            return Results.NoContent();
        });

        api.MapRecordApi();

        api.MapPost("/tax/{year:int}/mileage-rate", (int year, MileageRateRequest request, ITaxService taxes) =>
        {
            taxes.SetMileageRate(year, request.PerMile);
            return Results.NoContent();
        });

        api.MapGet("/tax/{year:int}", (int year, ITaxService taxes) => Results.Ok(taxes.Summary(year)));

        api.MapPost("/tax/{year:int}/forms", (int year, PlatformForm form, ITaxService taxes) =>
        {
            taxes.RecordForm(form with { Year = year });
            return Results.NoContent();
        });

        api.MapGet("/tax/{year:int}/reconcile/{platform}", (int year, string platform, ITaxService taxes) =>
            taxes.Reconcile(year, platform) is { } r
                ? Results.Ok(r)
                : Results.Problem($"No {year} tax form entered for {platform}.", statusCode: StatusCodes.Status404NotFound));

        api.MapGet("/reports", (DateOnly from, DateOnly to, IReportService reports) =>
            Results.Ok(reports.Report(from, to)));

        api.MapGet("/reports.csv", (DateOnly from, DateOnly to, IReportService reports) =>
            Results.File(System.Text.Encoding.UTF8.GetBytes(reports.Csv(from, to)), "text/csv", $"gigledger-{from:yyyy-MM-dd}-to-{to:yyyy-MM-dd}.csv"));
    }

    /// <summary>
    /// A refusal from the services becomes a status code with the reason in the body:
    /// an unknown id is 404, bad input (any ArgumentException) is 400, and a request that conflicts with what is
    /// already stored (a second close, a summary of an open shift) is 409.
    /// </summary>
    private static async ValueTask<object?> RefusalsAsStatusCodes(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (NotFoundException e)
        {
            return Results.Problem(e.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (ArgumentException e)
        {
            return Results.Problem(e.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (InvalidOperationException e)
        {
            return Results.Problem(e.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }
}
