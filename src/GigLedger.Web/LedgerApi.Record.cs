using GigLedger.Core;

namespace GigLedger.Web;

/// <summary>Slice 3a endpoints: the record (FR-25 to FR-28, NFR-7). One service call each.</summary>
public static class LedgerApiRecord
{
    public static void MapRecordApi(this RouteGroupBuilder api)
    {
        // ---- Mileage (FR-26) ----

        api.MapPost("/drives", (Drive drive, IMileageService mileage) =>
        {
            var id = mileage.Log(drive);
            return Results.Created($"/api/drives/{id}", new Created(id));
        });

        api.MapGet("/drives", (int year, IMileageService mileage) => Results.Ok(mileage.Year(year)));

        api.MapGet("/mileage/{year:int}", (int year, IMileageService mileage) => Results.Ok(mileage.Totals(year)));

        api.MapPost("/drives/{id:guid}/correct", (Guid id, CorrectDriveRequest request, IMileageService mileage) =>
        {
            var corrected = mileage.Correct(id, request.Drive, request.Reason);
            return Results.Created($"/api/drives/{corrected}", new Created(corrected));
        });

        api.MapGet("/drives/{id:guid}/history", (Guid id, IMileageService mileage) => Results.Ok(mileage.History(id)));

        // ---- Expenses (FR-28) ----

        api.MapPost("/expenses", (Expense expense, IExpenseService expenses) =>
        {
            var id = expenses.Record(expense);
            return Results.Created($"/api/expenses/{id}", new Created(id));
        });

        api.MapGet("/expenses", (int year, IExpenseService expenses) => Results.Ok(expenses.Year(year)));

        api.MapPost("/expenses/{id:guid}/correct", (Guid id, CorrectExpenseRequest request, IExpenseService expenses) =>
        {
            var corrected = expenses.Correct(id, request.Expense, request.Reason);
            return Results.Created($"/api/expenses/{corrected}", new Created(corrected));
        });

        api.MapGet("/expenses/{id:guid}/history", (Guid id, IExpenseService expenses) => Results.Ok(expenses.History(id)));

        // ---- Corrections to trips and charges (FR-25) ----

        api.MapPost("/trips/{id:guid}/actuals/correct", (Guid id, CorrectActualsRequest request, ICorrectionService corrections) =>
        {
            corrections.CorrectActuals(id, request.Actuals, request.Reason);
            return Results.NoContent();
        });

        api.MapGet("/trips/{id:guid}/actuals/history", (Guid id, ICorrectionService corrections) =>
            Results.Ok(corrections.ActualsHistory(id)));

        api.MapPost("/charges/{id:guid}/correct", (Guid id, CorrectChargeRequest request, ICorrectionService corrections) =>
        {
            var corrected = corrections.CorrectCharge(id, request.Session, request.Reason);
            return Results.Created($"/api/charges/{corrected}", new Created(corrected));
        });

        api.MapGet("/charges/{id:guid}/history", (Guid id, ICorrectionService corrections) =>
            Results.Ok(corrections.ChargeHistory(id)));

        // ---- Receipts (FR-27) ----

        api.MapPost("/attachments", (AttachRequest request, IAttachmentService attachments) =>
        {
            byte[] content;
            try
            {
                content = Convert.FromBase64String(request.ContentBase64);
            }
            catch (FormatException)
            {
                throw new ArgumentException("ContentBase64 is not valid base64.", nameof(request));
            }
            var id = attachments.Attach(request.Owner, request.OwnerId, request.FileName, request.ContentType, content);
            return Results.Created($"/api/attachments/{id}", new Created(id));
        });

        api.MapGet("/attachments/{id:guid}", (Guid id, IAttachmentService attachments) =>
        {
            var stored = attachments.Get(id);
            return Results.File(stored.Content, stored.Info.ContentType, stored.Info.FileName);
        });

        api.MapGet("/attachments/{id:guid}/verify", (Guid id, IAttachmentService attachments) =>
            Results.Ok(new Verification(attachments.Verify(id))));

        // ---- Backup (NFR-7): only ever into the configured folder ----

        api.MapPost("/backup", (IBackupService backup, BackupFolder folder) =>
            Results.Ok(new BackupResult(backup.Backup(folder.Path))));
    }
}
