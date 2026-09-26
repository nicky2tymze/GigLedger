using GigLedger.Core;

namespace GigLedger.Data;

/// <summary>Slice 3a: the record. Corrections, mileage, expenses, receipts, and backup.</summary>
public sealed partial class LedgerServices
    : IMileageService, IExpenseService, ICorrectionService, IAttachmentService, IBackupService
{
    public Guid Log(Drive drive) => throw new NotImplementedException();
    IReadOnlyList<StoredDrive> IMileageService.Year(int year) => throw new NotImplementedException();
    MileageTotals IMileageService.Totals(int year) => throw new NotImplementedException();
    Guid IMileageService.Correct(Guid driveId, Drive corrected, string reason) => throw new NotImplementedException();
    IReadOnlyList<Version<Drive>> IMileageService.History(Guid driveId) => throw new NotImplementedException();

    public Guid Record(Expense expense) => throw new NotImplementedException();
    IReadOnlyList<StoredExpense> IExpenseService.Year(int year) => throw new NotImplementedException();
    Guid IExpenseService.Correct(Guid expenseId, Expense corrected, string reason) => throw new NotImplementedException();
    IReadOnlyList<Version<Expense>> IExpenseService.History(Guid expenseId) => throw new NotImplementedException();

    public void CorrectActuals(Guid tripId, GradedActuals corrected, string reason) => throw new NotImplementedException();
    public IReadOnlyList<Version<GradedActuals>> ActualsHistory(Guid tripId) => throw new NotImplementedException();
    public Guid CorrectCharge(Guid chargeId, ChargeSession corrected, string reason) => throw new NotImplementedException();
    public IReadOnlyList<Version<ChargeSession>> ChargeHistory(Guid chargeId) => throw new NotImplementedException();

    public Guid Attach(AttachedTo owner, Guid ownerId, string fileName, string contentType, byte[] content) => throw new NotImplementedException();
    StoredAttachment IAttachmentService.Get(Guid attachmentId) => throw new NotImplementedException();
    public IReadOnlyList<AttachmentInfo> For(AttachedTo owner, Guid ownerId) => throw new NotImplementedException();
    public bool Verify(Guid attachmentId) => throw new NotImplementedException();

    public string Backup(string folder) => throw new NotImplementedException();
}
