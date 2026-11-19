using System.Buffers.Binary;

namespace FreeExtend.Protocol;

/// <summary>Payload di <see cref="MessageType.Hello"/> (tablet -> host): <c>u16 width, u16 height, u16 refreshHz, u8 codecMask</c> (bit 0 = H.264).</summary>
public static class HelloPayload
{
    public const int Size = 7;

    public static byte[] Encode(ushort width, ushort height, ushort refreshHz, byte codecMask)
    {
        var b = new byte[Size];
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(0, 2), width);
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(2, 2), height);
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(4, 2), refreshHz);
        b[6] = codecMask;
        return b;
    }

    public static (ushort W, ushort H, ushort Hz, byte Codecs) Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < Size) throw new ProtocolException($"Hello too short ({payload.Length} bytes, {Size} needed)");
        return (BinaryPrimitives.ReadUInt16BigEndian(payload[..2]),
                BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(2, 2)),
                BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(4, 2)),
                payload[6]);
    }
}

/// <summary>Payload di <see cref="MessageType.Config"/> (host -> tablet): <c>u16 width, u16 height, u16 fps</c> + SPS/PPS in Annex-B.</summary>
public static class ConfigPayload
{
    public const int FixedSize = 6;

    public static byte[] Encode(ushort width, ushort height, ushort fps, ReadOnlySpan<byte> annexBParamSets)
    {
        var b = new byte[FixedSize + annexBParamSets.Length];
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(0, 2), width);
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(2, 2), height);
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(4, 2), fps);
        annexBParamSets.CopyTo(b.AsSpan(FixedSize));
        return b;
    }

    public static (ushort W, ushort H, ushort Fps, byte[] ParamSets) Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < FixedSize) throw new ProtocolException($"Config too short ({payload.Length} bytes, at least {FixedSize} needed)");
        return (BinaryPrimitives.ReadUInt16BigEndian(payload[..2]),
                BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(2, 2)),
                BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(4, 2)),
                payload[FixedSize..].ToArray());
    }
}
