using System.Net;
using System.Net.Sockets;
using FreeExtend.Host.Capture;
using FreeExtend.Host.Encode;
using FreeExtend.Protocol;

namespace FreeExtend.Host.Net;

/// <summary>Dato l'Hello del tablet, restituisce il payload di Config (vedi <see cref="ConfigPayload.Encode"/>).</summary>
public delegate byte[] ConfigProvider((ushort W, ushort H, ushort Hz) hello);

/// <summary>
/// Server TCP del flusso video: un tablet alla volta, in ascolto su 127.0.0.1 (il cavo arriva qui con <c>adb reverse</c>).
/// Handshake: magic "FXD1" da entrambi i lati + Hello dal tablet, poi Config dall'host. Nessuna coda lunga: se il tablet e
/// in ritardo i frame si scartano e si chiede un keyframe (<see cref="SendBacklogPolicy"/>).
/// </summary>
public sealed class StreamServer : IAsyncDisposable
{
    private const int KeyframeRequestMinIntervalMs = 300;

    private readonly int _port;
    private readonly ConfigProvider _config;
    private TcpListener? _listener;
    private volatile Session? _session;
    private int _keyframeRequested;
    private volatile bool _disposed;

    private readonly int _keepaliveMs, _deadAfterMs, _handshakeTimeoutMs;
    private long _lastDropKeyframeRequestTick = long.MinValue / 2;

    /// <param name="keepaliveMs">Se per questo tempo non parte nulla verso il tablet (desktop fermo) il server manda un Ping: il tablet
    /// considera morta una connessione senza traffico.</param>
    /// <param name="deadAfterMs">Se dal tablet non arriva nulla (nemmeno i Pong) per questo tempo la sessione e morta: adb potrebbe non chiudere
    /// il socket quando il cavo viene staccato davvero.</param>
    /// <param name="handshakeTimeoutMs">Tempo massimo per completare l'handshake: un client muto non deve bloccare l'accettazione degli altri.</param>
    public StreamServer(int port, ConfigProvider config, int keepaliveMs = 1000, int deadAfterMs = 3000, int handshakeTimeoutMs = 5000)
    {
        _port = port;
        _config = config;
        _keepaliveMs = keepaliveMs;
        _deadAfterMs = deadAfterMs;
        _handshakeTimeoutMs = handshakeTimeoutMs;
    }

    /// <summary>C'e un tablet connesso (handshake completato e sessione in corso).</summary>
    public bool IsConnected => _session != null;

    /// <summary>Un tablet si e connesso e ha completato l'handshake.</summary>
    public event Action<(ushort W, ushort H, ushort Hz)>? ClientConnected;
    /// <summary>Il tablet ha disegnato il frame con questo timestamp di cattura.</summary>
    public event Action<long>? RenderedAck;
    /// <summary>Risposta a un Ping: (timestamp inviato, istante di ricezione sull'orologio dell'host).</summary>
    public event Action<long, long>? PongReceived;
    /// <summary>Un client e stato scartato per un errore (handshake, configurazione, flusso): il server resta in ascolto.</summary>
    public event Action<Exception>? ClientError;

    /// <summary>Pacchetti video in attesa di invio (esclusi quello in scrittura e i messaggi di controllo).</summary>
    public int QueuedVideoPackets => _session?.QueuedVideo ?? 0;

    /// <summary>Apre l'ascolto e restituisce la porta effettiva (utile con porta 0 nei test). Poi chiamare <see cref="RunAsync"/>.</summary>
    public int Start()
    {
        _listener = new TcpListener(IPAddress.Loopback, _port);
        _listener.Start();
        return ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        if (_listener == null) Start();
        try
        {
            while (!ct.IsCancellationRequested && !_disposed)
            {
                TcpClient client = await _listener!.AcceptTcpClientAsync(ct);
                try { await HandleClientAsync(client, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Qualunque errore legato a un client (handshake, flusso corrotto, configurazione fallita nel callback):
                    // si chiude quel client, si segnala e si resta in ascolto. Il ciclo di accettazione non deve morire.
                    if (!_disposed) SafeInvokeError(ex);
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
        {
            // arresto richiesto
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            client.NoDelay = true;
            client.SendBufferSize = 256 * 1024;                                              // il buffer del kernel non deve nascondere un backlog di molti frame
            using var stream = client.GetStream();
            stream.ReadTimeout = _handshakeTimeoutMs;                                        // vale per le letture sincrone (Hello)

            // La lettura asincrona del magic NON rispetta NetworkStream.ReadTimeout: serve un token con scadenza, altrimenti un client
            // che si connette e tace bloccherebbe per sempre l'accettazione di tutti gli altri.
            using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            handshakeCts.CancelAfter(_handshakeTimeoutMs);
            var magic = new byte[MessageCodec.Magic.Length];
            int got = 0;
            try
            {
                while (got < magic.Length)
                {
                    int n = await stream.ReadAsync(magic.AsMemory(got), handshakeCts.Token);
                    if (n == 0) return;
                    got += n;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException($"handshake not completed within {_handshakeTimeoutMs} ms");
            }
            if (!magic.AsSpan().SequenceEqual(MessageCodec.Magic)) return;                  // magic sbagliato: chiudi
            await stream.WriteAsync(MessageCodec.Magic);

            var hello = MessageCodec.Read(stream);
            if (hello is not { Type: MessageType.Hello } h) return;
            var (w, hgt, hz, _) = HelloPayload.Decode(h.Payload.Span);
            stream.ReadTimeout = _deadAfterMs;                                               // sessione: niente dal tablet per questo tempo = morto

            var session = new Session(stream, _keepaliveMs);
            session.EnqueueControl(new Message(MessageType.Config, 0, 0, _config((w, hgt, hz))));
            _session = session;
            Interlocked.Exchange(ref _keyframeRequested, 1);                                // un client nuovo parte da un keyframe
            ClientConnected?.Invoke((w, hgt, hz));

            var sender = Task.Factory.StartNew(session.SendLoop, TaskCreationOptions.LongRunning);
            var reader = Task.Factory.StartNew(() => ReadLoop(session, stream), TaskCreationOptions.LongRunning);
            await Task.WhenAny(sender, reader);

            session.Close();
            if (ReferenceEquals(_session, session)) _session = null;
            client.Close();
            try { await Task.WhenAll(sender, reader).WaitAsync(TimeSpan.FromSeconds(2)); } catch { /* chiusura */ }
        }
    }

    private void ReadLoop(Session session, NetworkStream stream)
    {
        try
        {
            while (true)
            {
                var m = MessageCodec.Read(stream);
                if (m == null) return;                                                      // il tablet ha chiuso
                switch (m.Value.Type)
                {
                    case MessageType.Pong: SafeInvoke(() => PongReceived?.Invoke(m.Value.TimestampUs, CaptureClock.NowUs())); break;
                    case MessageType.Rendered: SafeInvoke(() => RenderedAck?.Invoke(m.Value.TimestampUs)); break;
                    case MessageType.RequestKeyframe: Interlocked.Exchange(ref _keyframeRequested, 1); break;
                    // altri tipi dal tablet (Hello ripetuto, ecc.): ignorati
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ProtocolException or ObjectDisposedException or SocketException)
        {
            // flusso corrotto, troncato o chiuso: la sessione finisce
        }
    }

    private void SafeInvokeError(Exception ex)
    {
        try { ClientError?.Invoke(ex); } catch { /* un handler difettoso non deve far morire il server */ }
    }

    private static void SafeInvoke(Action a)
    {
        try { a(); } catch { /* un handler difettoso non deve far cadere la connessione */ }
    }

    /// <summary>Offre un pacchetto al tablet. Non blocca mai: se non c'e un client o e in ritardo, il frame si scarta.</summary>
    public void Publish(EncodedPacket packet)
    {
        var s = _session;
        if (s == null) return;
        if (!s.TryEnqueueVideo(packet))
        {
            // Un frame scartato chiede un keyframe, ma al massimo uno ogni KeyframeRequestMinIntervalMs: con un collegamento lento
            // "keyframe, scarto, keyframe..." peggiorerebbe la congestione (i keyframe sono molte volte piu grandi).
            long now = Environment.TickCount64;
            if (now - Interlocked.Read(ref _lastDropKeyframeRequestTick) >= KeyframeRequestMinIntervalMs)
            {
                Interlocked.Exchange(ref _lastDropKeyframeRequestTick, now);
                Interlocked.Exchange(ref _keyframeRequested, 1);
            }
        }
    }

    /// <summary>Messaggio di controllo (Ping, keepalive) in coda con il video, nello stesso ordine.</summary>
    public void Send(Message message) => _session?.EnqueueControl(message);

    /// <summary>Vero (una sola volta) se l'encoder deve produrre un keyframe: client nuovo, frame scartato o richiesta del tablet.</summary>
    public bool TakeKeyframeRequest() => Interlocked.Exchange(ref _keyframeRequested, 0) == 1;

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        try { _listener?.Stop(); } catch { }
        _session?.Close();
        return ValueTask.CompletedTask;
    }

    /// <summary>Connessione attiva: coda in uscita (video + controllo) svuotata da un thread solo.</summary>
    private sealed class Session
    {
        private readonly NetworkStream _stream;
        private readonly object _gate = new();
        private readonly LinkedList<(Message Message, bool IsVideo)> _queue = new();
        private readonly SendBacklogPolicy _policy = new(startWaitingForKeyframe: true);   // un client nuovo parte da un keyframe
        private readonly int _keepaliveMs;
        private int _queuedVideo;
        private bool _closed;

        public Session(NetworkStream stream, int keepaliveMs)
        {
            _stream = stream;
            _keepaliveMs = keepaliveMs;
        }

        public int QueuedVideo { get { lock (_gate) return _queuedVideo; } }

        public bool TryEnqueueVideo(EncodedPacket p)
        {
            lock (_gate)
            {
                if (_closed) return true;
                if (_policy.OnPacket(_queuedVideo, p.IsKeyframe) == BacklogDecision.DropAndRequestKeyframe) return false;
                if (p.IsKeyframe) DropQueuedVideo();                                        // un keyframe rende inutili i frame piu vecchi in coda
                _queue.AddLast((new Message(MessageType.Video, (byte)(p.IsKeyframe ? 1 : 0), p.CaptureUs, p.Data), true));
                _queuedVideo++;
                Monitor.Pulse(_gate);
                return true;
            }
        }

        public void EnqueueControl(Message m)
        {
            lock (_gate)
            {
                if (_closed) return;
                _queue.AddLast((m, false));
                Monitor.Pulse(_gate);
            }
        }

        private void DropQueuedVideo()
        {
            for (var node = _queue.First; node != null;)
            {
                var next = node.Next;
                if (node.Value.IsVideo) { _queue.Remove(node); _queuedVideo--; }
                node = next;
            }
        }

        public void SendLoop()
        {
            try
            {
                while (true)
                {
                    Message m;
                    lock (_gate)
                    {
                        while (_queue.Count == 0 && !_closed)
                        {
                            // Nessun traffico per _keepaliveMs (desktop fermo): un Ping tiene viva la connessione.
                            if (!Monitor.Wait(_gate, _keepaliveMs))
                                _queue.AddLast((new Message(MessageType.Ping, 0, CaptureClock.NowUs(), ReadOnlyMemory<byte>.Empty), false));
                        }
                        if (_closed) return;
                        var (msg, isVideo) = _queue.First!.Value;
                        _queue.RemoveFirst();
                        if (isVideo) _queuedVideo--;
                        m = msg;
                    }
                    MessageCodec.Write(_stream, m);
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
            {
                // cavo scollegato o tablet chiuso: la sessione finisce
            }
        }

        public void Close()
        {
            lock (_gate)
            {
                _closed = true;
                _queue.Clear();
                _queuedVideo = 0;
                Monitor.PulseAll(_gate);
            }
            try { _stream.Close(); } catch { }
        }
    }
}
