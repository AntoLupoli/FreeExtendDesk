namespace FreeExtend.Gui.Tests;

public class StatusEvaluatorTests
{
    private static readonly string[] OneGpu = { "NVIDIA GeForce RTX 3050 Laptop GPU" };
    private static readonly string[] TwoGpus = { "NVIDIA GeForce RTX 3050 Laptop GPU", "AMD Radeon(TM) Graphics" };

    private static SystemSnapshot Good() =>
        new(DriverInstalled: true, HostExeExists: true, Tablet: TabletState.Ready, TabletModel: "SM-X520", AppInstalled: true,
            Gpus: OneGpu, HardwareEncoderFound: true, GpuPreferenceSet: true);

    private static RequirementRow Row(SystemSnapshot s, RequirementId id) => StatusEvaluator.Evaluate(s).Single(r => r.Id == id);

    [Fact]
    public void Everything_fine_gives_five_ok_rows_and_can_start()
    {
        var rows = StatusEvaluator.Evaluate(Good());
        Assert.Equal(5, rows.Count);
        Assert.All(rows, r => Assert.Equal(CheckState.Ok, r.State));
        Assert.Null(StatusEvaluator.WhyCannotStart(Good()));
    }

    [Fact]
    public void Driver_missing_offers_install()
    {
        var r = Row(Good() with { DriverInstalled = false }, RequirementId.Driver);
        Assert.Equal(CheckState.Missing, r.State);
        Assert.Equal("Install", r.ActionLabel);
        Assert.Contains("not installed", r.Text);
    }

    [Fact]
    public void Host_missing_has_no_action()
    {
        var r = Row(Good() with { HostExeExists = false }, RequirementId.HostProgram);
        Assert.Equal(CheckState.Missing, r.State);
        Assert.Null(r.ActionLabel);
        Assert.Contains("not built", r.Text);
    }

    [Fact]
    public void Gpu_row_variants()
    {
        Assert.Contains("No graphics adapter", Row(Good() with { Gpus = Array.Empty<string>() }, RequirementId.Gpu).Text);
        Assert.Equal(CheckState.Unknown, Row(Good() with { HardwareEncoderFound = null }, RequirementId.Gpu).State);
        var none = Row(Good() with { HardwareEncoderFound = false }, RequirementId.Gpu);
        Assert.Equal(CheckState.Missing, none.State);
        Assert.Contains("No hardware H.264 encoder", none.Text);
        var ok = Row(Good(), RequirementId.Gpu);
        Assert.Contains("NVIDIA GeForce RTX 3050 Laptop GPU", ok.Text);
        Assert.Contains("hardware H.264 encoder found", ok.Text);
        Assert.Null(ok.ActionLabel);
    }

    [Fact]
    public void Gpu_preference_row_exists_only_with_two_or_more_adapters()
    {
        Assert.DoesNotContain(StatusEvaluator.Evaluate(Good()), r => r.Id == RequirementId.GpuPreference);
        var warn = Row(Good() with { Gpus = TwoGpus, GpuPreferenceSet = false }, RequirementId.GpuPreference);
        Assert.Equal(CheckState.Warning, warn.State);
        Assert.Equal("Set", warn.ActionLabel);
        Assert.Equal(CheckState.Ok, Row(Good() with { Gpus = TwoGpus, GpuPreferenceSet = true }, RequirementId.GpuPreference).State);
    }

    [Fact]
    public void Tablet_row_variants()
    {
        Assert.Contains("adb not found", Row(Good() with { Tablet = TabletState.NoAdb }, RequirementId.Tablet).Text);
        Assert.Contains("No tablet connected", Row(Good() with { Tablet = TabletState.None }, RequirementId.Tablet).Text);
        var unauth = Row(Good() with { Tablet = TabletState.Unauthorized }, RequirementId.Tablet);
        Assert.Equal(CheckState.Warning, unauth.State);
        Assert.Contains("not authorized", unauth.Text);
        Assert.Equal("Tablet connected: SM-X520", Row(Good(), RequirementId.Tablet).Text);
        Assert.Equal("Tablet connected", Row(Good() with { TabletModel = null }, RequirementId.Tablet).Text);
    }

    [Fact]
    public void App_row_variants()
    {
        Assert.Equal(CheckState.Unknown, Row(Good() with { Tablet = TabletState.None, AppInstalled = null }, RequirementId.TabletApp).State);
        var missing = Row(Good() with { AppInstalled = false }, RequirementId.TabletApp);
        Assert.Equal(CheckState.Missing, missing.State);
        Assert.Equal("Install", missing.ActionLabel);
        Assert.Equal(CheckState.Ok, Row(Good(), RequirementId.TabletApp).State);
    }

    [Fact]
    public void WhyCannotStart_reports_the_first_blocker_in_priority_order()
    {
        var allBad = Good() with { DriverInstalled = false, HostExeExists = false, HardwareEncoderFound = false, Tablet = TabletState.None, AppInstalled = null };
        Assert.Contains("Virtual display driver", StatusEvaluator.WhyCannotStart(allBad));
        Assert.Contains("program not built", StatusEvaluator.WhyCannotStart(allBad with { DriverInstalled = true }));
        Assert.Contains("H.264 encoder", StatusEvaluator.WhyCannotStart(allBad with { DriverInstalled = true, HostExeExists = true }));
        Assert.Contains("No tablet connected", StatusEvaluator.WhyCannotStart(allBad with { DriverInstalled = true, HostExeExists = true, HardwareEncoderFound = true }));
        Assert.Contains("app not installed", StatusEvaluator.WhyCannotStart(Good() with { AppInstalled = false }));
    }

    [Fact]
    public void Encoder_still_being_checked_blocks_with_its_own_message() =>
        Assert.Equal("Still checking the video encoder", StatusEvaluator.WhyCannotStart(Good() with { HardwareEncoderFound = null }));

    [Fact]
    public void Missing_gpu_preference_does_not_block() =>
        Assert.Null(StatusEvaluator.WhyCannotStart(Good() with { Gpus = TwoGpus, GpuPreferenceSet = false }));
}
