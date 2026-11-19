using System.Text.RegularExpressions;

namespace FreeExtend.Gui.Tests;

/// <summary>Static checks on the PowerShell scripts: a regex that is invalid only shows up on a fresh clone, so it is checked here.</summary>
public class ScriptSanityTests
{
    private static readonly Regex LiteralPattern = new(@"-(?:replace|match|notmatch|split)\s+'((?:[^']|'')*)'", RegexOptions.IgnoreCase);

    private static IEnumerable<string> Scripts()
    {
        string root = ProjectPaths.FindRoot(AppContext.BaseDirectory, File.Exists) ?? throw new InvalidOperationException("project root not found");
        foreach (var f in Directory.EnumerateFiles(root, "*.ps1", SearchOption.TopDirectoryOnly)) yield return f;
        foreach (var f in Directory.EnumerateFiles(Path.Combine(root, "tools"), "*.ps1", SearchOption.AllDirectories))
            if (!f.Contains("vdd-download")) yield return f;
    }

    [Fact]
    public void Every_literal_replace_or_match_pattern_is_a_valid_regex()
    {
        var bad = new List<string>();
        foreach (var script in Scripts())
        {
            foreach (var line in File.ReadAllLines(script))
            {
                foreach (Match m in LiteralPattern.Matches(line))
                {
                    string pattern = m.Groups[1].Value.Replace("''", "'");
                    try { _ = new Regex(pattern); }
                    catch (ArgumentException ex) { bad.Add($"{Path.GetFileName(script)}: '{pattern}' -> {ex.Message}"); }
                }
            }
        }
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }
}
