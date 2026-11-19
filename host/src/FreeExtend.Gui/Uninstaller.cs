using FreeExtend.Host.Display;
using FreeExtend.Host.Net;

namespace FreeExtend.Gui;

/// <summary>What was removed. <c>AppRemoved</c> is null when the tablet was not connected (the app stays on it).</summary>
internal sealed record UninstallReport(bool DriverRemoved, bool GpuPreferenceRemoved, bool? AppRemoved, string? Error);

/// <summary>
/// Removes everything FreeExtend put on the system, in this order: the virtual display driver (elevated remove.ps1), the graphics preference
/// for FreeExtend.Host.exe, the app on the tablet. A step that fails does not stop the others. The project folder is never touched.
/// </summary>
internal static class Uninstaller
{
    public static async Task<UninstallReport> RunAsync(ProjectPaths paths, bool tabletReady)
    {
        var errors = new List<string>();

        bool driver = false;
        var script = await ScriptRunner.RunAsync(paths.RemoveDriverScript, elevated: true);
        if (script.Cancelled) errors.Add("Driver removal was cancelled.");
        else if (script.ExitCode != 0) errors.Add("Driver removal failed: " + script.LogTail);
        else driver = true;

        bool preference = false;
        try { new GpuPreference().Remove(paths.HostExe); preference = true; }
        catch (Exception ex) { errors.Add("Could not remove the graphics preference: " + ex.Message); }

        bool? app = null;
        if (tabletReady)
        {
            try
            {
                var (code, output) = await Task.Run(() => AdbTunnel.Run($"uninstall {SystemProbe.Package}", 30_000));
                app = code == 0 && output.Contains("Success", StringComparison.OrdinalIgnoreCase);
                if (app != true) errors.Add("Could not remove the app from the tablet: " + output.Trim());
            }
            catch (Exception ex) { app = false; errors.Add("Could not remove the app from the tablet: " + ex.Message); }
        }

        return new UninstallReport(driver, preference, app, errors.Count == 0 ? null : string.Join(" ", errors));
    }
}
