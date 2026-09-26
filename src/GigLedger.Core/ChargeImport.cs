using System.Globalization;

namespace GigLedger.Core;

/// <summary>FR-22, SDD 6.7: charging receipts, one CSV row each, into charge sessions.</summary>
public static class ChargeImport
{
    private static readonly string[] Required =
        ["receipt_number", "start_local", "timezone", "kwh", "net_total", "station_id", "location_name"];

    /// <summary>The zones the receipts print. Any other is refused rather than guessed.</summary>
    private static readonly Dictionary<string, TimeSpan> Zones = new()
    {
        ["CDT"] = TimeSpan.FromHours(-5),
        ["CST"] = TimeSpan.FromHours(-6),
    };

    /// <summary>Every row, or an ArgumentException naming the first line that cannot be imported.</summary>
    public static IReadOnlyList<ChargeSession> Parse(string csv)
    {
        var rows = Csv.Read(csv);
        if (rows.Count == 0)
            throw new ArgumentException("The file is empty.");

        var header = rows[0];
        var missing = Required.Where(name => !header.Contains(name)).ToList();
        if (missing.Count > 0)
            throw new ArgumentException($"Missing column: {string.Join(", ", missing)}.");
        var column = Required.ToDictionary(name => name, name => header.ToList().IndexOf(name));

        var sessions = new List<ChargeSession>();
        for (var i = 1; i < rows.Count; i++)
        {
            var line = i + 1;
            try
            {
                sessions.Add(Session(name => rows[i][column[name]]));
            }
            catch (Exception e) when (e is ArgumentException or FormatException or IndexOutOfRangeException or ArgumentOutOfRangeException)
            {
                throw new ArgumentException($"line {line}: {Reason(e)}");
            }
        }
        return sessions;
    }

    private static ChargeSession Session(Func<string, string> field)
    {
        var zone = field("timezone");
        if (!Zones.TryGetValue(zone, out var offset))
            throw new FormatException($"time zone {zone} is not one the receipts use (CDT, CST).");

        var local = DateTime.ParseExact(field("start_local"), "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        var session = new ChargeSession(
            new DateTimeOffset(local, offset),
            Odometer: null,
            new Graded<decimal>(Number(field("kwh"), "kwh"), Grade.Measured),
            new Graded<decimal>(Number(field("net_total"), "net_total"), Grade.Measured),
            StartSoc: null,
            EndSoc: null,
            $"Blink {field("station_id")}, {field("location_name")}",
            ChargeType.DcFast,
            Purpose: null,
            field("receipt_number"));
        EnergyCalculations.Validate(session);
        return session;
    }

    private static decimal Number(string text, string name) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new FormatException($"{name} \"{text}\" is not a number.");

    /// <summary>The reason alone, without .NET's "(Parameter ...)" and "Actual value" tail.</summary>
    private static string Reason(Exception e) =>
        e.Message.Split(" (Parameter")[0].Split(Environment.NewLine)[0];
}
