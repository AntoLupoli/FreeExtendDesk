namespace FreeExtend.Host.Encode;

/// <summary>
/// Nessun encoder hardware utilizzabile con la scheda del monitor virtuale. Non si risolve riprovando: serve intervenire sulla
/// configurazione (su un portatile a due schede, la preferenza grafica "Prestazioni elevate" per questo eseguibile).
/// </summary>
public sealed class EncoderUnavailableException(string adapterName, IReadOnlyList<string> details)
    : InvalidOperationException(Build(adapterName, details))
{
    private static string Build(string adapterName, IReadOnlyList<string> details) =>
        $"Nessun encoder H.264 hardware utilizzabile con la scheda '{adapterName}'. " +
        $"Su un portatile con due schede Windows deve eseguire questo programma sulla scheda NVIDIA: lancia \"{Environment.ProcessPath}\" setup-gpu " +
        "(imposta 'Prestazioni elevate' solo per questo eseguibile) e riavvia. " +
        (details.Count > 0 ? "Dettagli: " + string.Join(" | ", details) : "");
}
