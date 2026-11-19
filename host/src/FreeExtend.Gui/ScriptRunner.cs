using System.ComponentModel;
using System.Diagnostics;

namespace FreeExtend.Gui;

internal sealed record ScriptResult(int ExitCode, bool Cancelled, string LogTail);

/// <summary>
/// Runs a PowerShell script in a hidden window, optionally elevated (UAC prompt). Output cannot be read from an elevated process, so it is
/// written to a temporary log file whose last lines are returned. A refused UAC prompt is reported as <c>Cancelled</c>, not as an error.
/// </summary>
internal static class ScriptRunner
{
    private const int ErrorCancelled = 1223;

    public static Task<ScriptResult> RunAsync(string scriptPath, bool elevated, params string[] args) => Task.Run(() =>
    {
        string log = Path.Combine(Path.GetTempPath(), "freeextend-" + Guid.NewGuid().ToString("N") + ".log");
        // The script runs in its own "powershell -File" so its exit code follows -File rules: 0 unless the script calls "exit N" or throws.
        // (Called with "&", a script that merely ends would report the code of the last native command it ran, e.g. pnputil's 3010 = reboot needed.)
        string call = "& powershell.exe -NoProfile -ExecutionPolicy Bypass -File " + PowerShellQuoting.Quote(scriptPath)
                      + string.Concat(args.Select(a => " " + PowerShellQuoting.Quote(a)));
        string q = PowerShellQuoting.Quote(log);
        // try/catch so a script that throws gives a non-zero exit code (a bare "exit $LASTEXITCODE" would report 0 after a throw).
        string command = $"try {{ {call} *>&1 | Out-File -Encoding utf8 {q}; exit $LASTEXITCODE }} catch {{ $_ | Out-String | Out-File -Append -Encoding utf8 {q}; exit 1 }}";
        var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"")
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            Verb = elevated ? "runas" : "",
        };
        try
        {
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("Could not start PowerShell.");
            p.WaitForExit();
            return new ScriptResult(p.ExitCode, false, TailOf(log));
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return new ScriptResult(-1, true, "");
        }
        finally
        {
            try { File.Delete(log); } catch (Exception) { }
        }
    });

    private static string TailOf(string logPath)
    {
        try { return string.Join(Environment.NewLine, File.ReadAllLines(logPath).TakeLast(20)); }
        catch (Exception) { return ""; }
    }
}
