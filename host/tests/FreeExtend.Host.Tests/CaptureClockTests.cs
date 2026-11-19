using FreeExtend.Host.Capture;
using Xunit;

namespace FreeExtend.Host.Tests;

public class CaptureClockTests
{
    [Fact]
    public void NowUs_is_monotonic_and_microsecond_scaled()
    {
        long a = CaptureClock.NowUs();
        Thread.Sleep(20);
        long b = CaptureClock.NowUs();

        long delta = b - a;
        // 20 ms di sleep = circa 20.000 microsecondi (margine per lo scheduler di Windows)
        Assert.InRange(delta, 19_000, 60_000);
    }

    [Fact]
    public void NowUs_never_goes_backwards()
    {
        long prev = CaptureClock.NowUs();
        for (int i = 0; i < 10_000; i++)
        {
            long now = CaptureClock.NowUs();
            Assert.True(now >= prev);
            prev = now;
        }
    }
}
