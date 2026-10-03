using GigLedger.Core;
using Microsoft.EntityFrameworkCore;

namespace GigLedger.Data;

/// <summary>Slice 4: entry checks (FR-35 to FR-38, SDD 6.10).</summary>
public sealed partial class LedgerServices
{
    public Limits GetLimits()
    {
        var r = Current(db.Limits.AsNoTracking()).SingleOrDefault()
            ?? throw new InvalidOperationException("No limits are stored; the schema seed is missing.");
        return new Limits(
            new(r.PayConfirm, r.PayDocument),
            new(r.SpeedConfirm, r.SpeedDocument),
            new(r.TripLengthConfirm, r.TripLengthDocument),
            new(r.TipConfirm, r.TipDocument),
            new(r.ShiftLengthConfirm, r.ShiftLengthDocument),
            r.BatteryKwh);
    }

    public void SetLimits(Limits limits)
    {
        foreach (var pair in new[] { limits.Pay, limits.Speed, limits.TripLength, limits.Tip, limits.ShiftLength })
            if (pair.Confirm < 0 || pair.Document < pair.Confirm)
                throw new ArgumentException("Each limit needs a confirm figure of zero or more and a document figure at or above it.", nameof(limits));
        if (limits.BatteryKwh <= 0)
            throw new ArgumentException("The battery size must be more than zero.", nameof(limits));

        db.Limits.Add(new LimitsRow
        {
            RecordedAt = clock.GetUtcNow(),
            SupersedesId = Current(db.Limits.AsNoTracking()).Single().Id,
            PayConfirm = limits.Pay.Confirm, PayDocument = limits.Pay.Document,
            SpeedConfirm = limits.Speed.Confirm, SpeedDocument = limits.Speed.Document,
            TripLengthConfirm = limits.TripLength.Confirm, TripLengthDocument = limits.TripLength.Document,
            TipConfirm = limits.Tip.Confirm, TipDocument = limits.Tip.Document,
            ShiftLengthConfirm = limits.ShiftLength.Confirm, ShiftLengthDocument = limits.ShiftLength.Document,
            BatteryKwh = limits.BatteryKwh,
        });
        db.SaveChanges();
    }

    public IReadOnlyList<EntryMark> ExplainedValues(DateOnly from, DateOnly to) =>
        // Filtered and ordered in memory: SQLite cannot compare DateTimeOffset in SQL.
        db.EntryMarks.AsNoTracking().Where(m => m.Level == MarkLevel.Explained).AsEnumerable()
            .Where(m => DateOnly.FromDateTime(m.At.DateTime) is var day && day >= from && day <= to)
            .OrderBy(m => m.At)
            .Select(ToMark)
            .ToList();

    public IReadOnlyList<EntryMark> MarksOn(Guid recordId) =>
        db.EntryMarks.AsNoTracking().Where(m => m.RecordId == recordId).AsEnumerable()
            .OrderBy(m => m.At)
            .Select(ToMark)
            .ToList();

    /// <summary>
    /// Checks the values, resolves them against the acknowledgement (throwing before anything is stored),
    /// and stages the marks. The caller's SaveChanges stores them with the record.
    /// </summary>
    private void CheckAndMark(MarkedRecord record, Guid recordId, Acknowledgement? acknowledgement, params (EntryLimit Limit, decimal Value)[] values)
    {
        var marks = EntryChecks.Resolve(EntryChecks.CheckAll(GetLimits(), values), acknowledgement);
        foreach (var (check, level, explanation) in marks)
            db.EntryMarks.Add(new EntryMarkRow
            {
                RecordedAt = clock.GetUtcNow(),
                Record = record,
                RecordId = recordId,
                Limit = check.Limit,
                Value = check.Value,
                Passed = check.Passed,
                Level = level,
                Explanation = explanation,
                At = clock.GetLocalNow(),
            });
    }

    private static EntryMark ToMark(EntryMarkRow m) =>
        new(m.Id, m.Record, m.RecordId, m.Limit, m.Value, m.Passed, m.Level, m.Explanation, m.At);
}
