using FreeExtend.Host.Capture;

namespace FreeExtend.Host.Stats;

/// <summary>Statistiche sulla finestra mobile degli ultimi 5 secondi (percentili a rango piu vicino).</summary>
public readonly record struct StatsSnapshot(
    double Fps, double RttMs, double PipelineP50Ms, double PipelineP95Ms, double EncodeMsAvg, long DroppedFrames);

/// <summary>Statistiche su tutta la durata della misura.</summary>
public readonly record struct StatsSummary(
    long Samples, double PipelineP50Ms, double PipelineP95Ms, double PipelineMaxMs, double RttMedianMs, double EncodeMsAvg, long DroppedFrames);

/// <summary>
/// Latenza della pipeline dalla cattura al disegno sul tablet. Il tablet conferma ogni frame disegnato (Rendered) con il
/// timestamp di cattura dell'host; i due orologi non si confrontano mai tra loro: la latenza si misura tutta sull'orologio
/// dell'host come <c>(ora di ricezione dell'ack - cattura) - RTT/2</c>, dove l'RTT e la mediana degli ultimi Ping/Pong
/// (il tragitto di ritorno dell'ack si toglie a meta). Non comprende il tempo di scansione del pannello del tablet.
/// </summary>
public sealed class LatencyStats
{
    private const long WindowUs = 5_000_000;
    private const long FpsWindowUs = 1_000_000;
    private const int RttSamples = 16;

    private readonly Func<long> _nowUs;
    private readonly object _gate = new();
    private readonly Queue<double> _rtts = new();
    private readonly Queue<(long AtUs, double Ms)> _window = new();
    private readonly List<double> _all = new();
    private readonly Queue<long> _frames = new();
    private double _encodeSum;
    private long _encodeCount, _dropped;

    public LatencyStats(Func<long>? nowUs = null) => _nowUs = nowUs ?? CaptureClock.NowUs;

    /// <param name="sentUs">Timestamp scritto nel Ping, ripetuto dal tablet nel Pong.</param>
    /// <param name="recvUs">Istante di ricezione del Pong sull'orologio dell'host.</param>
    public void OnPong(long sentUs, long recvUs)
    {
        lock (_gate)
        {
            _rtts.Enqueue((recvUs - sentUs) / 1000.0);
            while (_rtts.Count > RttSamples) _rtts.Dequeue();
        }
    }

    public void OnRendered(long captureUs, long nowUs)
    {
        lock (_gate)
        {
            double ms = Math.Max(0, (nowUs - captureUs) / 1000.0 - MedianRtt() / 2);
            _window.Enqueue((nowUs, ms));
            _all.Add(ms);
        }
    }

    public void OnFrameSent()
    {
        lock (_gate) _frames.Enqueue(_nowUs());
    }

    public void OnEncode(double ms)
    {
        lock (_gate) { _encodeSum += ms; _encodeCount++; }
    }

    public void OnDropped()
    {
        lock (_gate) _dropped++;
    }

    public StatsSnapshot Snapshot()
    {
        lock (_gate)
        {
            long now = _nowUs();
            while (_window.Count > 0 && _window.Peek().AtUs < now - WindowUs) _window.Dequeue();
            while (_frames.Count > 0 && _frames.Peek() <= now - FpsWindowUs) _frames.Dequeue();

            var sorted = _window.Select(w => w.Ms).OrderBy(x => x).ToList();
            return new StatsSnapshot(
                Fps: _frames.Count / (FpsWindowUs / 1e6),
                RttMs: MedianRtt(),
                PipelineP50Ms: Percentile(sorted, 0.50),
                PipelineP95Ms: Percentile(sorted, 0.95),
                EncodeMsAvg: _encodeCount == 0 ? 0 : _encodeSum / _encodeCount,
                DroppedFrames: _dropped);
        }
    }

    public StatsSummary Summary()
    {
        lock (_gate)
        {
            var sorted = _all.OrderBy(x => x).ToList();
            return new StatsSummary(
                Samples: sorted.Count,
                PipelineP50Ms: Percentile(sorted, 0.50),
                PipelineP95Ms: Percentile(sorted, 0.95),
                PipelineMaxMs: sorted.Count == 0 ? 0 : sorted[^1],
                RttMedianMs: MedianRtt(),
                EncodeMsAvg: _encodeCount == 0 ? 0 : _encodeSum / _encodeCount,
                DroppedFrames: _dropped);
        }
    }

    private double MedianRtt()
    {
        if (_rtts.Count == 0) return 0;
        var s = _rtts.OrderBy(x => x).ToArray();
        return s.Length % 2 == 1 ? s[s.Length / 2] : (s[s.Length / 2 - 1] + s[s.Length / 2]) / 2.0;
    }

    private static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        int idx = (int)Math.Ceiling(p * sorted.Count) - 1;
        return sorted[Math.Clamp(idx, 0, sorted.Count - 1)];
    }
}
