using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using GigLedger.Core;
using Microsoft.EntityFrameworkCore;

namespace GigLedger.Data;

/// <summary>Slice 3b: the full export (FR-32).</summary>
public sealed partial class LedgerServices : IExportService
{
    public void Export(Stream output)
    {
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        var manifest = new List<string>
        {
            "GigLedger full export (SRS FR-32)",
            $"Generated {clock.GetUtcNow():o}",
            "Every version of every row is included: a correction is a row whose SupersedesId names the row it replaces.",
            "",
        };

        manifest.Add(Table(zip, "Shifts", db.Shifts));
        manifest.Add(Table(zip, "ShiftCloses", db.ShiftCloses));
        manifest.Add(Table(zip, "Trips", db.Trips));
        manifest.Add(Table(zip, "Declines", db.Declines));
        manifest.Add(Table(zip, "TripActuals", db.TripActuals));
        manifest.Add(Table(zip, "Settings", db.Settings));
        manifest.Add(Table(zip, "Limits", db.Limits));
        manifest.Add(Table(zip, "EntryMarks", db.EntryMarks));
        manifest.Add(Table(zip, "ChargeSessions", db.ChargeSessions));
        manifest.Add(Table(zip, "Tips", db.Tips));
        manifest.Add(Table(zip, "HomeRates", db.HomeRates));
        manifest.Add(Table(zip, "Drives", db.Drives));
        manifest.Add(Table(zip, "Expenses", db.Expenses));
        manifest.Add(Table(zip, "Attachments", db.Attachments));
        manifest.Add(Table(zip, "MileageRates", db.MileageRates));
        manifest.Add(Table(zip, "PlatformForms", db.PlatformForms));
        manifest.Add(Table(zip, "Payouts", db.Payouts));

        // Receipts as their original files; the Attachments table carries each one's SHA-256.
        var receipts = db.Attachments.AsNoTracking().ToList();
        foreach (var r in receipts)
        {
            var name = string.Concat(Path.GetFileName(r.FileName).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            using var entry = zip.CreateEntry($"receipts/{r.Id}-{name}").Open();
            entry.Write(r.Content);
        }
        manifest.Add($"receipts: {receipts.Count}");

        using var writer = new StreamWriter(zip.CreateEntry("manifest.txt").Open());
        writer.Write(string.Join(Core.Csv.LineEnd, manifest) + Core.Csv.LineEnd);
    }

    private static readonly string[] VersionColumns = ["RecordedAt", "SupersedesId", "CorrectionReason"];

    /// <summary>
    /// One CSV: Id, the table's own fields, then the version fields, so a row's history reads
    /// left to right. Stored file content is not a column; it is exported as files.
    /// </summary>
    private static string Table<T>(ZipArchive zip, string name, IQueryable<T> rows) where T : LedgerRecord
    {
        var own = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.PropertyType != typeof(byte[]));
        var columns = new[] { typeof(LedgerRecord).GetProperty("Id")! }
            .Concat(own)
            .Concat(VersionColumns.Select(c => typeof(LedgerRecord).GetProperty(c)!))
            .ToList();

        var all = rows.AsNoTracking().AsEnumerable().OrderBy(r => r.RecordedAt).ToList();
        using var writer = new StreamWriter(zip.CreateEntry($"{name}.csv").Open());
        writer.Write(string.Join(",", columns.Select(c => c.Name)) + Core.Csv.LineEnd);
        foreach (var row in all)
            writer.Write(string.Join(",", columns.Select(c => Core.Csv.Quote(Format(c.GetValue(row))))) + Core.Csv.LineEnd);
        return $"{name}: {all.Count}";
    }

    private static string Format(object? value) => value switch
    {
        null => "",
        DateTimeOffset t => t.ToString("o", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };
}
