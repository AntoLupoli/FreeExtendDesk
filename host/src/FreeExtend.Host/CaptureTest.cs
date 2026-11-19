using System.Drawing;
using System.Drawing.Imaging;
using FreeExtend.Host.Capture;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FreeExtend.Host;

/// <summary>Verifica manuale della cattura (Task 5): fps effettivi, errori, e un PNG di un frame.</summary>
internal static class CaptureTest
{
    public static int Run(string[] args)
    {
        string? output = null;
        int seconds = 10;
        string? png = null;
        int lossEvery = 0; // secondi; 0 = nessuna simulazione
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--output") output = args[i + 1];
            else if (args[i] == "--seconds") seconds = int.Parse(args[i + 1]);
            else if (args[i] == "--png") png = args[i + 1];
            else if (args[i] == "--simulate-loss-every") lossEvery = int.Parse(args[i + 1]);
        }

        using var source = new DesktopDuplicationSource(output);
        Console.WriteLine($"Cattura {source.Width}x{source.Height} su {output ?? "(prima uscita attaccata)"} per {seconds} s...");

        long start = Environment.TickCount64, lastReport = start;
        int frames = 0, framesThisSecond = 0, timeouts = 0;
        bool pngSaved = false;
        int seconds_elapsed = 0;
        while (Environment.TickCount64 - start < seconds * 1000L)
        {
            if (source.TryAcquireFrame(100, out var frame))
            {
                frames++; framesThisSecond++;
                if (png != null && !pngSaved && frames == 5)
                {
                    SavePng(source, frame.Texture, png);
                    pngSaved = true;
                    Console.WriteLine($"PNG salvato: {png}");
                }
            }
            else timeouts++;

            long now = Environment.TickCount64;
            if (now - lastReport >= 1000)
            {
                Console.WriteLine($"  {framesThisSecond,3} fps  (timeout totali: {timeouts})");
                framesThisSecond = 0; lastReport = now;
                seconds_elapsed++;
                if (lossEvery > 0 && seconds_elapsed % lossEvery == 0)
                {
                    Console.WriteLine("  -- accesso perso (simulato) --");
                    source.SimulateAccessLoss();
                }
            }
        }
        Console.WriteLine($"Totale: {frames} frame, {timeouts} timeout in {seconds} s");
        return frames > 0 ? 0 : 2;
    }

    private static void SavePng(DesktopDuplicationSource source, ID3D11Texture2D texture, string path)
    {
        var desc = texture.Description;
        desc.Usage = ResourceUsage.Staging;
        desc.BindFlags = BindFlags.None;
        desc.CPUAccessFlags = CpuAccessFlags.Read;
        using var staging = source.Device.CreateTexture2D(desc);
        source.Device.ImmediateContext.CopyResource(staging, texture);
        var map = source.Device.ImmediateContext.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            using var bmp = new Bitmap((int)desc.Width, (int)desc.Height, PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            unsafe
            {
                for (int y = 0; y < bmp.Height; y++)
                {
                    Buffer.MemoryCopy((byte*)map.DataPointer + (long)y * map.RowPitch,
                        (byte*)data.Scan0 + (long)y * data.Stride, data.Stride, bmp.Width * 4);
                }
            }
            bmp.UnlockBits(data);
            bmp.Save(path, ImageFormat.Png);
        }
        finally
        {
            source.Device.ImmediateContext.Unmap(staging, 0);
        }
    }
}
