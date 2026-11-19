using FreeExtend.Host.Encode;
using Xunit;

namespace FreeExtend.Host.Tests;

/// <summary>
/// Un encoder hardware funziona solo con un device D3D11 della propria scheda: l'MFT AMD non sa lavorare con una texture della NVIDIA
/// (E_FAIL). Se la scelta cade su quello sbagliato il programma deve saltarlo e dire perche, non riprovare all'infinito.
/// </summary>
public class EncoderVendorTests
{
    [Theory]
    [InlineData("NVIDIA GeForce RTX 3050 Laptop GPU", "NVIDIA H.264 Encoder MFT", true)]
    [InlineData("NVIDIA GeForce RTX 3050 Laptop GPU", "AMDh264Encoder", false)]
    [InlineData("AMD Radeon(TM) Graphics", "AMDh264Encoder", true)]
    [InlineData("AMD Radeon(TM) Graphics", "NVIDIA H.264 Encoder MFT", false)]
    [InlineData("Intel(R) UHD Graphics", "Intel(R) Quick Sync Video H.264 Encoder MFT", true)]
    [InlineData("Intel(R) UHD Graphics", "AMDh264Encoder", false)]
    public void An_encoder_is_compatible_only_with_the_adapter_of_its_own_vendor(string adapter, string encoder, bool expected)
    {
        Assert.Equal(expected, EncoderVendor.IsCompatible(adapter, encoder));
    }

    [Theory]
    [InlineData("Microsoft Basic Render Driver")]
    [InlineData("")]
    public void With_an_unknown_adapter_vendor_nothing_is_filtered_out(string adapter)
    {
        Assert.True(EncoderVendor.IsCompatible(adapter, "AMDh264Encoder"));
        Assert.True(EncoderVendor.IsCompatible(adapter, "NVIDIA H.264 Encoder MFT"));
    }

    [Theory]
    [InlineData("nvidia geforce", "NVIDIA H.264 Encoder MFT", true)]
    [InlineData("ATI Radeon", "AMDh264Encoder", true)]
    public void Matching_ignores_case_and_knows_the_ati_name(string adapter, string encoder, bool expected)
    {
        Assert.Equal(expected, EncoderVendor.IsCompatible(adapter, encoder));
    }
}
