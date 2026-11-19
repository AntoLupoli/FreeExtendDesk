using FreeExtend.Protocol;
using Xunit;

namespace FreeExtend.Protocol.Tests;

public class MessageCodecTests
{
    private static string GoldenPath(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "protocol", "golden")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "protocol", "golden", name);
    }

    private static byte[] Hex(string s) =>
        s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(h => Convert.ToByte(h, 16)).ToArray();

    private static byte[] Serialize(Message m)
    {
        using var ms = new MemoryStream();
        MessageCodec.Write(ms, m);
        return ms.ToArray();
    }

    /// <summary>Stream che restituisce al massimo un byte per Read: simula le letture parziali di TCP.</summary>
    private sealed class OneByteStream(byte[] data) : Stream
    {
        private int _pos;
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos >= data.Length || count == 0) return 0;
            buffer[offset] = data[_pos++];
            return 1;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public void Hello_encodes_to_golden_bytes()
    {
        var msg = new Message(MessageType.Hello, 0, 0, HelloPayload.Encode(2304, 1440, 60, 1));

        var bytes = Serialize(msg);

        Assert.Equal(Hex("01 00 00 00 00 00 00 07 00 00 00 00 00 00 00 00 09 00 05 A0 00 3C 01"), bytes);
        Assert.Equal(File.ReadAllBytes(GoldenPath("hello.bin")), bytes);
    }

    [Fact]
    public void Video_key_roundtrips_with_timestamp_and_matches_golden()
    {
        var payload = new byte[] { 0x00, 0x00, 0x00, 0x01, 0x65, 0xAA };
        var msg = new Message(MessageType.Video, 1, 1_234_567, payload);

        var bytes = Serialize(msg);
        Assert.Equal(File.ReadAllBytes(GoldenPath("video_key.bin")), bytes);

        var back = MessageCodec.Read(new MemoryStream(bytes));
        Assert.NotNull(back);
        Assert.Equal(MessageType.Video, back!.Value.Type);
        Assert.Equal(1, back.Value.Flags);
        Assert.Equal(1_234_567, back.Value.TimestampUs);
        Assert.Equal(payload, back.Value.Payload.ToArray());
    }

    [Fact]
    public void Read_returns_null_on_clean_eof()
    {
        Assert.Null(MessageCodec.Read(new MemoryStream(Array.Empty<byte>())));
    }

    [Fact]
    public void Read_returns_two_messages_then_null()
    {
        var both = Serialize(new Message(MessageType.Ping, 0, 7, ReadOnlyMemory<byte>.Empty))
            .Concat(Serialize(new Message(MessageType.Pong, 0, 7, ReadOnlyMemory<byte>.Empty))).ToArray();
        var ms = new MemoryStream(both);

        Assert.Equal(MessageType.Ping, MessageCodec.Read(ms)!.Value.Type);
        Assert.Equal(MessageType.Pong, MessageCodec.Read(ms)!.Value.Type);
        Assert.Null(MessageCodec.Read(ms));
    }

    [Fact]
    public void Read_throws_on_truncated_header()
    {
        var bytes = Serialize(new Message(MessageType.Ping, 0, 1, ReadOnlyMemory<byte>.Empty)).Take(10).ToArray();
        Assert.Throws<ProtocolException>(() => MessageCodec.Read(new MemoryStream(bytes)));
    }

    [Fact]
    public void Read_throws_on_truncated_payload()
    {
        var bytes = Serialize(new Message(MessageType.Video, 0, 1, new byte[] { 1, 2, 3, 4 })).Take(16 + 2).ToArray();
        Assert.Throws<ProtocolException>(() => MessageCodec.Read(new MemoryStream(bytes)));
    }

    [Fact]
    public void Read_throws_on_unknown_type()
    {
        var bytes = Hex("63 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00");
        Assert.Throws<ProtocolException>(() => MessageCodec.Read(new MemoryStream(bytes)));
    }

    [Fact]
    public void Read_throws_on_payload_over_max_without_allocating()
    {
        // lunghezza dichiarata = MaxPayload + 1 = 0x00800001; nel flusso non c'e altro.
        var bytes = Hex("03 00 00 00 00 80 00 01 00 00 00 00 00 00 00 00");
        var ex = Assert.Throws<ProtocolException>(() => MessageCodec.Read(new MemoryStream(bytes)));
        Assert.Contains("payload", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_handles_one_byte_at_a_time()
    {
        var payload = new byte[] { 9, 8, 7, 6, 5 };
        var bytes = Serialize(new Message(MessageType.Video, 1, 42, payload));

        var back = MessageCodec.Read(new OneByteStream(bytes));

        Assert.NotNull(back);
        Assert.Equal(42, back!.Value.TimestampUs);
        Assert.Equal(payload, back.Value.Payload.ToArray());
    }

    [Fact]
    public void Write_throws_on_payload_over_max()
    {
        var tooBig = new Message(MessageType.Video, 0, 0, new byte[MessageCodec.MaxPayload + 1]);
        Assert.Throws<ProtocolException>(() => MessageCodec.Write(new MemoryStream(), tooBig));
    }

    [Fact]
    public void Magic_is_FXD1()
    {
        Assert.Equal(new byte[] { 0x46, 0x58, 0x44, 0x31 }, MessageCodec.Magic);
    }

    [Fact]
    public void Hello_payload_roundtrips()
    {
        var (w, h, hz, codecs) = HelloPayload.Decode(HelloPayload.Encode(2304, 1440, 90, 1));
        Assert.Equal((2304, 1440, 90, 1), ((int)w, (int)h, (int)hz, (int)codecs));
    }

    [Fact]
    public void Hello_payload_throws_when_too_short()
    {
        Assert.Throws<ProtocolException>(() => HelloPayload.Decode(new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public void Config_payload_roundtrips_with_param_sets()
    {
        var paramSets = new byte[] { 0, 0, 0, 1, 0x67, 0xAA, 0, 0, 0, 1, 0x68, 0xBB };

        var encoded = ConfigPayload.Encode(2304, 1440, 60, paramSets);
        Assert.Equal(Hex("09 00 05 A0 00 3C"), encoded.Take(6).ToArray());

        var (w, h, fps, back) = ConfigPayload.Decode(encoded);
        Assert.Equal((2304, 1440, 60), ((int)w, (int)h, (int)fps));
        Assert.Equal(paramSets, back);
    }

    [Fact]
    public void Config_payload_throws_when_too_short()
    {
        Assert.Throws<ProtocolException>(() => ConfigPayload.Decode(new byte[] { 1, 2, 3, 4, 5 }));
    }
}
