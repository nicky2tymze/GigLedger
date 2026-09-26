namespace GigLedger.Core;

public enum TaxForm { Form1099K, Form1099Nec }

/// <summary>The standard mileage rate for a tax year, entered from the published figure (FR-30).</summary>
public sealed record MileageRate(int Year, decimal PerMile);

/// <summary>
/// A platform's annual tax form as the driver received it (FR-31). Monthly amounts exist only on a
/// 1099-K; when given there must be twelve, and they must add up to the annual total.
/// </summary>
public sealed record PlatformForm(int Year, string Platform, TaxForm Form, decimal AnnualTotal, IReadOnlyList<decimal>? Monthly);

/// <summary>One deduction method. Vehicle is null when it cannot be computed (no rate, no miles).</summary>
public sealed record DeductionMethod(Result? Vehicle, decimal ParkingAndTolls, Result? Total);

/// <summary>
/// FR-29: a tax year, both deduction methods side by side. GigLedger shows both and chooses
/// neither. The method is the author's reading of the IRS rules (SDD 6.6), to be confirmed
/// with a tax preparer.
/// </summary>
public sealed record TaxSummary(
    int Year,
    IReadOnlyDictionary<string, decimal> GrossByPlatform,
    MileageTotals Miles,
    Result? BusinessShare,
    Result ChargingCost,
    IReadOnlyDictionary<ExpenseCategory, decimal> ExpensesByCategory,
    MileageRate? Rate,
    DeductionMethod StandardMileage,
    DeductionMethod ActualExpenses,
    decimal OtherBusinessExpenses);

public sealed record MonthDifference(int Month, decimal Recorded, decimal Reported, decimal Difference);

/// <summary>FR-31. Difference is reported minus recorded: positive means the form shows more than the ledger.</summary>
public sealed record Reconciliation(
    int Year, string Platform, TaxForm Form,
    decimal Recorded, decimal Reported, decimal Difference,
    IReadOnlyList<MonthDifference>? Months);

public static class Tax
{
    public static void Validate(MileageRate rate) => throw new NotImplementedException();

    public static void Validate(PlatformForm form) => throw new NotImplementedException();

    /// <summary>FR-29 (SDD 6.6).</summary>
    public static TaxSummary Summarize(
        int year,
        IReadOnlyDictionary<string, decimal> grossByPlatform,
        MileageTotals miles,
        Result chargingCost,
        IReadOnlyList<Expense> expenses,
        MileageRate? rate) => throw new NotImplementedException();

    /// <summary>FR-31: the form against the ledger's twelve months.</summary>
    public static Reconciliation Reconcile(PlatformForm form, IReadOnlyList<decimal> recordedByMonth) => throw new NotImplementedException();
}

public interface ITaxService
{
    void SetMileageRate(int year, decimal perMile);
    MileageRate? RateFor(int year);
    TaxSummary Summary(int year);
    void RecordForm(PlatformForm form);
    /// <summary>Trip pay by the month it was accepted, tips by the month they posted.</summary>
    IReadOnlyList<decimal> RecordedByMonth(int year, string platform);
    /// <summary>Null until the platform's form for the year has been entered.</summary>
    Reconciliation? Reconcile(int year, string platform);
}
