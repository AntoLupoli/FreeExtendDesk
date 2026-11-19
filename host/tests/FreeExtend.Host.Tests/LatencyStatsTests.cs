using FreeExtend.Host.Stats;
using Xunit;

namespace FreeExtend.Host.Tests;

public class LatencyStatsTests
{
    private sealed class FakeClock
    {
        public long Us;
        public long Now() => Us;
    }

    private static (LatencyStats Stats, FakeClock Clock) Create()
    {
        var clock = new FakeClock();
        return (new LatencyStats(clock.Now), clock);
    }

    [Fact]
    public void Pipeline_subtracts_half_the_rtt()
    {
        var (stats, clock) = Create();
        stats.OnPong(sentUs: 0, recvUs: 10_000);                       // RTT 10 ms
        clock.Us = 1_040_000;

        stats.OnRendered(captureUs: 1_000_000, nowUs: 1_040_000);      // 40 ms dalla cattura al disegno

        var s = stats.Snapshot();
        Assert.Equal(10.0, s.RttMs, 3);
        Assert.Equal(35.0, s.PipelineP50Ms, 3);                        // 40 - 10/2
    }

    [Fact]
    public void Percentiles_over_the_window_use_nearest_rank()
    {
        var (stats, clock) = Create();
        for (int ms = 1; ms <= 100; ms++)                              // RTT sconosciuto = 0: pipeline = latenza grezza
        {
            clock.Us = 1_000_000 + ms * 1000L;
            stats.OnRendered(captureUs: 1_000_000, nowUs: clock.Us);
        }

        var s = stats.Snapshot();
        Assert.Equal(50.0, s.PipelineP50Ms, 3);
        Assert.Equal(95.0, s.PipelineP95Ms, 3);
    }

    [Fact]
    public void Samples_older_than_five_seconds_leave_the_window_but_stay_in_the_summary()
    {
        var (stats, clock) = Create();
        clock.Us = 1_000_000;
        stats.OnRendered(captureUs: 900_000, nowUs: 1_000_000);        // 100 ms
        clock.Us = 7_000_000;                                          // 6 s dopo
        stats.OnRendered(captureUs: 6_980_000, nowUs: 7_000_000);      // 20 ms

        var window = stats.Snapshot();
        Assert.Equal(20.0, window.PipelineP50Ms, 3);                   // il campione da 100 ms e fuori finestra

        var all = stats.Summary();
        Assert.Equal(2, all.Samples);
        Assert.Equal(100.0, all.PipelineMaxMs, 3);
    }

    [Fact]
    public void Fps_counts_frames_sent_in_the_last_second()
    {
        var (stats, clock) = Create();
        for (int i = 0; i < 30; i++)
        {
            clock.Us = i * 30_000L;                                    // 30 frame in 0,87 s
            stats.OnFrameSent();
        }
        clock.Us = 900_000;
        Assert.Equal(30.0, stats.Snapshot().Fps, 3);

        clock.Us = 1_500_000;                                          // i frame fino a t=0,5 s sono fuori dall'ultimo secondo
        Assert.True(stats.Snapshot().Fps < 30.0);
    }

    [Fact]
    public void Encode_average_and_dropped_counters()
    {
        var (stats, _) = Create();
        stats.OnEncode(4.0);
        stats.OnEncode(8.0);
        stats.OnDropped();
        stats.OnDropped();
        stats.OnDropped();

        var s = stats.Snapshot();
        Assert.Equal(6.0, s.EncodeMsAvg, 3);
        Assert.Equal(3, s.DroppedFrames);
    }

    [Fact]
    public void Pipeline_never_goes_negative_when_rtt_is_larger_than_the_measured_latency()
    {
        var (stats, clock) = Create();
        stats.OnPong(0, 100_000);                                      // RTT 100 ms
        clock.Us = 1_010_000;
        stats.OnRendered(captureUs: 1_000_000, nowUs: 1_010_000);      // 10 ms - 50 ms = negativo

        Assert.Equal(0.0, stats.Snapshot().PipelineP50Ms, 3);
    }

    [Fact]
    public void Rtt_uses_the_median_of_recent_pongs_not_the_last_one()
    {
        var (stats, clock) = Create();
        clock.Us = 1_000_000;
        stats.OnPong(0, 4_000);
        stats.OnPong(0, 5_000);
        stats.OnPong(0, 6_000);
        stats.OnPong(0, 90_000);                                       // un picco isolato

        Assert.Equal(5.5, stats.Snapshot().RttMs, 3);                  // mediana di 4,5,6,90
    }

    [Fact]
    public void Empty_stats_snapshot_is_all_zero()
    {
        var (stats, _) = Create();
        var s = stats.Snapshot();
        Assert.Equal(0, s.Fps);
        Assert.Equal(0, s.RttMs);
        Assert.Equal(0, s.PipelineP50Ms);
        Assert.Equal(0, s.PipelineP95Ms);
        Assert.Equal(0, s.DroppedFrames);
    }
}
