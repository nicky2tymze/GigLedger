using GigLedger.Core;

namespace GigLedger.Data;

/// <summary>
/// The Core service interfaces, over the ledger. The UI and the API both call these
/// (FR-34). They move data in and out; every calculation is delegated to Core.
/// </summary>
public sealed class LedgerServices(LedgerContext db, TimeProvider clock)
    : ISettingsService, IOfferService, ITripService, IShiftService
{
    public Settings Get() => throw new NotImplementedException();
    public void Set(Settings settings) => throw new NotImplementedException();

    public OfferEvaluation Evaluate(Offer offer) => throw new NotImplementedException();
    public Guid Accept(Guid shiftId, Offer offer, DateTimeOffset acceptedAt) => throw new NotImplementedException();

    public void RecordActuals(Guid tripId, GradedActuals actuals) => throw new NotImplementedException();
    StoredTrip ITripService.Get(Guid tripId) => throw new NotImplementedException();

    public Guid Start(string platform, DateTimeOffset startedAt, Graded<decimal> startOdometer) => throw new NotImplementedException();
    public void End(Guid shiftId, DateTimeOffset endedAt, Graded<decimal> endOdometer) => throw new NotImplementedException();
    StoredShift IShiftService.Get(Guid shiftId) => throw new NotImplementedException();
    public ShiftSummary Summary(Guid shiftId) => throw new NotImplementedException();
}
