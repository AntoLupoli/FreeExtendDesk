using System.Globalization;
using FreeExtend.Host.Net;

namespace FreeExtend.Host.Tests;

public class HostStatusLineTests
{
    [Theory]
    [InlineData("@@fx waiting", HostStatusKind.Waiting, 0, 0, 0, 0.0)]
    [InlineData("@@fx disconnected", HostStatusKind.Disconnected, 0, 0, 0, 0.0)]
    [InlineData("@@fx connected 2304x1440@60", HostStatusKind.Connected, 2304, 1440, 60, 0.0)]
    [InlineData("@@fx fps 59.8", HostStatusKind.Fps, 0, 0, 0, 59.8)]
    public void Parses_each_status_line(string line, HostStatusKind kind, int w, int h, int hz, double fps)
    {
        Assert.True(HostStatusLine.TryParse(line, out var s));
        Assert.Equal(new HostStatus(kind, w, h, hz, fps), s);
    }

    [Fact]
    public void Format_and_parse_round_trip()
    {
        foreach (var original in new[]
        {
            new HostStatus(HostStatusKind.Waiting),
            new HostStatus(HostStatusKind.Disconnected),
            new HostStatus(HostStatusKind.Connected, 1920, 1200, 90),
            new HostStatus(HostStatusKind.Fps, Fps: 30.5),
        })
        {
            Assert.True(HostStatusLine.TryParse(HostStatusLine.Format(original), out var back));
            Assert.Equal(original, back);
        }
    }

    [Fact]
    public void Fps_uses_the_invariant_culture_in_both_directions()
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("it-IT");
            Assert.Equal("@@fx fps 59.8", HostStatusLine.Format(new HostStatus(HostStatusKind.Fps, Fps: 59.8)));
            Assert.True(HostStatusLine.TryParse("@@fx fps 59.8", out var s));
            Assert.Equal(59.8, s.Fps);
        }
        finally { CultureInfo.CurrentCulture = old; }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("@@fx bogus")]
    [InlineData("@@fx connected abc")]
    [InlineData("@@fx fps abc")]
    public void Rejects_anything_else(string? line) => Assert.False(HostStatusLine.TryParse(line, out _));
}
