namespace FreeExtend.Gui;

internal static class PowerShellQuoting
{
    /// <summary>A PowerShell single-quoted literal: safe for paths with spaces and apostrophes.</summary>
    public static string Quote(string s) => "'" + s.Replace("'", "''") + "'";
}
