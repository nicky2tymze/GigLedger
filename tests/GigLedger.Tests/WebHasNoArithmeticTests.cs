using System.Text.RegularExpressions;

namespace GigLedger.Tests;

/// <summary>
/// TC-34 (SDD 9.4): the Web project shows numbers, it does not compute them (FR-34).
///
/// What this checks: no multiplication or division in any Web source file, once comments,
/// string literals, and markup are removed. Rates, costs, and shares all need one or the
/// other, so this is where a calculation leaking into a page or endpoint would show.
///
/// What it does not check: addition and subtraction, which a page may legitimately use
/// (a counter, an index). A sum computed in Web would get past this test.
///
/// A known false positive: a C# string nested inside a Razor attribute's quotes, as in
/// href="@($"/api/attachments/{id}")", is read as code, and "api/attachments" as i / a.
/// Build such strings in @code, where the scanner reads them as strings.
/// </summary>
public partial class WebHasNoArithmeticTests
{
    [GeneratedRegex(@"@\*.*?\*@|/\*.*?\*/|//[^\n]*|@""(?:[^""]|"""")*""|""(?:\\.|[^""\\\n])*""|'(?:\\.|[^'\\\n])'", RegexOptions.Singleline)]
    private static partial Regex CommentsAndStrings();

    // An operand, then * or /, then an operand. Markup like </div> and <br /> has no operand
    // on one side, so it does not match.
    [GeneratedRegex(@"[\w\)\]]\s*[*/]\s*[\w\(]")]
    private static partial Regex MultiplyOrDivide();

    public static IReadOnlyList<string> Scan(string source)
    {
        var code = CommentsAndStrings().Replace(source, " ");
        return MultiplyOrDivide().Matches(code).Select(m => m.Value).ToList();
    }

    private static string WebSourceDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GigLedger.sln")))
            dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("GigLedger.sln"), "src", "GigLedger.Web");
    }

    [Fact]
    public void TC34_TheScannerCatchesAPlantedCalculation()
    {
        // The check must be shown able to fail before its pass means anything.
        Assert.NotEmpty(Scan("var rate = pay / hours;"));
        Assert.NotEmpty(Scan("@(summary.Gross * 0.9m)"));
    }

    [Fact]
    public void TC34_TheScannerIgnoresMarkupCommentsAndStrings()
    {
        Assert.Empty(Scan("<div></div> <br /> <base href=\"/\" />"));
        Assert.Empty(Scan("// divide pay / hours in Core\n/* a * b */ @* x / y *@"));
        Assert.Empty(Scan("app.MapGet(\"/api/shifts/{id}/summary\", Handler);"));
    }

    /// <summary>Every offender in the .cs and .razor files under root, build output excluded.</summary>
    public static IReadOnlyList<string> ScanDirectory(string root)
    {
        var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs") || f.EndsWith(".razor"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();
        if (files.Count == 0)
            throw new InvalidOperationException($"No .cs or .razor files under {root}: a scan of nothing proves nothing.");
        return files
            .SelectMany(f => Scan(File.ReadAllText(f)).Select(hit => $"{Path.GetRelativePath(root, f)}: {hit}"))
            .ToList();
    }

    [Fact]
    public void TC34_AScanThatReadsNoFilesFails()
    {
        // Zero files scanned is not zero offenders: a moved or emptied folder must not pass.
        var empty = Directory.CreateTempSubdirectory("gigledger-noweb-");
        try
        {
            Assert.Throws<InvalidOperationException>(() => ScanDirectory(empty.FullName));
        }
        finally
        {
            empty.Delete(recursive: true);
        }
    }

    [Fact]
    public void TC34_NoMultiplicationOrDivisionInTheWebProject()
    {
        var offenders = ScanDirectory(WebSourceDirectory());
        Assert.True(offenders.Count == 0, "Arithmetic in Web: " + string.Join("; ", offenders));
    }
}
