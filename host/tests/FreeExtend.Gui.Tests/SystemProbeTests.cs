using System.ComponentModel;

namespace FreeExtend.Gui.Tests;

public class SystemProbeTests
{
    private static SystemProbe Probe(Func<string, int, (int ExitCode, string Output)> runAdb) =>
        new(new ProjectPaths(@"C:\x"), runAdb);

    private static Func<string, int, (int, string)> Fake(string devices, string model = "SM-X520\r\n", string packages = "")
        => (args, _) =>
        {
            if (args == "devices") return (0, devices);
            if (args.StartsWith("shell getprop")) return (0, model);
            if (args.StartsWith("shell pm list packages")) return (0, packages);
            return (1, "unexpected: " + args);
        };

    [Fact]
    public void Adb_that_cannot_be_started_means_NoAdb()
    {
        var t = Probe((_, _) => throw new Win32Exception(2)).ProbeTablet(withApp: true);
        Assert.Equal(TabletState.NoAdb, t.State);
        Assert.Null(t.AppInstalled);
    }

    [Fact]
    public void Missing_adb_file_means_NoAdb() =>
        Assert.Equal(TabletState.NoAdb, Probe((_, _) => throw new FileNotFoundException()).ProbeTablet(true).State);

    [Fact]
    public void A_hung_adb_is_treated_as_no_tablet_not_a_crash() =>
        Assert.Equal(TabletState.None, Probe((_, _) => throw new TimeoutException()).ProbeTablet(true).State);

    [Fact]
    public void Empty_device_list_means_no_tablet()
    {
        var t = Probe(Fake("List of devices attached\r\n\r\n")).ProbeTablet(true);
        Assert.Equal(TabletState.None, t.State);
        Assert.Null(t.AppInstalled);
    }

    [Fact]
    public void Unauthorized_device_is_reported_and_the_app_is_not_checked()
    {
        var t = Probe(Fake("List of devices attached\r\nXYZ\tunauthorized\r\n")).ProbeTablet(true);
        Assert.Equal(TabletState.Unauthorized, t.State);
        Assert.Null(t.AppInstalled);
    }

    [Fact]
    public void Ready_tablet_without_the_app()
    {
        var t = Probe(Fake("List of devices attached\r\nXYZ\tdevice\r\n")).ProbeTablet(true);
        Assert.Equal(TabletState.Ready, t.State);
        Assert.Equal("SM-X520", t.Model);
        Assert.False(t.AppInstalled);
    }

    [Fact]
    public void Ready_tablet_with_the_app()
    {
        var t = Probe(Fake("List of devices attached\r\nXYZ\tdevice\r\n", packages: "package:it.freeextend.display\r\n")).ProbeTablet(true);
        Assert.True(t.AppInstalled);
    }

    [Fact]
    public void The_light_poll_does_not_look_for_the_app()
    {
        var calls = new List<string>();
        Func<string, int, (int, string)> run = (a, ms) => { calls.Add(a); return Fake("List of devices attached\r\nXYZ\tdevice\r\n")(a, ms); };
        var t = Probe(run).ProbeTablet(withApp: false);
        Assert.Equal(TabletState.Ready, t.State);
        Assert.Null(t.AppInstalled);
        Assert.DoesNotContain(calls, c => c.Contains("pm list packages"));
    }
}
