using GigLedger.Core;

namespace GigLedger.Data;

/// <summary>Slice 3b: the tax summary and reconciliation (FR-29 to FR-31).</summary>
public sealed partial class LedgerServices : ITaxService
{
    public void SetMileageRate(int year, decimal perMile) => throw new NotImplementedException();
    public MileageRate? RateFor(int year) => throw new NotImplementedException();
    TaxSummary ITaxService.Summary(int year) => throw new NotImplementedException();
    public void RecordForm(PlatformForm form) => throw new NotImplementedException();
    public IReadOnlyList<decimal> RecordedByMonth(int year, string platform) => throw new NotImplementedException();
    public Reconciliation? Reconcile(int year, string platform) => throw new NotImplementedException();
}
