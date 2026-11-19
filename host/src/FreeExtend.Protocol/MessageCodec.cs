using System.Buffers.Binary;

namespace FreeExtend.Protocol;

/// <summary>
/// Serializzazione dei messaggi, big-endian. Intestazione di 16 byte:
/// <c>u8 type | u8 flags | u16 0 | u32 payloadLength | u64 timestampUs</c>, poi il payload.
/// </summary>
public static class MessageCodec
{
    public const int HeaderSize = 16;
    public const int MaxPayload = 8 * 1024 * 1024;

    /// <summary>Primi 4 byte di ogni connessione, inviati da entrambi i lati: ASCII "FXD1".</summary>
    public static readonly byte[] Magic = { 0x46, 0x58, 0x44, 0x31 };

    public static void Write(Stream stream, Message message)
    {
        if (message.Payload.Length > MaxPayload)
            throw new ProtocolException($"payload di {message.Payload.Length} byte oltre il massimo di {MaxPayload}");

        Span<byte> header = stackalloc byte[HeaderSize];
        header.Clear();
        header[0] = (byte)message.Type;
        header[1] = message.Flags;
        BinaryPrimitives.WriteUInt32BigEndian(header.Slice(4, 4), (uint)message.Payload.Length);
        BinaryPrimitives.WriteInt64BigEndian(header.Slice(8, 8), message.TimestampUs);

        stream.Write(header);
        stream.Write(message.Payload.Span);
    }

    /// <summary>
    /// Legge un messaggio. Restituisce <c>null</c> se il flusso e finito esattamente sul confine di un messaggio.
    /// Lancia <see cref="ProtocolException"/> se il flusso e troncato, il tipo e sconosciuto o il payload supera <see cref="MaxPayload"/>
    /// (controllato prima di allocare).
    /// </summary>
    public static Message? Read(Stream stream)
    {
        Span<byte> header = stackalloc byte[HeaderSize];
        int got = ReadFully(stream, header);
        if (got == 0) return null;
        if (got < HeaderSize) throw new ProtocolException($"intestazione troncata ({got} di {HeaderSize} byte)");

        byte type = header[0];
        if (type < (byte)MessageType.Hello || type > (byte)MessageType.RequestKeyframe)
            throw new ProtocolException($"tipo di messaggio sconosciuto: {type}");

        uint length = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(4, 4));
        if (length > MaxPayload)
            throw new ProtocolException($"payload dichiarato di {length} byte oltre il massimo di {MaxPayload}");

        long timestampUs = BinaryPrimitives.ReadInt64BigEndian(header.Slice(8, 8));
        var payload = new byte[length];
        int payloadGot = ReadFully(stream, payload);
        if (payloadGot < length) throw new ProtocolException($"payload troncato ({payloadGot} di {length} byte)");

        return new Message((MessageType)type, header[1], timestampUs, payload);
    }

    /// <summary>Legge fino a riempire il buffer (TCP puo restituire letture parziali); restituisce i byte letti, meno solo a fine flusso.</summary>
    private static int ReadFully(Stream stream, Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = stream.Read(buffer.Slice(total));
            if (n == 0) break;
            total += n;
        }
        return total;
    }
}
