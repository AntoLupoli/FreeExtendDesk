using System.ComponentModel;
using System.Diagnostics;
using FreeExtend.Host.Display;
using FreeExtend.Host.Net;

namespace FreeExtend.Gui;

/// <summary>
/// Looks at this PC and the tablet and builds a <see cref="SystemSnapshot"/>. Everything is blocking work (adb, child processes): callers
/// use the async methods, which run it off the UI thread. The adb runner is injected so the logic can be tested without a tablet.
/// </summary>
internal sealed class SystemProbe
{
    public const string Package = "it.freeextend.display";

    private readonly ProjectPaths _paths;
    private readonly Func<string, int, (int ExitCode, string Output)> _runAdb;

    public SystemProbe(ProjectPaths paths, Func<string, int, (int ExitCode, string Output)> runAdb)
    {
        _paths = paths;
        _runAdb = runAdb;
    }

    public SystemProbe(ProjectPaths paths) : this(paths, AdbTunnel.Run) { }

    /// <summary>Tablet state, its model when ready, and (if asked) whether the app is installed. Never throws: adb problems become states.</summary>
    internal (TabletState State, string? Model, bool? AppInstalled) ProbeTablet(bool withApp)
    {
        DeviceState device;
        try { device = AdbTunnel.ParseDevices(_runAdb("devices", 15_000).Output); }
        catch (Win32Exception) { return (TabletState.NoAdb, null, null); }
        catch (FileNotFoundException) { return (TabletState.NoAdb, null, null); }
        catch (Exception) { return (TabletState.None, null, null); }          // timeout or any other adb trouble: treat as "no tablet right now"

        switch (device)
        {
            case DeviceState.Unauthorized: return (TabletState.Unauthorized, null, null);
            case DeviceState.None: return (TabletState.None, null, null);
        }

        string? model = null;
        try { model = AdbOutput.ParseModel(_runAdb("shell getprop ro.product.model", 5_000).Output); } catch (Exception) { }
        bool? app = null;
        if (withApp)
        {
            try { app = AdbOutput.IsPackageInstalled(_runAdb($"shell pm list packages {Package}", 10_000).Output, Package); } catch (Exception) { }
        }
        return (TabletState.Ready, model, app);
    }

    public Task<(TabletState State, string? Model)> TabletOnlyAsync() =>
        Task.Run(() => { var t = ProbeTablet(withApp: false); return (t.State, t.Model); });

    public Task<SystemSnapshot> CaptureAsync() => Task.Run(() =>
    {
        bool driver = false;
        try { driver = VirtualOutputFinder.Find() != null; } catch (Exception) { }

        bool hostExists = File.Exists(_paths.HostExe);
        var tablet = ProbeTablet(withApp: true);

        IReadOnlyList<string> gpus = Array.Empty<string>();
        try { gpus = GpuAdapters.ListNames(); } catch (Exception) { }

        bool? encoder = null;
        if (hostExists && RunHost("encoder-probe", 20_000) is int code) encoder = code == 0;

        bool preference = false;
        if (hostExists) { try { preference = new GpuPreference().IsHighPerformance(_paths.HostExe); } catch (Exception) { } }

        return new SystemSnapshot(driver, hostExists, tablet.State, tablet.Model, tablet.AppInstalled, gpus, encoder, preference);
    });

    /// <summary>Runs the host exe hidden and returns its exit code, or null if it could not run or did not finish in time.</summary>
    private int? RunHost(string args, int timeoutMs)
    {
        try
        {
            var psi = new ProcessStartInfo(_paths.HostExe, args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return null;
            var o = p.StandardOutput.ReadToEndAsync();          // drained so the child never blocks on a full pipe
            var e = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch (Exception) { } return null; }
            Task.WaitAll(new Task[] { o, e }, 2_000);
            return p.ExitCode;
        }
        catch (Exception) { return null; }
    }
}
