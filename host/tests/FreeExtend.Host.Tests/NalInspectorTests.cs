using FreeExtend.Host.Encode;
using Xunit;

namespace FreeExtend.Host.Tests;

public class NalInspectorTests
{
    private static byte[] Hex(string s) =>
        s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(h => Convert.ToByte(h, 16)).ToArray();

    [Fact]
    public void Parse_finds_sps_pps_idr_with_offsets_and_lengths()
    {
        // SPS (4 byte di start code) + PPS (4 byte) + IDR (3 byte di start code)
        var data = Hex("00 00 00 01 67 AA  00 00 00 01 68 BB  00 00 01 65 CC");

        var nals = NalInspector.Parse(data);

        Assert.Equal(new[] { 7, 8, 5 }, nals.Select(n => n.Type).ToArray());
        // Offset = posizione del byte di intestazione NAL (dopo lo start code); Length comprende l'intestazione.
        Assert.Equal((4, 2), (nals[0].Offset, nals[0].Length));
        Assert.Equal((10, 2), (nals[1].Offset, nals[1].Length));
        Assert.Equal((15, 2), (nals[2].Offset, nals[2].Length));
    }

    [Fact]
    public void Parse_returns_empty_without_start_codes()
    {
        Assert.Empty(NalInspector.Parse(Hex("01 02 03 04 05")));
    }

    [Fact]
    public void Parse_returns_empty_for_empty_input()
    {
        Assert.Empty(NalInspector.Parse(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Parse_ignores_trailing_zero_bytes_of_next_start_code_in_length()
    {
        // Il NAL SPS e lungo 2 byte (67 AA): gli zeri dello start code a 4 byte successivo non vanno contati.
        var nals = NalInspector.Parse(Hex("00 00 01 67 AA 00 00 00 01 68 BB"));

        Assert.Equal(2, nals.Count);
        Assert.Equal(2, nals[0].Length);
        Assert.Equal(2, nals[1].Length);
    }

    [Fact]
    public void ContainsKeyframe_true_only_when_idr_present()
    {
        Assert.True(NalInspector.ContainsKeyframe(Hex("00 00 00 01 67 AA 00 00 00 01 68 BB 00 00 01 65 CC")));
        Assert.False(NalInspector.ContainsKeyframe(Hex("00 00 00 01 41 AA BB")));
    }

    [Fact]
    public void ExtractParameterSets_returns_sps_and_pps_in_annexb()
    {
        var data = Hex("00 00 00 01 09 F0  00 00 00 01 67 AA  00 00 00 01 68 BB  00 00 01 65 CC");

        var ps = NalInspector.ExtractParameterSets(data);

        Assert.Equal(Hex("00 00 00 01 67 AA 00 00 00 01 68 BB"), ps);
    }
}
