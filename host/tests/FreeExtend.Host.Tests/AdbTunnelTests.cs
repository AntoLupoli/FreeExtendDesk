using FreeExtend.Host.Net;
using Xunit;

namespace FreeExtend.Host.Tests;

public class AdbTunnelTests
{
    [Fact]
    public void ParseDevices_unauthorized_from_real_output_of_this_machine()
    {
        // Output reale di `adb devices` visto in T1, prima dell'autorizzazione del PC sul tablet.
        string output = "List of devices attached\nABC123DEF456            unauthorized transport_id:1\n\n";
        Assert.Equal(DeviceState.Unauthorized, AdbTunnel.ParseDevices(output));
    }

    [Fact]
    public void ParseDevices_ready_when_a_device_is_authorized()
    {
        string output = "List of devices attached\r\nABC123DEF456\tdevice\r\n";
        Assert.Equal(DeviceState.Ready, AdbTunnel.ParseDevices(output));
    }

    [Fact]
    public void ParseDevices_none_with_only_the_header()
    {
        Assert.Equal(DeviceState.None, AdbTunnel.ParseDevices("List of devices attached\n\n"));
    }

    [Fact]
    public void ParseDevices_ignores_daemon_start_noise_and_offline_devices()
    {
        string output = "* daemon not running; starting now at tcp:5037\n* daemon started successfully\nList of devices attached\nABC123\toffline\n";
        Assert.Equal(DeviceState.None, AdbTunnel.ParseDevices(output));
    }

    [Fact]
    public void ParseDevices_ready_wins_over_unauthorized_when_both_present()
    {
        string output = "List of devices attached\nAAA\tunauthorized\nBBB\tdevice\n";
        Assert.Equal(DeviceState.Ready, AdbTunnel.ParseDevices(output));
    }
}

public class AdbReverseListTests
{
    [Fact]
    public void ParseReverseList_finds_the_port_in_the_real_format()
    {
        // Output reale di `adb reverse --list` su questa macchina
        Assert.True(AdbTunnel.ParseReverseList("UsbFfs tcp:27183 tcp:27183\n", 27183));
    }

    [Fact]
    public void ParseReverseList_false_when_empty_or_other_port()
    {
        Assert.False(AdbTunnel.ParseReverseList("", 27183));
        Assert.False(AdbTunnel.ParseReverseList("UsbFfs tcp:8080 tcp:8080\n", 27183));
    }

    [Fact]
    public void ParseReverseList_does_not_confuse_ports_that_share_a_prefix()
    {
        Assert.False(AdbTunnel.ParseReverseList("UsbFfs tcp:271830 tcp:271830\n", 27183));
        Assert.True(AdbTunnel.ParseReverseList("UsbFfs tcp:8080 tcp:8080\nUsbFfs tcp:27183 tcp:27183\r\n", 27183));
    }
}

public class AdbPathTests
{
    private static Func<string, string?> Env(params (string Name, string Value)[] vars) =>
        name => vars.FirstOrDefault(v => v.Name == name).Value;

    private static Func<string, bool> Exists(params string[] files) => path => files.Contains(path);

    [Fact]
    public void Uses_ANDROID_HOME_when_adb_is_there()
    {
        var path = AdbTunnel.ResolveAdbPath(Env(("ANDROID_HOME", @"D:\Sdk")), Exists(@"D:\Sdk\platform-tools\adb.exe"));
        Assert.Equal(@"D:\Sdk\platform-tools\adb.exe", path);
    }

    [Fact]
    public void Falls_back_to_ANDROID_SDK_ROOT_then_to_the_default_android_studio_location()
    {
        Assert.Equal(@"E:\Android\platform-tools\adb.exe",
            AdbTunnel.ResolveAdbPath(Env(("ANDROID_SDK_ROOT", @"E:\Android")), Exists(@"E:\Android\platform-tools\adb.exe")));

        Assert.Equal(@"C:\Users\x\AppData\Local\Android\Sdk\platform-tools\adb.exe",
            AdbTunnel.ResolveAdbPath(Env(("LOCALAPPDATA", @"C:\Users\x\AppData\Local")), Exists(@"C:\Users\x\AppData\Local\Android\Sdk\platform-tools\adb.exe")));
    }

    [Fact]
    public void ANDROID_HOME_wins_over_the_default_location()
    {
        var path = AdbTunnel.ResolveAdbPath(
            Env(("ANDROID_HOME", @"D:\Sdk"), ("LOCALAPPDATA", @"C:\Users\x\AppData\Local")),
            Exists(@"D:\Sdk\platform-tools\adb.exe", @"C:\Users\x\AppData\Local\Android\Sdk\platform-tools\adb.exe"));
        Assert.Equal(@"D:\Sdk\platform-tools\adb.exe", path);
    }

    [Fact]
    public void Uses_adb_from_PATH_when_no_sdk_is_found()
    {
        Assert.Equal("adb", AdbTunnel.ResolveAdbPath(Env(("ANDROID_HOME", @"D:\Sdk")), Exists()));   // la variabile punta a una cartella senza adb
        Assert.Equal("adb", AdbTunnel.ResolveAdbPath(Env(), Exists()));
    }
}
