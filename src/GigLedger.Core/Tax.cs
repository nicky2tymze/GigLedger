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
    decimal OtherBusinessExpenses,
    IReadOnlyDictionary<string, PaymentSource>? GrossFrom = null);

/// <summary>FR-31: where a platform's recorded payouts for a year came from. Never both.</summary>
public enum PaymentSource { LoggedTrips, ImportedPayouts }

public sealed record MonthDifference(int Month, decimal Recorded, decimal Reported, decimal Difference);

/// <summary>FR-31. Difference is reported minus recorded: positive means the form shows more than the ledger.</summary>
public sealed record Reconciliation(
    int Year, string Platform, TaxForm Form,
    decimal Recorded, decimal Reported, decimal Difference,
    IReadOnlyList<MonthDifference>? Months,
    PaymentSource RecordedFrom = PaymentSource.LoggedTrips);

public static class Tax
{
    public static void Validate(MileageRate rate)
    {
        if (rate.PerMile <= 0)
            throw new ArgumentOutOfRangeException(nameof(rate), rate.PerMile, "A mileage rate must be positive.");
    }

    public static void Validate(PlatformForm form)
    {
        if (string.IsNullOrWhiteSpace(form.Platform))
            throw new ArgumentException("Name the platform the form is from.", nameof(form));
        if (form.AnnualTotal < 0)
            throw new ArgumentOutOfRangeException(nameof(form), form.AnnualTotal, "An annual total cannot be negative.");
        if (form.Monthly is null)
            return;
        if (form.Form == TaxForm.Form1099Nec)
            throw new ArgumentException("A 1099-NEC reports a year, not months.", nameof(form));
        if (form.Monthly.Count != 12)
            throw new ArgumentException($"A 1099-K has twelve months; {form.Monthly.Count} were given.", nameof(form));
        if (form.Monthly.Sum() != form.AnnualTotal)
            throw new ArgumentException($"The months add up to {form.Monthly.Sum()}, not the annual {form.AnnualTotal}.", nameof(form));
    }

    /// <summary>FR-29 (SDD 6.6).</summary>
    public static TaxSummary Summarize(
        int year,
        IReadOnlyDictionary<string, decimal> grossByPlatform,
        MileageTotals miles,
        Result chargingCost,
        IReadOnlyList<Expense> expenses,
        MileageRate? rate)
    {
        decimal Spent(params ExpenseCategory[] categories) =>
            expenses.Where(e => categories.Contains(e.Category)).Sum(e => e.Amount.Value);

        var byCategory = Enum.GetValues<ExpenseCategory>().ToDictionary(c => c, c => Spent(c));
        var parkingAndTolls = Spent(ExpenseCategory.Parking, ExpenseCategory.Tolls);

        var allMiles = miles.BusinessMiles + miles.PersonalMiles;
        Result? share = allMiles > 0 ? new Result(miles.BusinessMiles / allMiles, []) : null;

        Result? standardVehicle = rate is null ? null : new Result(miles.BusinessMiles * rate.PerMile, []);
        var standard = new DeductionMethod(
            standardVehicle, parkingAndTolls,
            standardVehicle is null ? null : new Result(standardVehicle.Value + parkingAndTolls, []));

        var vehicleCosts = chargingCost.Value + Spent(ExpenseCategory.Vehicle);
        Result? actualVehicle = share is null ? null : new Result(vehicleCosts * share.Value, chargingCost.Assumptions);
        var actual = new DeductionMethod(
            actualVehicle, parkingAndTolls,
            actualVehicle is null ? null : new Result(actualVehicle.Value + parkingAndTolls, chargingCost.Assumptions));

        return new TaxSummary(
            year, grossByPlatform, miles, share, chargingCost, byCategory, rate, standard, actual,
            Spent(ExpenseCategory.Phone, ExpenseCategory.Supplies, ExpenseCategory.Other));
    }

    /// <summary>FR-31: the form against the ledger's twelve months.</summary>
    public static Reconciliation Reconcile(PlatformForm form, IReadOnlyList<decimal> recordedByMonth)
    {
        Validate(form);
        if (recordedByMonth.Count != 12)
            throw new ArgumentException("The ledger's year has twelve months.", nameof(recordedByMonth));

        var recorded = recordedByMonth.Sum();
        var months = form.Monthly?
            .Select((reported, i) => new MonthDifference(i + 1, recordedByMonth[i], reported, reported - recordedByMonth[i]))
            .ToList();
        return new Reconciliation(form.Year, form.Platform, form.Form, recorded, form.AnnualTotal, form.AnnualTotal - recorded, months);
    }
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
