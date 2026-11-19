using System.Globalization;
using FreeExtend.Host.Capture;
using FreeExtend.Host.Display;
using FreeExtend.Host.Encode;
using FreeExtend.Host.Net;
using FreeExtend.Host.Stats;
using FreeExtend.Protocol;

namespace FreeExtend.Host;

/// <summary>
/// Comando <c>stream</c>: il programma che si lancia per usare il tablet come secondo monitor.
/// Attende il tablet; quando si connette sceglie la modalita del monitor virtuale in base alla sua risoluzione, attacca il monitor
/// al desktop, cattura, codifica in H.264 e manda i pacchetti attraverso <c>adb reverse</c>. Alla fine stacca il monitor.
/// Ctrl+C per fermare, oppure <c>--seconds N</c> per una misura di durata fissa (riassunto finale).
/// Opzioni: <c>--fps</c> (tetto di refresh, 60; il tablet offre fino a 90), <c>--kbps</c>, <c>--csv file</c>, <c>--no-adb</c>, <c>--keep-display</c>.
/// </summary>
internal static class StreamCommand
{
    private const int Port = 27183;
    private const int PingIntervalMs = 250;

    /// <summary>Monitor virtuale (cattura) + encoder, per un certo modo. Si ricostruisce a ogni connessione del tablet.</summary>
    private sealed class Pipeline : IDisposable
    {
        public DisplayMode Mode { get; }
        public DesktopDuplicationSource Source { get; }
        public MfH264Encoder Encoder { get; }

        public Pipeline(OutputInfo output, DisplayMode mode, int kbps, Action<EncodedPacket> onPacket)
        {
            Mode = mode;
            Source = new DesktopDuplicationSource(output.DeviceName);
            try { Encoder = new MfH264Encoder(Source.Device, new EncoderConfig(Source.Width, Source.Height, mode.RefreshHz, kbps)); }
            catch { Source.Dispose(); throw; }                // se l'encoder non parte la cattura non deve restare aperta
            Encoder.PacketReady += onPacket;
        }

        public void Dispose()
        {
            Encoder.Dispose();
            Source.Dispose();
        }
    }

    public static int Run(string[] args)
    {
        string? csvPath = null;
        int fps = 60, kbps = 40_000, seconds = 0;
        bool skipAdb = false, keepDisplay = false, noCleanup = false;
        int simulateGpuLossAfter = 0;                 // solo per prove: dopo N secondi finge che la GPU sia stata persa
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--fps" && i + 1 < args.Length) fps = int.Parse(args[++i]);
            else if (args[i] == "--kbps" && i + 1 < args.Length) kbps = int.Parse(args[++i]);
            else if (args[i] == "--seconds" && i + 1 < args.Length) seconds = int.Parse(args[++i]);
            else if (args[i] == "--csv" && i + 1 < args.Length) csvPath = args[++i];
            else if (args[i] == "--no-adb") skipAdb = true;
            else if (args[i] == "--keep-display") keepDisplay = true;
            else if (args[i] == "--no-cleanup") noCleanup = true;
            else if (args[i] == "--simulate-gpu-loss-after" && i + 1 < args.Length) simulateGpuLossAfter = int.Parse(args[++i]);
        }

        if (VirtualOutputFinder.Find() == null)
        {
            Console.Error.WriteLine("Virtual Display Driver non installato (vedi tools\\virtual-display\\README.md).");
            return 2;
        }
        var supported = DriverModes.ReadInstalled();
        string virtualName = VirtualOutputFinder.Find()!;
        string? exePath = Environment.ProcessPath;
        if (exePath != null && !new GpuPreference().IsHighPerformance(exePath))
            Console.Error.WriteLine($"Attenzione: per questo eseguibile ({exePath}) non e impostata la preferenza grafica 'Prestazioni elevate'. " +
                $"Su un portatile con due schede l'encoder NVIDIA potrebbe non attivarsi: se la pipeline non parte, lancia \"{exePath}\" setup-gpu.");
        // Un monitor rimasto attaccato da una chiusura anomala non deve restare su un desktop dove nessuno lo vede.
        if (!keepDisplay && !noCleanup)
        {
            try { DisplayController.Detach(virtualName); }
            catch (Exception ex) { Console.Error.WriteLine($"Pulizia iniziale del monitor non riuscita: {ex.Message}"); }
        }
        // Stacca il monitor anche se la finestra della console viene chiusa, al logoff o allo spegnimento.
        if (!keepDisplay)
            ConsoleExitGuard.Register(() => { var n = VirtualOutputFinder.Find(); if (n != null) DisplayController.Detach(n); });
        if (supported.Count == 0)
        {
            Console.Error.WriteLine($"Nessun modo trovato in {DriverModes.InstalledSettingsPath}.");
            return 2;
        }

        if (!skipAdb)
        {
            var state = AdbTunnel.Current();
            if (state != DeviceState.Ready)
            {
                Console.Error.WriteLine(state == DeviceState.Unauthorized
                    ? "Il tablet non ha autorizzato il PC: accetta 'Consenti debug USB' sul tablet."
                    : "Nessun tablet collegato (cavo USB, debug USB attivo).");
                return 3;
            }
            AdbTunnel.EnsureReverse(Port);
        }

        var stats = new LatencyStats();
        var display = new VirtualDisplayDriver();
        var gate = new object();
        Pipeline? pipeline = null;
        DisplayMode? pendingMode = null;
        long bytesOut = 0;

        StreamServer? serverRef = null;
        void OnPacket(EncodedPacket p)
        {
            stats.OnFrameSent();
            stats.OnEncode((CaptureClock.NowUs() - p.CaptureUs) / 1000.0);          // cattura -> pacchetto pronto
            Interlocked.Add(ref bytesOut, p.Data.Length);
            serverRef?.Publish(p);
        }

        // Chiamato dal server all'handshake: sceglie la modalita per questo tablet e restituisce la Config. Se nessun modo del
        // driver va bene lancia: il server chiude quel client e resta in ascolto (ClientError).
        byte[] OnHello((ushort W, ushort H, ushort Hz) hello)
        {
            var requested = new DisplayMode(hello.W, hello.H, Math.Min((int)hello.Hz, fps));
            var mode = ModeSelector.Choose(requested, supported);
            lock (gate) pendingMode = mode;
            byte[] paramSets;
            lock (gate) paramSets = pipeline?.Encoder.ParameterSets ?? Array.Empty<byte>();
            Console.WriteLine($"Tablet chiede {hello.W}x{hello.H}@{hello.Hz}: modo scelto {mode.Width}x{mode.Height}@{mode.RefreshHz}");
            return ConfigPayload.Encode((ushort)mode.Width, (ushort)mode.Height, (ushort)mode.RefreshHz, paramSets);
        }

        var server = new StreamServer(Port, OnHello);
        serverRef = server;
        server.ClientConnected += c => Console.WriteLine($"Tablet connesso: {c.W}x{c.H}@{c.Hz} Hz");
        server.ClientError += e => Console.Error.WriteLine($"Client scartato: {e.Message}");
        server.RenderedAck += ts => stats.OnRendered(ts, CaptureClock.NowUs());
        server.PongReceived += (sent, recv) => stats.OnPong(sent, recv);
        server.Start();
        Console.WriteLine($"In attesa del tablet (porta {Port}). Apri l'app FreeExtend sul tablet. Ctrl+C per fermare.");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        var serverTask = server.RunAsync(cts.Token);

        // Se il cavo viene staccato e riattaccato adb perde la regola reverse: la si ricrea, cosi il tablet si riconnette da solo.
        Thread? adbWatchdog = null;
        if (!skipAdb)
        {
            adbWatchdog = new Thread(() =>
            {
                while (!cts.IsCancellationRequested)
                {
                    try
                    {
                        if (AdbTunnel.Current() == DeviceState.Ready && AdbTunnel.EnsureReverseIfMissing(Port))
                            Console.WriteLine("adb reverse ricreato (cavo riattaccato?).");
                    }
                    catch (Exception) { /* adb non risponde ora: si riprova */ }
                    cts.Token.WaitHandle.WaitOne(3000);
                }
            }) { IsBackground = true, Name = "adb-watchdog" };
            adbWatchdog.Start();
        }

        StreamWriter? csv = csvPath != null ? new StreamWriter(csvPath) : null;
        csv?.WriteLine("t_s,fps,rtt_ms,pipeline_p50_ms,pipeline_p95_ms,encode_ms_avg,dropped,mbit_s,queue");

        var sched = new FrameScheduler();
        bool wasConnected = false;
        CapturedFrame? lastFrame = null;
        long startTick = Environment.TickCount64, lastReport = startTick, lastPing = startTick;
        long disconnectedSince = startTick;                       // dall'ultima volta che c'era un tablet (o dall'avvio)
        long prevBytes = 0, prevDropped = 0;
        int exitCode = 0, pipelineFailures = 0;
        const int MaxPipelineFailures = 3;
        bool gpuLossSimulated = false;
        const long DetachGraceMs = 15_000;                        // senza tablet da tanto tempo il monitor non deve restare nel desktop

        // Butta la pipeline: al giro dopo si ricostruisce da capo (nuovo device D3D11, nuovo encoder) con lo stesso modo.
        void DropPipeline(string reason)
        {
            Console.WriteLine($"Pipeline scartata: {reason}");
            var old = pipeline;
            pipeline = null;
            lastFrame = null;
            sched.Reset();
            try { old?.Dispose(); } catch (Exception ex) { Console.Error.WriteLine($"  chiusura della pipeline: {ex.Message}"); }
        }

        try
        {
            while (!cts.IsCancellationRequested)
            {
                if (seconds > 0 && Environment.TickCount64 - startTick >= seconds * 1000L) break;

                // Nuovo tablet (o nuovo modo, o pipeline persa): attacca il monitor con il modo giusto e ricostruisce cattura+encoder.
                DisplayMode? toApply;
                lock (gate) { toApply = pendingMode; pendingMode = null; }
                if (toApply is { } mode)
                {
                    if (pipeline == null || pipeline.Mode != mode)
                    {
                        if (pipeline != null) DropPipeline("cambio di modo");
                        try
                        {
                            var output = display.EnsureAsync(mode, cts.Token).GetAwaiter().GetResult();
                            pipeline = new Pipeline(output, mode, kbps, OnPacket);
                            Console.WriteLine($"Pipeline pronta: {output.DeviceName} {pipeline.Source.Width}x{pipeline.Source.Height}, encoder {pipeline.Encoder.EncoderName}");
                            sched.Reset();                     // la texture dell'ultima cattura apparteneva alla pipeline vecchia
                        }
                        catch (EncoderUnavailableException ex)
                        {
                            // Problema di configurazione: riprovare non serve. Si spiega cosa fare e si esce.
                            Console.Error.WriteLine($"ERRORE: {ex.Message}");
                            exitCode = 4;
                            break;
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            if (++pipelineFailures >= MaxPipelineFailures)
                            {
                                Console.Error.WriteLine($"ERRORE: la pipeline non parte dopo {pipelineFailures} tentativi: {ex.Message.Trim()}");
                                exitCode = 1;
                                break;
                            }
                            Console.Error.WriteLine($"Pipeline non avviata ({pipelineFailures}/{MaxPipelineFailures}): {ex.Message.Trim()}. Riprovo tra 1 s.");
                            lock (gate) pendingMode = mode;
                            cts.Token.WaitHandle.WaitOne(1000);
                            continue;
                        }
                        pipelineFailures = 0;
                    }
                    sched.RequestKeyframe();                   // un tablet nuovo parte da un keyframe dell'immagine attuale
                }

                var pl = pipeline;
                bool connected = server.IsConnected;
                long nowMs = Environment.TickCount64;
                if (connected != wasConnected)
                {
                    if (!connected) { Console.WriteLine("Tablet disconnesso: in attesa di una nuova connessione..."); disconnectedSince = nowMs; }
                    wasConnected = connected;
                }
                if (connected) disconnectedSince = nowMs;

                if (pl == null) { Thread.Sleep(20); continue; }

                // Senza tablet da DetachGraceMs il monitor torna fuori dal desktop (come scollegare un monitor vero); il prossimo
                // collegamento lo riattacca. Cosi nulla resta su uno schermo invisibile se il cavo resta staccato.
                if (!connected && !keepDisplay && nowMs - disconnectedSince >= DetachGraceMs)
                {
                    DropPipeline($"nessun tablet da {DetachGraceMs / 1000} s");
                    try { display.RemoveAsync().GetAwaiter().GetResult(); Console.WriteLine("Monitor virtuale staccato dal desktop (si riattacca al prossimo collegamento)."); }
                    catch (Exception ex) { Console.Error.WriteLine($"Impossibile staccare il monitor: {ex.Message}"); }
                    continue;
                }

                // GPU persa (TDR, aggiornamento driver, riattivazione dopo sospensione): il device e morto per sempre.
                if (pl.Source.DeviceLost)
                {
                    lock (gate) pendingMode = pl.Mode;
                    DropPipeline("GPU persa (device removed/reset)");
                    cts.Token.WaitHandle.WaitOne(500);
                    continue;
                }

                if (simulateGpuLossAfter > 0 && !gpuLossSimulated && Environment.TickCount64 - startTick >= simulateGpuLossAfter * 1000L)
                {
                    gpuLossSimulated = true;
                    pl.Source.SimulateDeviceLoss();                 // prova: al giro dopo la pipeline va ricostruita
                }

                if (server.TakeKeyframeRequest()) sched.RequestKeyframe();

                try
                {
                    // Si copia SEMPRE il frame consegnato da Windows (una copia GPU): solo la codifica e limitata. Un frame non copiato
                    // non viene riconsegnato, e se era l'ultimo cambiamento il tablet resterebbe sull'immagine vecchia a desktop fermo.
                    if (pl.Source.TryAcquireFrame(20, out var frame))
                    {
                        lastFrame = frame;
                        sched.OnCaptured(Environment.TickCount64);
                        if (connected && pl.Encoder.Encode(frame, sched.PendingKeyframe)) sched.OnEncoded();
                    }
                    else if (lastFrame is { } last
                             && sched.ShouldReencodeLast(Environment.TickCount64, connected, pl.Encoder.WantsFrame())
                             && pl.Encoder.Encode(new CapturedFrame(last.Texture, CaptureClock.NowUs()), sched.PendingKeyframe))
                    {
                        sched.OnEncoded();                     // desktop fermo: l'immagine attuale (o il keyframe in sospeso) parte comunque
                    }
                }
                catch (Exception ex) when (ex is SharpGen.Runtime.SharpGenException or InvalidOperationException)
                {
                    lock (gate) pendingMode = pl.Mode;
                    DropPipeline($"errore GPU/encoder: {ex.Message}");
                    cts.Token.WaitHandle.WaitOne(500);
                    continue;
                }

                long now = Environment.TickCount64;
                if (now - lastPing >= PingIntervalMs)
                {
                    server.Send(new Message(MessageType.Ping, 0, CaptureClock.NowUs(), ReadOnlyMemory<byte>.Empty));
                    lastPing = now;
                }
                if (now - lastReport >= 1000)
                {
                    long dropped = pl.Encoder.DroppedBusy;
                    for (long d = prevDropped; d < dropped; d++) stats.OnDropped();
                    prevDropped = dropped;
                    long by = Interlocked.Read(ref bytesOut);
                    var s = stats.Snapshot();
                    double mbit = (by - prevBytes) * 8 / 1e6;
                    prevBytes = by; lastReport = now;
                    Console.WriteLine($"  {s.Fps,5:F1} fps  RTT {s.RttMs,5:F1} ms  pipeline p50 {s.PipelineP50Ms,5:F1} / p95 {s.PipelineP95Ms,5:F1} ms  codifica {s.EncodeMsAvg,5:F1} ms  {mbit,5:F1} Mbit/s  scartati {s.DroppedFrames}  coda {server.QueuedVideoPackets}");
                    csv?.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"{(now - startTick) / 1000.0:F1},{s.Fps:F1},{s.RttMs:F2},{s.PipelineP50Ms:F2},{s.PipelineP95Ms:F2},{s.EncodeMsAvg:F2},{s.DroppedFrames},{mbit:F2},{server.QueuedVideoPackets}"));
                }
            }
        }
        catch (OperationCanceledException) { /* Ctrl+C durante l'attesa del monitor */ }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Errore: {ex.Message}");
            exitCode = 1;
        }
        finally
        {
            var sum = stats.Summary();
            if (sum.Samples > 0)
                Console.WriteLine($"RIASSUNTO ({sum.Samples} frame disegnati): pipeline p50 {sum.PipelineP50Ms:F1} ms, p95 {sum.PipelineP95Ms:F1} ms, max {sum.PipelineMaxMs:F1} ms | RTT mediano {sum.RttMedianMs:F2} ms | codifica media (cattura->pacchetto) {sum.EncodeMsAvg:F1} ms | frame scartati {sum.DroppedFrames}");
            csv?.Dispose();
            pipeline?.Dispose();
            server.DisposeAsync().AsTask().Wait(2000);
            try { serverTask.Wait(2000); } catch { /* arresto */ }
            if (!keepDisplay)
            {
                try { display.RemoveAsync().GetAwaiter().GetResult(); Console.WriteLine("Monitor virtuale staccato dal desktop."); }
                catch (Exception ex) { Console.Error.WriteLine($"Impossibile staccare il monitor: {ex.Message}"); }
            }
        }
        return exitCode;
    }
}
