using FreeExtend.Host.Capture;

namespace FreeExtend.Host.Encode;

/// <summary>Verifica manuale dell'encoder (Task 6): cattura -> codifica -> file .h264 con statistiche.</summary>
internal static class EncodeTest
{
    public static int Run(string[] args)
    {
        string? output = null, outFile = null;
        int seconds = 10, fps = 60, kbps = 40_000, keyframeAtSec = 0;
        int? quality = null, outW = null, outH = null; int mftIndex = 0;
        for (int i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--output": output = args[i + 1]; break;
                case "--out": outFile = args[i + 1]; break;
                case "--seconds": seconds = int.Parse(args[i + 1]); break;
                case "--fps": fps = int.Parse(args[i + 1]); break;
                case "--kbps": kbps = int.Parse(args[i + 1]); break;
                case "--keyframe-at": keyframeAtSec = int.Parse(args[i + 1]); break;
                case "--quality": quality = int.Parse(args[i + 1]); break;
                case "--width": outW = int.Parse(args[i + 1]); break;
                case "--mft": mftIndex = int.Parse(args[i + 1]); break;
                case "--height": outH = int.Parse(args[i + 1]); break;
            }
        }

        using var source = new DesktopDuplicationSource(output);
        using var encoder = new MfH264Encoder(source.Device, new EncoderConfig(outW ?? source.Width, outH ?? source.Height, fps, kbps, quality, source.Width, source.Height, mftIndex));
        Console.WriteLine($"Encoder: {encoder.EncoderName}  {outW ?? source.Width}x{outH ?? source.Height} (sorgente {source.Width}x{source.Height}) @{fps} fps, {kbps} kbit/s");
        foreach (var line in encoder.CodecApiLog) Console.WriteLine($"  ICodecAPI {line}");

        var latenciesUs = new List<double>();
        var captureTimes = new List<long>();
        long bytes = 0; int packets = 0, keyframes = 0;
        bool firstDone = false, orderOk = true;
        using var file = outFile != null ? File.Create(outFile) : null;
        var gate = new object();

        encoder.PacketReady += p =>
        {
            long now = CaptureClock.NowUs();
            lock (gate)
            {
                packets++; bytes += p.Data.Length;
                if (p.IsKeyframe) keyframes++;
                if (captureTimes.Count > 0 && p.CaptureUs < captureTimes[^1]) orderOk = false;
                captureTimes.Add(p.CaptureUs);
                latenciesUs.Add(now - p.CaptureUs);
                file?.Write(p.Data.Span);
                if (!firstDone)
                {
                    firstDone = true;
                    var nals = NalInspector.Parse(p.Data.Span);
                    Console.WriteLine($"Primo pacchetto: {p.Data.Length} byte, NAL = [{string.Join(", ", nals.Select(n => n.Type))}] (7=SPS 8=PPS 5=IDR), keyframe={p.IsKeyframe}");
                }
            }
        };

        long start = Environment.TickCount64;
        int offered = 0, accepted = 0;
        bool forcedOnce = false;
        while (Environment.TickCount64 - start < seconds * 1000L)
        {
            if (!source.TryAcquireFrame(50, out var frame, encoder.WantsFrame)) continue;
            offered++;
            bool force = keyframeAtSec > 0 && !forcedOnce && Environment.TickCount64 - start >= keyframeAtSec * 1000L;
            if (encoder.Encode(frame, force))
            {
                accepted++;
                if (force) forcedOnce = true;
            }
        }
        Thread.Sleep(300); // lascia uscire gli ultimi pacchetti

        lock (gate)
        {
            Console.WriteLine($"Frame offerti: {offered}, codificati: {accepted}, saltati per fps: {encoder.SkippedForRate}, scartati (encoder occupato): {encoder.DroppedBusy}");
            Console.WriteLine($"Pacchetti: {packets}, keyframe: {keyframes}, {bytes / 1024.0 / 1024.0:F2} MiB, {bytes * 8.0 / seconds / 1e6:F1} Mbit/s medi");
            Console.WriteLine($"Ordine dei pacchetti = ordine di ingresso: {orderOk}");
            if (latenciesUs.Count > 0)
            {
                var sorted = latenciesUs.OrderBy(x => x).ToList();
                Console.WriteLine($"Latenza cattura->pacchetto: media {latenciesUs.Average() / 1000:F2} ms, p50 {sorted[sorted.Count / 2] / 1000:F2} ms, p95 {sorted[(int)(sorted.Count * 0.95)] / 1000:F2} ms, max {sorted[^1] / 1000:F2} ms");
            }
            var t = encoder.Timing;
            Console.WriteLine($"Dove va il tempo (media su {t.Frames} frame): preparazione {t.PrepareMs:F2} ms (cattura->ingresso encoder), codifica {t.EncodeMs:F2} ms (ingresso->pacchetto)");
            if (encoder.BltGpuMs > 0) Console.WriteLine($"Conversione colore (attesa GPU, solo con FX_SYNC_BLT=1): {encoder.BltGpuMs:F2} ms");
            Console.WriteLine($"SPS+PPS noti: {encoder.ParameterSets.Length} byte");
        }
        return packets > 0 ? 0 : 2;
    }
}
