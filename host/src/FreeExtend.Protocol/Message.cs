namespace FreeExtend.Protocol;

/// <summary>Tipi di messaggio del flusso PC <-> tablet (vedi protocol/golden/README.md).</summary>
public enum MessageType : byte
{
    Hello = 1,
    Config = 2,
    Video = 3,
    Ping = 4,
    Pong = 5,
    Rendered = 6,
    RequestKeyframe = 7,
}

/// <param name="Flags">Bit 0 = keyframe (solo per <see cref="MessageType.Video"/>).</param>
/// <param name="TimestampUs">Per Video: istante di cattura sull'orologio dell'host. Ping/Pong/Rendered lo ripetono.</param>
public readonly record struct Message(MessageType Type, byte Flags, long TimestampUs, ReadOnlyMemory<byte> Payload);
