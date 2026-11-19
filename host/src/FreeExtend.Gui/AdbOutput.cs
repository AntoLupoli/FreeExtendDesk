namespace FreeExtend.Gui;

internal static class AdbOutput
{
    /// <summary>True if <c>adb shell pm list packages</c> output has the exact line <c>package:&lt;package&gt;</c>.</summary>
    public static bool IsPackageInstalled(string pmListOutput, string package) =>
        pmListOutput.Split('\n').Any(l => l.Trim().Equals("package:" + package, StringComparison.Ordinal));

    public static string? ParseModel(string getpropOutput)
    {
        string m = getpropOutput.Trim();
        return m.Length == 0 ? null : m;
    }
}
