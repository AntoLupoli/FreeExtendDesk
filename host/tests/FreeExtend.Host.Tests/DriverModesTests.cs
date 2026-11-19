using FreeExtend.Host.Display;
using Xunit;

namespace FreeExtend.Host.Tests;

public class DriverModesTests
{
    private const string Xml = """
        <?xml version='1.0' encoding='utf-8'?>
        <vdd_settings>
            <monitors><count>1</count></monitors>
            <gpu><friendlyname>NVIDIA GeForce RTX 3050 Laptop GPU</friendlyname></gpu>
            <global>
                <g_refresh_rate>60</g_refresh_rate>
                <g_refresh_rate>90</g_refresh_rate>
            </global>
            <resolutions>
                <resolution><width>2304</width><height>1440</height><refresh_rate>60</refresh_rate></resolution>
                <resolution><width>1920</width><height>1200</height><refresh_rate>30</refresh_rate></resolution>
            </resolutions>
        </vdd_settings>
        """;

    [Fact]
    public void Global_refresh_rates_are_replicated_on_every_resolution()
    {
        var modes = DriverModes.Parse(Xml);

        Assert.Contains(new DisplayMode(2304, 1440, 60), modes);
        Assert.Contains(new DisplayMode(2304, 1440, 90), modes);
        Assert.Contains(new DisplayMode(1920, 1200, 60), modes);
        Assert.Contains(new DisplayMode(1920, 1200, 90), modes);
    }

    [Fact]
    public void Own_refresh_rate_of_a_resolution_is_kept_and_duplicates_are_removed()
    {
        var modes = DriverModes.Parse(Xml);

        Assert.Contains(new DisplayMode(1920, 1200, 30), modes);
        Assert.Equal(modes.Distinct().Count(), modes.Count);           // 2304x1440@60 compare sia come proprio sia come globale
        Assert.Equal(5, modes.Count);                                  // 2304@60,90 + 1920@30,60,90
    }

    [Fact]
    public void Without_a_global_section_only_own_rates_count()
    {
        var modes = DriverModes.Parse("<vdd_settings><resolutions><resolution><width>800</width><height>600</height><refresh_rate>30</refresh_rate></resolution></resolutions></vdd_settings>");
        Assert.Equal(new[] { new DisplayMode(800, 600, 30) }, modes);
    }

    [Fact]
    public void Malformed_or_empty_xml_gives_an_empty_list_instead_of_throwing()
    {
        Assert.Empty(DriverModes.Parse(""));
        Assert.Empty(DriverModes.Parse("<vdd_settings><oops"));
        Assert.Empty(DriverModes.Parse("<vdd_settings/>"));
    }

    [Fact]
    public void The_real_freeextend_config_offers_2304x1440_at_60_and_90()
    {
        var path = FindRepoFile("tools", "virtual-display", "vdd_settings.freeextend.xml");
        var modes = DriverModes.Parse(File.ReadAllText(path));

        Assert.Contains(new DisplayMode(2304, 1440, 60), modes);
        Assert.Contains(new DisplayMode(2304, 1440, 90), modes);
    }

    private static string FindRepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray())))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
    }
}
