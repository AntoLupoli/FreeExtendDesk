namespace FreeExtend.Host.Encode;

/// <summary>
/// Nessun encoder hardware utilizzabile con la scheda del monitor virtuale. Non si risolve riprovando: serve intervenire sulla
/// configurazione (su un portatile a due schede, la preferenza grafica "Prestazioni elevate" per questo eseguibile).
/// </summary>
public sealed class EncoderUnavailableException(string adapterName, IReadOnlyList<string> details)
    : InvalidOperationException(Build(adapterName, details))
{
    private static string Build(string adapterName, IReadOnlyList<string> details) =>
        $"No usable hardware H.264 encoder for the graphics adapter '{adapterName}'. " +
        $"On a laptop with two graphics cards Windows must run this program on the NVIDIA card: run \"{Environment.ProcessPath}\" setup-gpu " +
        "(it sets 'High performance' only for this executable) and restart. " +
        (details.Count > 0 ? "Details: " + string.Join(" | ", details) : "");
}
