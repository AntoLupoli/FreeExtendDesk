using System.Net.Sockets;
using FreeExtend.Host.Capture;
using FreeExtend.Host.Encode;
using FreeExtend.Host.Net;
using FreeExtend.Protocol;
using Xunit;

namespace FreeExtend.Host.Tests;

public class StreamServerTests : IAsyncDisposable
{
    private static readonly byte[] ParamSets = { 0, 0, 0, 1, 0x67, 0xAA, 0, 0, 0, 1, 0x68, 0xBB };
    private readonly CancellationTokenSource _cts = new();
    private readonly StreamServer _server;
    private readonly Task _run;
    private readonly int _port;

    public StreamServerTests() : this(keepaliveMs: 60_000) { }

    private StreamServerTests(int keepaliveMs)
    {
        _server = new StreamServer(0, hello => ConfigPayload.Encode(hello.W, hello.H, 60, ParamSets), keepaliveMs);
        _port = _server.Start();
        _run = _server.RunAsync(_cts.Token);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        await _server.DisposeAsync();
        try { await _run.WaitAsync(TimeSpan.FromSeconds(3)); } catch (Exception) { /* cancellazione attesa */ }
    }

    private async Task<(TcpClient Client, NetworkStream Stream)> ConnectAsync()
    {
        var c = new TcpClient { NoDelay = true };
        await c.ConnectAsync("127.0.0.1", _port);
        return (c, c.GetStream());
    }

    private static async Task HandshakeAsync(NetworkStream s, ushort w = 2304, ushort h = 1440, ushort hz = 60)
    {
        await s.WriteAsync(MessageCodec.Magic);
        MessageCodec.Write(s, new Message(MessageType.Hello, 0, 0, HelloPayload.Encode(w, h, hz, 1)));
        await s.FlushAsync();
    }

    private static async Task<byte[]> ReadExactAsync(NetworkStream s, int n)
    {
        var buf = new byte[n];
        int got = 0;
        while (got < n)
        {
            int r = await s.ReadAsync(buf.AsMemory(got, n - got)).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            if (r == 0) throw new EndOfStreamException();
            got += r;
        }
        return buf;
    }

    private static Message ReadMessage(NetworkStream s)
    {
        s.ReadTimeout = 5000;
        return MessageCodec.Read(s) ?? throw new EndOfStreamException();
    }

    [Fact]
    public async Task Handshake_then_config_then_video()
    {
        var (client, s) = await ConnectAsync();
        using var _ = client;
        var connected = new TaskCompletionSource<(ushort W, ushort H, ushort Hz)>();
        _server.ClientConnected += c => connected.TrySetResult(c);

        await HandshakeAsync(s);

        Assert.Equal(MessageCodec.Magic, await ReadExactAsync(s, 4));
        var config = ReadMessage(s);
        Assert.Equal(MessageType.Config, config.Type);
        var (w, h, fps, ps) = ConfigPayload.Decode(config.Payload.Span);
        Assert.Equal((2304, 1440, 60), ((int)w, (int)h, (int)fps));
        Assert.Equal(ParamSets, ps);
        Assert.Equal((2304, 1440, 60), await connected.Task.WaitAsync(TimeSpan.FromSeconds(5)) is var (cw, ch, chz) ? ((int)cw, (int)ch, (int)chz) : default);

        // un client nuovo ha bisogno di un keyframe
        Assert.True(_server.TakeKeyframeRequest());
        Assert.False(_server.TakeKeyframeRequest());

        var data = new byte[] { 0, 0, 0, 1, 0x65, 1, 2, 3 };
        _server.Publish(new EncodedPacket(data, true, 777));

        var video = ReadMessage(s);
        Assert.Equal(MessageType.Video, video.Type);
        Assert.Equal(1, video.Flags);
        Assert.Equal(777, video.TimestampUs);
        Assert.Equal(data, video.Payload.ToArray());
    }

    [Fact]
    public async Task Bad_magic_closes_connection_and_server_keeps_listening()
    {
        var (bad, bs) = await ConnectAsync();
        using (bad)
        {
            await bs.WriteAsync(new byte[] { (byte)'X', (byte)'X', (byte)'X', (byte)'X' });
            await bs.FlushAsync();
            var one = new byte[1];
            Assert.Equal(0, await bs.ReadAsync(one).AsTask().WaitAsync(TimeSpan.FromSeconds(5)));   // il server chiude
        }

        var (good, gs) = await ConnectAsync();
        using (good)
        {
            await HandshakeAsync(gs);
            Assert.Equal(MessageCodec.Magic, await ReadExactAsync(gs, 4));
            Assert.Equal(MessageType.Config, ReadMessage(gs).Type);
        }
    }

    [Fact]
    public async Task Oversized_payload_closes_connection_and_server_keeps_listening()
    {
        var (bad, bs) = await ConnectAsync();
        using (bad)
        {
            await HandshakeAsync(bs);
            await ReadExactAsync(bs, 4);
            ReadMessage(bs);
            // intestazione Video che dichiara 0x7FFFFFFF byte di payload
            await bs.WriteAsync(new byte[] { 3, 0, 0, 0, 0x7F, 0xFF, 0xFF, 0xFF, 0, 0, 0, 0, 0, 0, 0, 0 });
            await bs.FlushAsync();
            var one = new byte[1];
            Assert.Equal(0, await bs.ReadAsync(one).AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        }

        var (good, gs) = await ConnectAsync();
        using (good)
        {
            await HandshakeAsync(gs);
            Assert.Equal(MessageCodec.Magic, await ReadExactAsync(gs, 4));
            Assert.Equal(MessageType.Config, ReadMessage(gs).Type);
        }
    }

    [Fact]
    public async Task A_failing_config_provider_closes_that_client_and_the_server_keeps_listening()
    {
        // La scelta del modo puo fallire (nessun modo adatto al tablet): il server non deve morire per questo.
        bool fail = true;
        await using var server = new StreamServer(0, hello =>
            fail ? throw new InvalidOperationException("nessun modo adatto") : ConfigPayload.Encode(hello.W, hello.H, 60, ParamSets));
        int port = server.Start();
        using var cts = new CancellationTokenSource();
        var errors = new List<Exception>();
        server.ClientError += e => errors.Add(e);
        var run = server.RunAsync(cts.Token);

        using (var bad = new TcpClient { NoDelay = true })
        {
            await bad.ConnectAsync("127.0.0.1", port);
            var bs = bad.GetStream();
            await HandshakeAsync(bs);
            var one = new byte[1];
            // magic di risposta (4 byte) poi chiusura senza Config: letture fino a EOF
            int total = 0, n;
            while ((n = await bs.ReadAsync(one).AsTask().WaitAsync(TimeSpan.FromSeconds(5))) > 0) total += n;
            Assert.Equal(4, total);
        }
        Assert.Single(errors);

        fail = false;
        using var good = new TcpClient { NoDelay = true };
        await good.ConnectAsync("127.0.0.1", port);
        var gs = good.GetStream();
        await HandshakeAsync(gs);
        Assert.Equal(MessageCodec.Magic, await ReadExactAsync(gs, 4));
        Assert.Equal(MessageType.Config, ReadMessage(gs).Type);

        cts.Cancel();
    }

    [Fact]
    public async Task Pong_and_rendered_and_keyframe_request_from_client_raise_events()
    {
        var (client, s) = await ConnectAsync();
        using var _ = client;
        await HandshakeAsync(s);
        await ReadExactAsync(s, 4);
        ReadMessage(s);
        Assert.True(_server.TakeKeyframeRequest());

        var pong = new TaskCompletionSource<(long Sent, long Recv)>();
        var rendered = new TaskCompletionSource<long>();
        _server.PongReceived += (sent, recv) => pong.TrySetResult((sent, recv));
        _server.RenderedAck += ts => rendered.TrySetResult(ts);

        long before = CaptureClock.NowUs();
        MessageCodec.Write(s, new Message(MessageType.Pong, 0, 123, ReadOnlyMemory<byte>.Empty));
        MessageCodec.Write(s, new Message(MessageType.Rendered, 0, 456, ReadOnlyMemory<byte>.Empty));
        MessageCodec.Write(s, new Message(MessageType.RequestKeyframe, 0, 0, ReadOnlyMemory<byte>.Empty));
        await s.FlushAsync();

        var p = await pong.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(123, p.Sent);
        Assert.True(p.Recv >= before);
        Assert.Equal(456, await rendered.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        for (int i = 0; i < 100 && !_server.TakeKeyframeRequest(); i++) await Task.Delay(20);
        // TakeKeyframeRequest consuma il flag: se e arrivato, il ciclo sopra e uscito con true; altrimenti il test fallisce qui
        Assert.False(_server.TakeKeyframeRequest());
    }

    [Fact]
    public async Task Slow_client_cannot_grow_the_queue_beyond_the_backlog_limit()
    {
        var (client, s) = await ConnectAsync();   // si connette ma non legge mai dopo l'handshake
        using var _ = client;
        await HandshakeAsync(s);
        await ReadExactAsync(s, 4);
        ReadMessage(s);
        _server.TakeKeyframeRequest();

        var big = new byte[1024 * 1024];
        _server.Publish(new EncodedPacket(big, true, 1));
        for (int i = 2; i <= 80; i++)
            _server.Publish(new EncodedPacket(big, false, i));          // 80 MiB: non possono stare nei buffer TCP

        Assert.True(_server.QueuedVideoPackets <= 2, $"coda = {_server.QueuedVideoPackets}");
        Assert.True(_server.TakeKeyframeRequest());                      // il server ha chiesto un keyframe all'encoder
    }

    [Fact]
    public async Task New_client_gets_no_video_until_the_first_keyframe()
    {
        var (client, s) = await ConnectAsync();
        using var _ = client;
        await HandshakeAsync(s);
        await ReadExactAsync(s, 4);
        ReadMessage(s);                                                       // Config

        // frame non-keyframe prodotti prima del primo keyframe: il client non ha un riferimento per decodificarli
        _server.Publish(new EncodedPacket(new byte[] { 1, 1, 1 }, false, 100));
        _server.Publish(new EncodedPacket(new byte[] { 2, 2, 2 }, false, 101));
        _server.Publish(new EncodedPacket(new byte[] { 3, 3, 3 }, true, 102));
        _server.Publish(new EncodedPacket(new byte[] { 4, 4, 4 }, false, 103));

        var first = ReadMessage(s);
        Assert.Equal(MessageType.Video, first.Type);
        Assert.Equal(102, first.TimestampUs);                                 // il primo video ricevuto e il keyframe
        Assert.Equal(1, first.Flags);
        Assert.Equal(103, ReadMessage(s).TimestampUs);                        // poi i delta normalmente
    }

    [Fact]
    public async Task Idle_server_sends_a_ping_within_the_keepalive_interval()
    {
        await using var server = new StreamServer(0, hello => ConfigPayload.Encode(hello.W, hello.H, 60, ParamSets), keepaliveMs: 200);
        int port = server.Start();
        using var cts = new CancellationTokenSource();
        var run = server.RunAsync(cts.Token);

        using var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync("127.0.0.1", port);
        var s = client.GetStream();
        await HandshakeAsync(s);
        await ReadExactAsync(s, 4);
        Assert.Equal(MessageType.Config, ReadMessage(s).Type);

        // desktop fermo: nessun video, ma il server non deve tacere (il tablet considera morta una connessione muta)
        var m = ReadMessage(s);
        Assert.Equal(MessageType.Ping, m.Type);
        cts.Cancel();
    }

    [Fact]
    public async Task Reconnecting_client_gets_config_again_and_a_new_keyframe_request()
    {
        var (first, s1) = await ConnectAsync();
        await HandshakeAsync(s1);
        await ReadExactAsync(s1, 4);
        Assert.Equal(MessageType.Config, ReadMessage(s1).Type);
        Assert.True(_server.TakeKeyframeRequest());
        Assert.True(_server.IsConnected);

        first.Dispose();                                                      // cavo staccato / app chiusa
        for (int i = 0; i < 100 && _server.IsConnected; i++) await Task.Delay(20);
        Assert.False(_server.IsConnected);

        var (second, s2) = await ConnectAsync();
        using var _ = second;
        await HandshakeAsync(s2);
        await ReadExactAsync(s2, 4);
        Assert.Equal(MessageType.Config, ReadMessage(s2).Type);               // Config di nuovo, per prima cosa
        Assert.True(_server.TakeKeyframeRequest());                           // e il nuovo client riparte da un keyframe

        _server.Publish(new EncodedPacket(new byte[] { 9 }, true, 555));
        var video = ReadMessage(s2);
        Assert.Equal(1, video.Flags);
        Assert.Equal(555, video.TimestampUs);
    }

    // --- Revisione finale: I1 (accept bloccabile / host senza controllo di vita), I2 (richieste di keyframe a raffica) ---

    [Fact]
    public async Task A_silent_connection_does_not_block_the_next_client()
    {
        // Un processo locale (o un forward adb a meta) si connette e non dice nulla: non deve bloccare per sempre l'accettazione.
        await using var server = new StreamServer(0, hello => ConfigPayload.Encode(hello.W, hello.H, 60, ParamSets), handshakeTimeoutMs: 300);
        int port = server.Start();
        using var cts = new CancellationTokenSource();
        var run = server.RunAsync(cts.Token);

        using var silent = new TcpClient();
        await silent.ConnectAsync("127.0.0.1", port);                      // nessun byte inviato

        using var good = new TcpClient { NoDelay = true };
        await good.ConnectAsync("127.0.0.1", port);
        var gs = good.GetStream();
        await HandshakeAsync(gs);
        Assert.Equal(MessageCodec.Magic, await ReadExactAsync(gs, 4));     // servito dopo il timeout del muto (ReadExactAsync ha 5 s)
        Assert.Equal(MessageType.Config, ReadMessage(gs).Type);
        cts.Cancel();
    }

    [Fact]
    public async Task A_client_that_stops_answering_is_dropped_and_the_next_one_is_served()
    {
        // Adb potrebbe non chiudere il socket quando il cavo viene staccato davvero: l'host deve accorgersene da solo.
        await using var server = new StreamServer(0, hello => ConfigPayload.Encode(hello.W, hello.H, 60, ParamSets), keepaliveMs: 100, deadAfterMs: 400);
        int port = server.Start();
        using var cts = new CancellationTokenSource();
        var run = server.RunAsync(cts.Token);

        using var zombie = new TcpClient { NoDelay = true };
        await zombie.ConnectAsync("127.0.0.1", port);
        var zs = zombie.GetStream();
        await HandshakeAsync(zs);                                          // poi non risponde piu a nessun Ping
        for (int i = 0; i < 100 && !server.IsConnected; i++) await Task.Delay(20);
        Assert.True(server.IsConnected);

        for (int i = 0; i < 150 && server.IsConnected; i++) await Task.Delay(20);
        Assert.False(server.IsConnected);                                  // dichiarato morto da solo, entro circa 400 ms

        using var good = new TcpClient { NoDelay = true };
        await good.ConnectAsync("127.0.0.1", port);
        var gs = good.GetStream();
        await HandshakeAsync(gs);
        Assert.Equal(MessageCodec.Magic, await ReadExactAsync(gs, 4));
        Assert.Equal(MessageType.Config, ReadMessage(gs).Type);
        cts.Cancel();
    }

    [Fact]
    public async Task Keyframe_requests_for_dropped_frames_are_rate_limited()
    {
        // Con un collegamento lento ogni frame scartato chiederebbe un keyframe: keyframe, scarto, keyframe... peggiora la congestione.
        var (client, s) = await ConnectAsync();
        using var _ = client;
        await HandshakeAsync(s);
        await ReadExactAsync(s, 4);
        ReadMessage(s);
        _server.TakeKeyframeRequest();                                     // consuma la richiesta del collegamento

        var big = new byte[1024 * 1024];
        _server.Publish(new EncodedPacket(big, true, 1));
        for (int i = 2; i <= 80; i++) _server.Publish(new EncodedPacket(big, false, i));   // riempie la coda: scarti e richieste

        Assert.True(_server.TakeKeyframeRequest());                        // la prima richiesta passa
        for (int i = 81; i <= 120; i++) _server.Publish(new EncodedPacket(big, false, i));  // altri 40 scarti subito dopo
        Assert.False(_server.TakeKeyframeRequest());                       // ... ma non generano 40 richieste: sono limitate nel tempo

        await Task.Delay(400);
        _server.Publish(new EncodedPacket(big, false, 121));
        Assert.True(_server.TakeKeyframeRequest());                        // passato l'intervallo, una nuova richiesta e lecita
    }
}
