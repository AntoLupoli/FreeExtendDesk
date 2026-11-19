using FreeExtend.Host.Display;
using Xunit;

namespace FreeExtend.Host.Tests;

public class ModeSelectorTests
{
    private static readonly DisplayMode Native = new(2304, 1440, 60);

    [Fact]
    public void Exact_match_wins()
    {
        var supported = new[] { new DisplayMode(1920, 1200, 60), Native, new DisplayMode(2560, 1600, 60) };
        Assert.Equal(Native, ModeSelector.Choose(Native, supported));
    }

    [Fact]
    public void Falls_back_to_the_largest_mode_with_the_same_aspect_ratio_not_larger_than_requested()
    {
        // 1920x1200 e 2560x1600 sono 16:10 come 2304x1440; 2560x1600 e piu grande del richiesto: si prende 1920x1200
        var supported = new[] { new DisplayMode(1920, 1200, 60), new DisplayMode(2560, 1600, 60), new DisplayMode(1920, 1080, 60) };
        Assert.Equal(new DisplayMode(1920, 1200, 60), ModeSelector.Choose(Native, supported));
    }

    [Fact]
    public void Refresh_is_never_above_the_request()
    {
        var supported = new[] { new DisplayMode(2304, 1440, 90), new DisplayMode(2304, 1440, 60) };
        Assert.Equal(new DisplayMode(2304, 1440, 60), ModeSelector.Choose(Native, supported));
    }

    [Fact]
    public void Picks_the_highest_refresh_that_does_not_exceed_the_request()
    {
        var supported = new[] { new DisplayMode(2304, 1440, 30), new DisplayMode(2304, 1440, 60), new DisplayMode(2304, 1440, 90) };
        Assert.Equal(new DisplayMode(2304, 1440, 60), ModeSelector.Choose(new DisplayMode(2304, 1440, 75), supported));
    }

    [Fact]
    public void Prefers_the_larger_resolution_over_a_higher_refresh()
    {
        var supported = new[] { new DisplayMode(1920, 1200, 60), new DisplayMode(2304, 1440, 30) };
        Assert.Equal(new DisplayMode(2304, 1440, 30), ModeSelector.Choose(Native, supported));
    }

    [Fact]
    public void Throws_when_nothing_fits()
    {
        var supported = new[] { new DisplayMode(1920, 1080, 60), new DisplayMode(2560, 1600, 60), new DisplayMode(2304, 1440, 90) };
        Assert.Throws<NoSuitableModeException>(() => ModeSelector.Choose(Native, supported));
    }

    [Fact]
    public void Throws_on_an_empty_list()
    {
        Assert.Throws<NoSuitableModeException>(() => ModeSelector.Choose(Native, Array.Empty<DisplayMode>()));
    }

    [Fact]
    public void Picks_90hz_when_the_tablet_can_show_it_and_the_cap_allows_it()
    {
        var supported = new[] { new DisplayMode(2304, 1440, 60), new DisplayMode(2304, 1440, 90) };
        // Hello del tablet con refresh massimo 90 e tetto dell'host a 90 (--fps 90): richiesta = min(90, 90)
        Assert.Equal(new DisplayMode(2304, 1440, 90), ModeSelector.Choose(new DisplayMode(2304, 1440, Math.Min(90, 90)), supported));
    }

    [Fact]
    public void The_host_cap_keeps_the_mode_at_60hz_even_if_the_tablet_offers_90()
    {
        var supported = new[] { new DisplayMode(2304, 1440, 60), new DisplayMode(2304, 1440, 90) };
        // tetto di default dell'host = 60: richiesta = min(90, 60)
        Assert.Equal(new DisplayMode(2304, 1440, 60), ModeSelector.Choose(new DisplayMode(2304, 1440, Math.Min(90, 60)), supported));
    }

    [Fact]
    public void Falls_back_to_60hz_when_the_driver_has_no_90hz_mode()
    {
        var supported = new[] { new DisplayMode(2304, 1440, 60) };
        Assert.Equal(new DisplayMode(2304, 1440, 60), ModeSelector.Choose(new DisplayMode(2304, 1440, 90), supported));
    }
}
