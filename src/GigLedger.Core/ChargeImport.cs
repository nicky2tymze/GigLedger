namespace GigLedger.Core;

/// <summary>FR-22, SDD 6.7: charging receipts, one CSV row each, into charge sessions.</summary>
public static class ChargeImport
{
    /// <summary>Every row, or an ArgumentException naming the first line that cannot be imported.</summary>
    public static IReadOnlyList<ChargeSession> Parse(string csv) => throw new NotImplementedException();
}
