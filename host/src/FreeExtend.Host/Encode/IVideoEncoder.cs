using FreeExtend.Host.Capture;

namespace FreeExtend.Host.Encode;

/// <param name="QualityVsSpeed">0..100 (AVEncCommonQualityVsSpeed): piu basso = piu veloce. Null = predefinito dell'encoder.</param>
public sealed record EncoderConfig(int Width, int Height, int Fps, int BitrateKbps, int? QualityVsSpeed = null, int? SourceWidth = null, int? SourceHeight = null, int MftIndex = 0);

/// <param name="CaptureUs">Istante di cattura del frame (CaptureClock), riportato dal frame di origine.</param>
public readonly record struct EncodedPacket(ReadOnlyMemory<byte> Data, bool IsKeyframe, long CaptureUs);

public interface IVideoEncoder : IDisposable
{
    /// <summary>SPS+PPS in Annex-B visti nell'ultimo keyframe prodotto; vuoto finche non e uscito il primo.</summary>
    byte[] ParameterSets { get; }

    /// <summary>
    /// Offre un frame all'encoder. Restituisce <c>false</c> se il frame non e stato codificato: l'encoder e occupato
    /// o il frame supera il refresh richiesto (<see cref="EncoderConfig.Fps"/>). Scartare e sempre meglio che accodare.
    /// </summary>
    bool Encode(CapturedFrame frame, bool forceKeyframe);

    /// <summary>Vero se un frame offerto ora verrebbe codificato (rispetta il refresh richiesto e l'encoder ha chiesto ingresso). Non modifica i contatori.</summary>
    bool WantsFrame();

    /// <summary>Un pacchetto per frame codificato, nello stesso ordine di ingresso. Chiamato da un thread dell'encoder.</summary>
    event Action<EncodedPacket> PacketReady;
}
