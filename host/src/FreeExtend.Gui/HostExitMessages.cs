namespace FreeExtend.Gui;

/// <summary>Plain-English text for the exit code of <c>FreeExtend.Host.exe stream</c> (codes: 0 stopped, 1 failed, 2 driver, 3 tablet, 4 encoder).</summary>
internal static class HostExitMessages
{
    public static string Describe(int exitCode, string? lastErrorLine)
    {
        string WithDetail(string head) => string.IsNullOrWhiteSpace(lastErrorLine) ? head : head + ": " + lastErrorLine.Trim();
        return exitCode switch
        {
            0 => "Stopped",
            1 => WithDetail("Streaming failed"),
            2 => "Virtual display driver not installed or no display modes configured",
            3 => "Tablet not connected or not authorized",
            4 => WithDetail("No usable hardware H.264 encoder"),
            _ => $"Host stopped unexpectedly (code {exitCode})",
        };
    }
}
