using System.Xml;
using System.Xml.Linq;

namespace FreeExtend.Host.Display;

/// <summary>I modi che il Virtual Display Driver offre, letti dal suo <c>vdd_settings.xml</c>.</summary>
public static class DriverModes
{
    public const string InstalledSettingsPath = @"C:\VirtualDisplayDriver\vdd_settings.xml";

    /// <summary>
    /// Ogni risoluzione con il proprio refresh piu quelli globali (<c>g_refresh_rate</c>, replicati su tutte le risoluzioni).
    /// XML vuoto o malformato: elenco vuoto, mai un'eccezione.
    /// </summary>
    public static IReadOnlyList<DisplayMode> Parse(string xml)
    {
        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch (XmlException) { return Array.Empty<DisplayMode>(); }

        var globals = doc.Descendants("g_refresh_rate").Select(e => ParseInt(e.Value)).Where(v => v > 0).ToList();
        var modes = new List<DisplayMode>();
        foreach (var r in doc.Descendants("resolution"))
        {
            int w = ParseInt(r.Element("width")?.Value), h = ParseInt(r.Element("height")?.Value);
            if (w <= 0 || h <= 0) continue;
            var rates = new List<int>();
            int own = ParseInt(r.Element("refresh_rate")?.Value);
            if (own > 0) rates.Add(own);
            rates.AddRange(globals);
            foreach (int hz in rates) modes.Add(new DisplayMode(w, h, hz));
        }
        return modes.Distinct().ToList();
    }

    /// <summary>Modi del driver installato; vuoto se il file non c'e.</summary>
    public static IReadOnlyList<DisplayMode> ReadInstalled() =>
        File.Exists(InstalledSettingsPath) ? Parse(File.ReadAllText(InstalledSettingsPath)) : Array.Empty<DisplayMode>();

    private static int ParseInt(string? s) => int.TryParse(s?.Trim(), out int v) ? v : 0;
}
