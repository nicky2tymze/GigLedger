using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

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
    private const string Platform = "Spark";

    private static readonly Dictionary<string, PayoutType> Types = new()
    {
        ["Trip earnings"] = PayoutType.TripEarnings,
        ["Tip"] = PayoutType.Tip,
        ["Incentive"] = PayoutType.Incentive,
        ["Adjustment credits"] = PayoutType.AdjustmentCredit,
    };

    /// <summary>
    /// The zones this reader accepts. Spark labels every row CDT, winter included; it is read as
    /// UTC−5 as printed (SRS FR-23), and any other label is refused rather than guessed.
    /// </summary>
    private static readonly Dictionary<string, TimeSpan> Zones = new() { ["CDT"] = TimeSpan.FromHours(-5) };

    /// <summary>Every payout, or an ArgumentException naming the first row that cannot be imported.</summary>
    public static IReadOnlyList<Payout> Parse(Stream xlsx)
    {
        var bytes = ReadAll(xlsx);
        var sheet = Xlsx.ReadSheet(new MemoryStream(bytes), "Transactions");

        var headerIndex = sheet.ToList().FindIndex(row => row.Contains("Trip ID"));
        if (headerIndex < 0)
            throw new ArgumentException("The Transactions sheet has no Trip ID header.");
        var header = sheet[headerIndex].ToList();
        int Column(string name) => header.FindIndex(h => h == name || h.StartsWith(name + " ("));

        string[] required = ["Trip ID", "Transaction date", "Earning type", "Earned", "Deposit status", "Deposited on"];
        var missing = required.Where(name => Column(name) < 0).ToList();
        if (missing.Count > 0)
            throw new ArgumentException($"Missing column: {string.Join(", ", missing)}.");

        var dateHeader = header[Column("Transaction date")];
        var zone = dateHeader.Contains('(') ? dateHeader[(dateHeader.IndexOf('(') + 1)..].TrimEnd(')') : "";
        if (!Zones.TryGetValue(zone, out var offset))
            throw new ArgumentException($"Time zone \"{zone}\" is not one GigLedger reads (CDT).");

        var trip = Column("Trip ID");
        var payouts = new List<Payout>();
        for (var i = headerIndex + 1; i < sheet.Count; i++)
        {
            var row = sheet[i];
            string Cell(int column) => column < row.Count ? row[column] : "";
            // The data ends at the disclaimer footer: text in the first column and nothing after it.
            if (Enumerable.Range(0, Math.Max(row.Count, header.Count)).Where(c => c != trip).All(c => Cell(c).Length == 0))
                break;
            try
            {
                payouts.Add(Row(name => Cell(Column(name)), offset, zone));
            }
            catch (Exception e) when (e is FormatException or ArgumentException)
            {
                throw new ArgumentException($"row {i + 1}: {e.Message}");
            }
        }

        CheckTotal(Xlsx.ReadSheet(new MemoryStream(bytes), "Summary"), payouts.Sum(p => p.Amount.Value));
        return payouts;
    }

    private static Payout Row(Func<string, string> field, TimeSpan offset, string zone)
    {
        var type = field("Earning type");
        if (!Types.TryGetValue(type, out var payoutType))
            throw new FormatException($"earning type \"{type}\" is not one GigLedger knows ({string.Join(", ", Types.Keys)}).");

        var local = DateTime.ParseExact(field("Transaction date"), "yyyy-MM-dd hh:mm tt", CultureInfo.InvariantCulture);
        var deposited = field("Deposited on");
        return new Payout(
            Platform,
            field("Trip ID"),
            new DateTimeOffset(local, offset),
            zone,
            payoutType,
            new Graded<decimal>(Cents(field("Earned"), "Earned"), Grade.Stated),
            field("Deposit status"),
            deposited.Length == 0 ? null : DateOnly.ParseExact(deposited, "yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    /// <summary>The file checks itself: its rows must sum to its own Total earnings.</summary>
    private static void CheckTotal(IReadOnlyList<IReadOnlyList<string>> summary, decimal sum)
    {
        var row = summary.FirstOrDefault(r => r.Contains("Total earnings"))
            ?? throw new ArgumentException("The Summary sheet has no Total earnings to check the rows against.");
        var value = row.SkipWhile(c => c != "Total earnings").Skip(1).FirstOrDefault(c => c.Length > 0) ?? "";
        var total = Cents(value, "Total earnings");
        if (total != sum)
            throw new ArgumentException($"The rows sum to {sum:0.00}, but the Summary sheet's Total earnings is {total:0.00}.");
    }

    /// <summary>To the cent: the file stores money as binary floating point (49999.79999999999).</summary>
    private static decimal Cents(string text, string name) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? Math.Round(value, 2, MidpointRounding.AwayFromZero)
            : throw new FormatException($"{name} \"{text}\" is not a number.");

    private static byte[] ReadAll(Stream stream)
    {
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}

/// <summary>
/// SDD 6.8: one sheet of an .xlsx, as rows of cell text, using only the framework's zip and XML
/// readers. Rows and cells are placed by their references, so skipped ones read as empty.
/// </summary>
public static class Xlsx
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Package = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static IReadOnlyList<IReadOnlyList<string>> ReadSheet(Stream xlsx, string sheet)
    {
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(xlsx, ZipArchiveMode.Read);
        }
        catch (InvalidDataException)
        {
            throw new ArgumentException("The file is not an .xlsx workbook.");
        }

        using (zip)
        {
            var workbook = Load(zip, "xl/workbook.xml") ?? throw new ArgumentException("The file is not an .xlsx workbook.");
            var relationId = workbook.Descendants(Main + "sheet")
                .FirstOrDefault(s => (string?)s.Attribute("name") == sheet)?
                .Attribute(Relationships + "id")?.Value
                ?? throw new ArgumentException($"The workbook has no sheet named {sheet}.");
            var target = Load(zip, "xl/_rels/workbook.xml.rels")?
                .Descendants(Package + "Relationship")
                .FirstOrDefault(r => (string?)r.Attribute("Id") == relationId)?
                .Attribute("Target")?.Value
                ?? throw new ArgumentException($"The workbook does not say where sheet {sheet} is.");
            var path = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;

            var shared = Load(zip, "xl/sharedStrings.xml")?
                .Root!.Elements(Main + "si").Select(Text).ToList() ?? [];
            var data = Load(zip, path) ?? throw new ArgumentException($"Sheet {sheet} is missing from the workbook.");

            var rows = new List<IReadOnlyList<string>>();
            foreach (var row in data.Descendants(Main + "row"))
            {
                var number = (int?)row.Attribute("r") ?? rows.Count + 1;
                while (rows.Count < number - 1) rows.Add([]);
                var cells = new List<string>();
                foreach (var cell in row.Elements(Main + "c"))
                {
                    var column = ColumnIndex((string?)cell.Attribute("r")) ?? cells.Count;
                    while (cells.Count < column) cells.Add("");
                    cells.Add(Value(cell, shared));
                }
                rows.Add(cells);
            }
            return rows;
        }
    }

    private static string Value(XElement cell, List<string> shared)
    {
        var raw = cell.Element(Main + "v")?.Value ?? "";
        return (string?)cell.Attribute("t") switch
        {
            "s" => shared[int.Parse(raw, CultureInfo.InvariantCulture)],
            "inlineStr" => Text(cell.Element(Main + "is")!),
            _ => raw,
        };
    }

    /// <summary>All the text runs of a string item, joined.</summary>
    private static string Text(XElement item) => string.Concat(item.Descendants(Main + "t").Select(t => t.Value));

    /// <summary>"C7" is column 2.</summary>
    private static int? ColumnIndex(string? reference)
    {
        if (reference is null) return null;
        var index = 0;
        foreach (var c in reference.TakeWhile(char.IsLetter))
            index = index * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
        return index - 1;
    }

    private static XDocument? Load(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path);
        if (entry is null) return null;
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }
}
