using System.Diagnostics;

namespace FreeExtend.Host.Capture;

/// <summary>Orologio monotono dell'host, in microsecondi. Vale solo sul PC: il tablet non lo confronta mai con il proprio.</summary>
public static class CaptureClock
{
    public static long NowUs() => (long)(Stopwatch.GetTimestamp() * (1_000_000.0 / Stopwatch.Frequency));
}
