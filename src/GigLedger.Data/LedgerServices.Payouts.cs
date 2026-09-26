using GigLedger.Core;
using Microsoft.EntityFrameworkCore;

namespace GigLedger.Data;

/// <summary>SDD 6.8: payouts from a platform's earnings export (FR-23).</summary>
public sealed partial class LedgerServices : IPayoutService
{
    ImportResult IPayoutService.Import(Stream xlsx)
    {
        // Parse reads and checks the whole file before anything is stored.
        var payouts = PayoutImport.Parse(xlsx);

        // Matched by count, not by presence: two identical tips in the file are two payouts,
        // and a second import of that file adds neither.
        var stored = Current(db.Payouts.AsNoTracking())
            .Select(ToPayout)
            .GroupBy(p => p)
            .ToDictionary(g => g.Key, g => g.Count());
        var fresh = payouts
            .GroupBy(p => p)
            .SelectMany(g => g.Take(Math.Max(0, g.Count() - stored.GetValueOrDefault(g.Key))))
            .ToList();

        using var transaction = db.Database.BeginTransaction();
        db.Payouts.AddRange(fresh.Select(p => new PayoutRow
        {
            RecordedAt = clock.GetUtcNow(),
            Platform = p.Platform,
            TripId = p.TripId,
            At = p.At,
            Zone = p.Zone,
            Type = p.Type,
            Amount = p.Amount.Value, AmountGrade = p.Amount.Grade,
            DepositStatus = p.DepositStatus,
            DepositedOn = p.DepositedOn,
        }));
        db.SaveChanges();
        transaction.Commit();
        return new ImportResult(fresh.Count, payouts.Count - fresh.Count);
    }

    IReadOnlyList<Payout> IPayoutService.Year(int year) =>
        // Filtered in memory: SQLite cannot compare DateTimeOffset in SQL.
        Current(db.Payouts.AsNoTracking())
            .Where(r => r.At.Year == year)
            .OrderBy(r => r.At)
            .Select(ToPayout)
            .ToList();

    private static Payout ToPayout(PayoutRow r) => new(
        r.Platform, r.TripId, r.At, r.Zone, r.Type, new(r.Amount, r.AmountGrade), r.DepositStatus, r.DepositedOn);
}
