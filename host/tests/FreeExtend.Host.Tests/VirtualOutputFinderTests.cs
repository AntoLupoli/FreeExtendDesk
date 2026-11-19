using FreeExtend.Host.Display;
using Xunit;

namespace FreeExtend.Host.Tests;

public class VirtualOutputFinderTests
{
    [Fact]
    public void Finds_the_virtual_display_by_its_adapter_description_whatever_its_number()
    {
        var devices = new[]
        {
            (@"\\.\DISPLAY1", "NVIDIA GeForce RTX 3050 Laptop GPU"),
            (@"\\.\DISPLAY60", "Virtual Display Driver"),        // il numero cambia a ogni reload del driver (58, 59, 60...)
        };
        Assert.Equal(@"\\.\DISPLAY60", VirtualOutputFinder.Pick(devices));
    }

    [Fact]
    public void Returns_null_when_the_driver_is_not_installed()
    {
        var devices = new[] { (@"\\.\DISPLAY1", "AMD Radeon(TM) Graphics") };
        Assert.Null(VirtualOutputFinder.Pick(devices));
    }

    [Fact]
    public void Matching_is_case_insensitive_and_takes_the_first_when_several()
    {
        var devices = new[] { (@"\\.\DISPLAY7", "virtual display driver"), (@"\\.\DISPLAY8", "Virtual Display Driver") };
        Assert.Equal(@"\\.\DISPLAY7", VirtualOutputFinder.Pick(devices));
    }
}
