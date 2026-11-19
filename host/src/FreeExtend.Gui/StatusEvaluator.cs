namespace FreeExtend.Gui;

internal enum TabletState { NoAdb, None, Unauthorized, Ready }
internal enum CheckState { Ok, Missing, Unknown, Warning }
internal enum RequirementId { Driver, HostProgram, Gpu, GpuPreference, Tablet, TabletApp }

/// <summary>Everything the window needs to know about this PC and the tablet at one moment. Null booleans mean "not known yet / cannot be checked".</summary>
internal sealed record SystemSnapshot(
    bool DriverInstalled, bool HostExeExists, TabletState Tablet, string? TabletModel,
    bool? AppInstalled, IReadOnlyList<string> Gpus, bool? HardwareEncoderFound, bool GpuPreferenceSet);

internal sealed record RequirementRow(RequirementId Id, CheckState State, string Text, string? ActionLabel);

internal static class StatusEvaluator
{
    public static IReadOnlyList<RequirementRow> Evaluate(SystemSnapshot s)
    {
        var rows = new List<RequirementRow>
        {
            s.DriverInstalled
                ? new(RequirementId.Driver, CheckState.Ok, "Virtual display driver installed", null)
                : new(RequirementId.Driver, CheckState.Missing, "Virtual display driver not installed", "Install"),
            s.HostExeExists
                ? new(RequirementId.HostProgram, CheckState.Ok, "FreeExtend host program ready", null)
                : new(RequirementId.HostProgram, CheckState.Missing, "Host program not built: run Start-FreeExtend-Gui.ps1", null),
            GpuRow(s),
        };
        if (s.Gpus.Count >= 2)
            rows.Add(s.GpuPreferenceSet
                ? new(RequirementId.GpuPreference, CheckState.Ok, "High-performance GPU preference set", null)
                : new(RequirementId.GpuPreference, CheckState.Warning, "Dual-GPU laptop: GPU preference not set for FreeExtend.Host.exe", "Set"));
        rows.Add(TabletRow(s));
        rows.Add(s.AppInstalled switch
        {
            null => new(RequirementId.TabletApp, CheckState.Unknown, "Connect the tablet to check the app", null),
            true => new(RequirementId.TabletApp, CheckState.Ok, "FreeExtend app installed on the tablet", null),
            false => new(RequirementId.TabletApp, CheckState.Missing, "FreeExtend app not installed on the tablet", "Install"),
        });
        return rows;
    }

    private static RequirementRow GpuRow(SystemSnapshot s)
    {
        if (s.Gpus.Count == 0) return new(RequirementId.Gpu, CheckState.Missing, "No graphics adapter found", null);
        return s.HardwareEncoderFound switch
        {
            null => new(RequirementId.Gpu, CheckState.Unknown, "Checking the video encoder...", null),
            true => new(RequirementId.Gpu, CheckState.Ok, $"Graphics: {string.Join(", ", s.Gpus)} (hardware H.264 encoder found)", null),
            false => new(RequirementId.Gpu, CheckState.Missing, "No hardware H.264 encoder found: needs an NVIDIA, AMD or Intel GPU", null),
        };
    }

    private static RequirementRow TabletRow(SystemSnapshot s) => s.Tablet switch
    {
        TabletState.NoAdb => new(RequirementId.Tablet, CheckState.Missing, "adb not found: install Android Studio or set ANDROID_HOME", null),
        TabletState.None => new(RequirementId.Tablet, CheckState.Missing, "No tablet connected: plug in the USB-C cable and enable USB debugging", null),
        TabletState.Unauthorized => new(RequirementId.Tablet, CheckState.Warning, "Tablet not authorized: accept \"Allow USB debugging\" on the tablet", null),
        _ => new(RequirementId.Tablet, CheckState.Ok, string.IsNullOrWhiteSpace(s.TabletModel) ? "Tablet connected" : $"Tablet connected: {s.TabletModel}", null),
    };

    /// <summary>Why "Start streaming" must stay disabled, or null if it can start. A missing GPU preference does not block (the host warns by itself).</summary>
    public static string? WhyCannotStart(SystemSnapshot s)
    {
        if (!s.DriverInstalled) return "Virtual display driver not installed";
        if (!s.HostExeExists) return "Host program not built: run Start-FreeExtend-Gui.ps1";
        if (s.Gpus.Count == 0) return "No graphics adapter found";
        if (s.HardwareEncoderFound == null) return "Still checking the video encoder";
        if (s.HardwareEncoderFound == false) return "No hardware H.264 encoder found: needs an NVIDIA, AMD or Intel GPU";
        if (s.Tablet != TabletState.Ready) return TabletRow(s).Text;
        if (s.AppInstalled != true) return "FreeExtend app not installed on the tablet";
        return null;
    }
}
