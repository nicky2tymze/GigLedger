namespace GigLedger.Core;

/// <summary>FR-23: the kinds of payment a platform's earnings export lists.</summary>
public enum PayoutType { TripEarnings, Tip, Incentive, AdjustmentCredit }

/// <summary>
/// One payment from a platform's earnings export (FR-23). It stands on its own, keyed by the
/// platform's trip ID; no shift or trip logged in GigLedger is needed. Zone is as printed.
/// </summary>
public sealed record Payout(
    string Platform,
    string TripId,
    DateTimeOffset At,
    string Zone,
    PayoutType Type,
    Graded<decimal> Amount,
    string DepositStatus,
    DateOnly? DepositedOn);

public interface IPayoutService
{
    /// <summary>FR-23. All or nothing; rows already stored are skipped, matched by count.</summary>
    ImportResult Import(Stream xlsx);
    /// <summary>The payouts whose time falls in the year, in time order.</summary>
    IReadOnlyList<Payout> Year(int year);
}

/// <summary>SDD 6.8: the Spark earnings export's Transactions sheet into payouts.</summary>
public static class PayoutImport
{
    public static IReadOnlyList<Payout> Parse(Stream xlsx) => throw new NotImplementedException();
}

/// <summary>SDD 6.8: one sheet of an .xlsx, as rows of cell text.</summary>
public static class Xlsx
{
    public static IReadOnlyList<IReadOnlyList<string>> ReadSheet(Stream xlsx, string sheet) => throw new NotImplementedException();
}
