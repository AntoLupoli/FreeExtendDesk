namespace FreeExtend.Gui.Tests;

public class UninstallerTests
{
    private static readonly ProjectPaths Paths = new(@"C:\fx-uninstaller-test-root");

    private static Func<string, bool, Task<ScriptResult>> Script(int exit, bool cancelled = false, string log = "") =>
        (_, _) => Task.FromResult(new ScriptResult(exit, cancelled, log));

    [Fact]
    public async Task Refusing_the_admin_prompt_cancels_the_whole_uninstall()
    {
        var r = await Uninstaller.RunAsync(Paths, tabletReady: true, Script(-1, cancelled: true));
        Assert.True(r.Cancelled);
        Assert.False(r.DriverRemoved);
        Assert.False(r.GpuPreferenceRemoved);      // nothing else is touched when the user backed out
        Assert.Null(r.AppRemoved);
        Assert.Null(r.Error);
    }

    [Fact]
    public async Task A_successful_driver_removal_continues_with_the_graphics_preference()
    {
        var r = await Uninstaller.RunAsync(Paths, tabletReady: false, Script(0));
        Assert.False(r.Cancelled);
        Assert.True(r.DriverRemoved);
        Assert.True(r.GpuPreferenceRemoved);
        Assert.Null(r.AppRemoved);                 // tablet not connected: the app stays on it
        Assert.Null(r.Error);
    }

    [Fact]
    public async Task A_failed_driver_removal_is_reported_but_the_other_steps_still_run()
    {
        var r = await Uninstaller.RunAsync(Paths, tabletReady: false, Script(1, log: "boom"));
        Assert.False(r.DriverRemoved);
        Assert.True(r.GpuPreferenceRemoved);
        Assert.Contains("Driver removal failed", r.Error);
        Assert.Contains("boom", r.Error);
    }
}
