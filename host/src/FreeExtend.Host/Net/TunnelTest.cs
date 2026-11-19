using FreeExtend.Host.Capture;
using FreeExtend.Protocol;

namespace FreeExtend.Host.Net;

/// <summary>
/// Misura del cavo (comando <c>tunnel-test</c>, da eseguire con l'app sul tablet aperta): per 20 secondi invia Ping da 64 KiB
/// con al massimo 8 in volo; l'app risponde a ognuno con un Pong. Stampa MB/s sostenuti e RTT.
/// </summary>
internal static class TunnelTest
{
    private const int Port = 27183;
    private const int PingPayloadBytes = 64 * 1024;
    private const int MaxInFlight = 8;

    public static async Task<int> Run(string[] args)
    {
        int seconds = 20;
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "--seconds") seconds = int.Parse(args[i + 1]);

        var state = AdbTunnel.Current();
        Console.WriteLine($"Stato adb: {state}");
        if (state != DeviceState.Ready)
        {
            Console.Error.WriteLine(state == DeviceState.Unauthorized
                ? "Il tablet non ha autorizzato il PC: accetta la richiesta 'Consenti debug USB' sul tablet."
                : "Nessun tablet collegato (cavo USB, debug USB attivo).");
            return 3;
        }
        AdbTunnel.EnsureReverse(Port);
        Console.WriteLine($"adb reverse tcp:{Port} attivo.");

        await using var server = new StreamServer(Port, hello => ConfigPayload.Encode(hello.W, hello.H, 60, ReadOnlySpan<byte>.Empty));
        var connected = new TaskCompletionSource();
        var rtts = new List<double>();
        long pongs = 0, lastPongUs = 0;
        var gate = new object();
        server.ClientConnected += _ => connected.TrySetResult();
        server.PongReceived += (sent, recv) => { lock (gate) { pongs++; lastPongUs = recv; rtts.Add(recv - sent); } };
        server.Start();
        using var cts = new CancellationTokenSource();
        var run = server.RunAsync(cts.Token);

        Console.WriteLine("In attesa dell'app sul tablet (apri FreeExtend, 60 s)...");
        if (await Task.WhenAny(connected.Task, Task.Delay(TimeSpan.FromSeconds(60))) != connected.Task)
        {
            Console.Error.WriteLine("Nessuna connessione dal tablet.");
            cts.Cancel();
            return 4;
        }

        var payload = new byte[PingPayloadBytes];
        long startUs = CaptureClock.NowUs();
        long sent = 0;
        while (CaptureClock.NowUs() - startUs < seconds * 1_000_000L)
        {
            long inFlight; lock (gate) inFlight = sent - pongs;
            if (inFlight >= MaxInFlight) { await Task.Delay(1); continue; }
            server.Send(new Message(MessageType.Ping, 0, CaptureClock.NowUs(), payload));
            sent++;
        }
        // attende le ultime risposte
        for (int i = 0; i < 100; i++) { lock (gate) if (pongs >= sent) break; await Task.Delay(50); }

        double elapsedS;
        lock (gate)
        {
            elapsedS = ((lastPongUs == 0 ? CaptureClock.NowUs() : lastPongUs) - startUs) / 1e6;
            double mb = pongs * (double)PingPayloadBytes / (1024 * 1024);
            Console.WriteLine($"Ping inviati: {sent}, risposte: {pongs}, dati: {mb:F1} MiB in {elapsedS:F1} s = {mb / elapsedS:F1} MiB/s ({mb * 8 / elapsedS * 1.048576:F0} Mbit/s)");
            if (rtts.Count > 0)
            {
                var s = rtts.OrderBy(x => x).ToList();
                Console.WriteLine($"RTT con coda (64 KiB per ping, {MaxInFlight} in volo): p50 {s[s.Count / 2] / 1000:F2} ms, p95 {s[(int)(s.Count * 0.95)] / 1000:F2} ms, min {s[0] / 1000:F2} ms");
            }
        }
        cts.Cancel();
        return 0;
    }
}
