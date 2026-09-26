namespace GigLedger.Core;

/// <summary>CSV per RFC 4180, shared by reports (FR-24) and the full export (FR-32).</summary>
public static class Csv
{
    /// <summary>A field with a comma, quote, or line break is quoted, and its quotes doubled.</summary>
    public static string Quote(string field) =>
        field.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + field.Replace("\"", "\"\"") + "\"" : field;

    /// <summary>Rows of fields. A trailing line end does not make an empty row.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> Read(string text) => throw new NotImplementedException();

    public const string LineEnd = "\r\n";
}
