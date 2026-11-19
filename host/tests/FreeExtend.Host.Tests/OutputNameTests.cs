using FreeExtend.Host.Capture;
using Xunit;

namespace FreeExtend.Host.Tests;

public class OutputNameTests
{
    [Theory]
    [InlineData(@"\.\DISPLAY58", @"\.\DISPLAY58")]
    [InlineData(@"\.\DISPLAY58", "DISPLAY58")]
    [InlineData(@"\.\DISPLAY58", "display58")]
    public void Matches_full_and_short_names(string deviceName, string wanted)
    {
        Assert.True(DesktopDuplicationSource.NameMatches(deviceName, wanted));
    }

    [Theory]
    [InlineData(@"\.\DISPLAY58", "DISPLAY5")]
    [InlineData(@"\.\DISPLAY1", "DISPLAY58")]
    public void Does_not_match_other_outputs(string deviceName, string wanted)
    {
        Assert.False(DesktopDuplicationSource.NameMatches(deviceName, wanted));
    }
}
