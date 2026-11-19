using System.Diagnostics;

namespace FreeExtend.Host.Net;

public enum DeviceState { None, Unauthorized, Ready }

/// <summary>Il cavo USB passa da <c>adb reverse</c>: il tablet si connette a 127.0.0.1:porta e arriva sul PC.</summary>
public static class AdbTunnel
{
    public static string AdbPath() => ResolveAdbPath(Environment.GetEnvironmentVariable, File.Exists);

    /// <summary>
    /// Dove sta adb: nell'Android SDK indicato da ANDROID_HOME o ANDROID_SDK_ROOT, poi nella posizione predefinita di Android Studio
    /// (<c>%LOCALAPPDATA%\Android\Sdk</c>), altrimenti si lascia a Windows il compito di trovarlo nel PATH.
    /// </summary>
    public static string ResolveAdbPath(Func<string, string?> getEnv, Func<string, bool> fileExists)
    {
        var sdkRoots = new List<string>();
        foreach (var name in new[] { "ANDROID_HOME", "ANDROID_SDK_ROOT" })
            if (getEnv(name) is { Length: > 0 } v) sdkRoots.Add(v);
        if (getEnv("LOCALAPPDATA") is { Length: > 0 } local) sdkRoots.Add(Path.Combine(local, "Android", "Sdk"));

        foreach (var root in sdkRoots)
        {
            string adb = Path.Combine(root, "platform-tools", "adb.exe");
            if (fileExists(adb)) return adb;
        }
        return "adb";
    }

    /// <summary>Interpreta l'output di <c>adb devices</c>. Pronto se almeno un dispositivo e <c>device</c>; "unauthorized" se serve accettare il debug USB sul tablet.</summary>
    public static DeviceState ParseDevices(string adbDevicesOutput)
    {
        var state = DeviceState.None;
        bool inList = false;
        foreach (var raw in adbDevicesOutput.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("List of devices attached", StringComparison.Ordinal)) { inList = true; continue; }
            if (!inList || line.Length == 0) continue;               // salta il rumore prima dell'elenco ("* daemon ...")

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            if (parts[1] == "device") return DeviceState.Ready;
            if (parts[1] == "unauthorized") state = DeviceState.Unauthorized;
        }
        return state;
    }

    public static DeviceState Current() => ParseDevices(Run("devices", timeoutMs: 15_000).Output);

    /// <summary>Interpreta `adb reverse --list` (righe come `UsbFfs tcp:27183 tcp:27183`): c'e una regola per questa porta?</summary>
    public static bool ParseReverseList(string output, int port)
    {
        string token = $"tcp:{port}";
        foreach (var line in output.Split('\n'))
            if (line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Contains(token)) return true;
        return false;
    }

    public static bool HasReverse(int port) => ParseReverseList(Run("reverse --list", timeoutMs: 15_000).Output, port);

    /// <summary>Quando il cavo viene staccato e riattaccato adb perde la regola reverse: la ricrea se manca. Restituisce true se l'ha ricreata.</summary>
    public static bool EnsureReverseIfMissing(int port)
    {
        if (HasReverse(port)) return false;
        EnsureReverse(port);
        return true;
    }

    /// <summary>Esegue <c>adb reverse tcp:port tcp:port</c>; lancia se fallisce (nessun tablet, non autorizzato, adb mancante).</summary>
    public static void EnsureReverse(int port)
    {
        var (code, output) = Run($"reverse tcp:{port} tcp:{port}", timeoutMs: 15_000);
        if (code != 0) throw new InvalidOperationException($"adb reverse non riuscito (codice {code}): {output.Trim()}");
    }

    public static (int ExitCode, string Output) Run(string args, int timeoutMs)
    {
        var psi = new ProcessStartInfo(AdbPath(), args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("impossibile avviare adb");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeoutMs))
        {
            try { p.Kill(true); } catch { }
            throw new TimeoutException($"adb {args} non risponde");
        }
        Task.WaitAll(stdout, stderr);
        return (p.ExitCode, stdout.Result + stderr.Result);
    }
}
