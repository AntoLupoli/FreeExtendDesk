using FreeExtend.Host.Display;

namespace FreeExtend.Host.Tests;

public class GpuAdaptersTests
{
    [Theory]
    [InlineData("NVIDIA GeForce RTX 3050 Laptop GPU")]
    [InlineData("AMD Radeon(TM) Graphics")]
    [InlineData("Intel(R) UHD Graphics")]
    public void Real_adapters_are_accepted(string name) => Assert.True(GpuAdapters.IsRealAdapter(name));

    [Theory]
    [InlineData("Microsoft Basic Render Driver")]
    [InlineData("Virtual Display Driver by MTT")]
    [InlineData("Parsec Virtual Display Adapter")]
    [InlineData("Remote Display Adapter")]
    [InlineData("")]
    public void Software_and_virtual_adapters_are_rejected(string name) => Assert.False(GpuAdapters.IsRealAdapter(name));
}
