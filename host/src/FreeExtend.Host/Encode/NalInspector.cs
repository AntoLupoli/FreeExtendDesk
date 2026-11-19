namespace FreeExtend.Host.Encode;

/// <param name="Type">Tipo NAL H.264 (7 = SPS, 8 = PPS, 5 = IDR, 1 = slice non-IDR).</param>
/// <param name="Offset">Posizione del byte di intestazione NAL (subito dopo lo start code).</param>
/// <param name="Length">Lunghezza del NAL, intestazione compresa, senza gli zeri dello start code successivo.</param>
public readonly record struct NalUnit(int Type, int Offset, int Length);

/// <summary>Lettura minima di un flusso H.264 in formato Annex-B (start code 00 00 01 o 00 00 00 01).</summary>
public static class NalInspector
{
    public const int NalSps = 7;
    public const int NalPps = 8;
    public const int NalIdr = 5;

    public static IReadOnlyList<NalUnit> Parse(ReadOnlySpan<byte> annexB)
    {
        var starts = new List<int>(); // indice del primo byte "01" dello start code + 1 = offset dell'intestazione NAL
        for (int i = 0; i + 2 < annexB.Length; i++)
        {
            if (annexB[i] == 0 && annexB[i + 1] == 0 && annexB[i + 2] == 1)
            {
                starts.Add(i);
                i += 2;
            }
        }

        var result = new List<NalUnit>(starts.Count);
        for (int k = 0; k < starts.Count; k++)
        {
            int offset = starts[k] + 3;
            if (offset >= annexB.Length) continue;

            int end = k + 1 < starts.Count ? starts[k + 1] : annexB.Length;
            if (k + 1 < starts.Count)
                while (end > offset && annexB[end - 1] == 0) end--; // zero iniziale di uno start code a 4 byte

            result.Add(new NalUnit(annexB[offset] & 0x1F, offset, end - offset));
        }
        return result;
    }

    public static bool ContainsKeyframe(ReadOnlySpan<byte> annexB)
    {
        foreach (var nal in Parse(annexB))
            if (nal.Type == NalIdr) return true;
        return false;
    }

    /// <summary>SPS e PPS del flusso, riscritti con start code a 4 byte.</summary>
    public static byte[] ExtractParameterSets(ReadOnlySpan<byte> annexB)
    {
        var output = new List<byte>();
        foreach (var nal in Parse(annexB))
        {
            if (nal.Type != NalSps && nal.Type != NalPps) continue;
            output.AddRange(new byte[] { 0, 0, 0, 1 });
            output.AddRange(annexB.Slice(nal.Offset, nal.Length).ToArray());
        }
        return output.ToArray();
    }
}
