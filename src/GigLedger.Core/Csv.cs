using System.Text;

namespace GigLedger.Core;

/// <summary>CSV per RFC 4180, shared by reports (FR-24), the full export (FR-32), and imports (FR-22).</summary>
public static class Csv
{
    /// <summary>A field with a comma, quote, or line break is quoted, and its quotes doubled.</summary>
    public static string Quote(string field) =>
        field.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + field.Replace("\"", "\"\"") + "\"" : field;

    /// <summary>
    /// Rows of fields. Quoted fields may hold commas, doubled quotes, and line breaks. Lines end in
    /// CRLF or a bare LF, and a trailing line end does not make an empty row.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> Read(string text)
    {
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var atLineStart = true;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c != '"') field.Append(c);
                else if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else quoted = false;
                continue;
            }

            atLineStart = false;
            switch (c)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r' when i + 1 < text.Length && text[i + 1] == '\n':
                    break;
                case '\r' or '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = [];
                    atLineStart = true;
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (!atLineStart)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }
        return rows;
    }

    public const string LineEnd = "\r\n";
}
