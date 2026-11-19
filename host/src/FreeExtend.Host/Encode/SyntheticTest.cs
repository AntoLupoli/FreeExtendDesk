using FreeExtend.Host.Capture;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FreeExtend.Host.Encode;

/// <summary>
/// Diagnostica (comando <c>encode-synthetic</c>): alimenta l'encoder con frame BGRA sintetici alla massima velocita,
/// senza cattura. Separa il limite dell'encoder/GPU da quello della catena di cattura.
/// </summary>
internal static class SyntheticTest
{
    public static int Run(string[] args)
    {
        string? output = null; int seconds = 6, fps = 240, kbps = 40_000, mftIndex = 0, pace = 0; string? captureMode = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--output": output = args[i + 1]; break;
                case "--seconds": seconds = int.Parse(args[i + 1]); break;
                case "--fps": fps = int.Parse(args[i + 1]); break;
                case "--kbps": kbps = int.Parse(args[i + 1]); break;
                case "--mft": mftIndex = int.Parse(args[i + 1]); break;
                case "--pace": pace = int.Parse(args[i + 1]); break;   // frame al secondo offerti (0 = il piu veloce possibile)
                case "--capture": captureMode = args[i + 1]; break;    // copy | nocopy: cattura vera in parallelo (diagnostica)
            }
        }

        using var source = new DesktopDuplicationSource(output);          // serve solo per avere il device sull'adattatore giusto
        var device = source.Device;
        int w = source.Width, h = source.Height;

        ID3D11Texture2D[] frames = new ID3D11Texture2D[2];
        var rnd = new Random(1);
        for (int k = 0; k < 2; k++)
        {
            var data = new byte[w * h * 4];
            // gradiente + rumore leggero: contenuto "reale" per il rate control, diverso fra i due frame
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int o = (y * w + x) * 4;
                    data[o] = (byte)(x * 255 / w + (k * 40) + rnd.Next(8));
                    data[o + 1] = (byte)(y * 255 / h + rnd.Next(8));
                    data[o + 2] = (byte)((x + y + k * 90) & 0xFF);
                    data[o + 3] = 255;
                }
            var gh = System.Runtime.InteropServices.GCHandle.Alloc(data, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                frames[k] = device.CreateTexture2D(new Texture2DDescription
                {
                    Width = (uint)w, Height = (uint)h, MipLevels = 1, ArraySize = 1, Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Default,
                    BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                }, new[] { new SubresourceData(gh.AddrOfPinnedObject(), (uint)(w * 4)) });
            }
            finally { gh.Free(); }
        }

        using var encoder = new MfH264Encoder(device, new EncoderConfig(w, h, fps, kbps, null, w, h, mftIndex));
        bool stopCapture = false; long captured = 0;
        Thread? capThread = null;
        if (captureMode != null)
        {
            capThread = new Thread(() => { while (!stopCapture) { if (source.TryAcquireFrame(50, out _, captureMode == "nocopy" ? () => false : null)) captured++; } }) { IsBackground = true };
            capThread.Start();
        }
        Console.WriteLine($"Synthetic: {encoder.EncoderName} {w}x{h}, requested up to {fps} fps");
        var lat = new List<double>(); int packets = 0;
        var gate = new object();
        encoder.PacketReady += p => { lock (gate) { packets++; lat.Add(CaptureClock.NowUs() - p.CaptureUs); } };

        long start = Environment.TickCount64; int n = 0, offered = 0;
        long nextUs = CaptureClock.NowUs(); long paceUs = pace > 0 ? 1_000_000L / pace : 0;
        while (Environment.TickCount64 - start < seconds * 1000L)
        {
            offered++;
            if (paceUs > 0) { while (CaptureClock.NowUs() < nextUs) Thread.Sleep(0); nextUs += paceUs; }
            if (encoder.Encode(new CapturedFrame(frames[n & 1], CaptureClock.NowUs()), false)) n++;
            else Thread.Yield();
        }
        Thread.Sleep(300);
        stopCapture = true; capThread?.Join(500);
        if (captureMode != null) Console.WriteLine($"Parallel capture ({captureMode}): {captured} frames copied");
        lock (gate)
        {
            var s = lat.OrderBy(x => x).ToList();
            Console.WriteLine($"Encoded {n} frames in {seconds} s = {n / (double)seconds:F1} fps; packets {packets}");
            if (s.Count > 0) Console.WriteLine($"Input->packet latency: mean {s.Average() / 1000:F2} ms, p50 {s[s.Count / 2] / 1000:F2} ms, p95 {s[(int)(s.Count * 0.95)] / 1000:F2} ms");
            var t = encoder.Timing;
            Console.WriteLine($"Detail: prepare {t.PrepareMs:F2} ms, encode {t.EncodeMs:F2} ms");
        }
        return 0;
    }
}

/// <summary>
/// Verifica dello spegnimento dell'encoder (comando <c>encoder-cycle-test</c>, revisione I7): crea e distrugge l'encoder piu volte di fila
/// con del lavoro in corso. Se lo spegnimento fosse scorretto il thread degli eventi resterebbe bloccato nell'MFT e le sessioni NVENC
/// si accumulerebbero fino a esaurire il limite della scheda.
/// </summary>
internal static class EncoderCycleTest
{
    public static int Run(string[] args)
    {
        string? output = null; int cycles = 6;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--output") output = args[i + 1];
            else if (args[i] == "--cycles") cycles = int.Parse(args[i + 1]);
        }
        using var source = new DesktopDuplicationSource(output);
        int w = source.Width, h = source.Height, failures = 0;
        using var tex = source.Device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)w, Height = (uint)h, MipLevels = 1, ArraySize = 1, Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Default, BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
        });
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int c = 1; c <= cycles; c++)
        {
            var encoder = new MfH264Encoder(source.Device, new EncoderConfig(w, h, 60, 20_000));
            int packets = 0;
            encoder.PacketReady += _ => Interlocked.Increment(ref packets);
            for (int f = 0; f < 30; f++)
            {
                encoder.Encode(new CapturedFrame(tex, CaptureClock.NowUs()), f == 0);
                Thread.Sleep(10);
            }
            long t0 = sw.ElapsedMilliseconds;
            encoder.Dispose();
            long disposeMs = sw.ElapsedMilliseconds - t0;
            Console.WriteLine($"cycle {c}: packets {packets}, Dispose {disposeMs} ms, event thread stopped = {encoder.EventThreadStopped}");
            if (!encoder.EventThreadStopped) failures++;
        }
        Console.WriteLine(failures == 0 ? $"OK: {cycles} cycles without stuck threads." : $"FAILED: {failures} cycles with a stuck thread.");
        return failures == 0 ? 0 : 1;
    }
}
