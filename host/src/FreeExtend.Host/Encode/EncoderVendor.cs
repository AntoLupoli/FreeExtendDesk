namespace FreeExtend.Host.Encode;

/// <summary>
/// Un encoder hardware di Media Foundation lavora solo con un device D3D11 creato sulla scheda del suo produttore: l'MFT AMD con una
/// texture della NVIDIA (e viceversa) fallisce con un E_FAIL generico. Riconosce il produttore dal nome della scheda e dell'encoder.
/// </summary>
public static class EncoderVendor
{
    private static readonly (string AdapterToken, string EncoderToken)[] Vendors =
    {
        ("nvidia", "nvidia"),
        ("amd", "amd"),
        ("ati", "amd"),          // le schede piu vecchie si chiamano "ATI Radeon", ma l'encoder e AMD
        ("radeon", "amd"),
        ("intel", "intel"),
    };

    /// <summary>
    /// Vero se l'encoder puo lavorare con questa scheda. Se il produttore della scheda non e riconoscibile (es. "Microsoft Basic Render
    /// Driver") non si filtra nulla: si prova e basta.
    /// </summary>
    public static bool IsCompatible(string adapterName, string encoderName)
    {
        foreach (var (adapterToken, encoderToken) in Vendors)
        {
            if (adapterName.Contains(adapterToken, StringComparison.OrdinalIgnoreCase))
                return encoderName.Contains(encoderToken, StringComparison.OrdinalIgnoreCase);
        }
        return true;
    }
}
