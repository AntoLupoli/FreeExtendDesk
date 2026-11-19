namespace FreeExtend.Host.Display;

public readonly record struct DisplayMode(int Width, int Height, int RefreshHz);

public sealed class NoSuitableModeException(string message) : Exception(message);

public static class ModeSelector
{
    /// <summary>
    /// Sceglie il modo del monitor virtuale piu vicino a quello chiesto dal tablet: quello esatto se esiste; altrimenti, tra i modi con
    /// lo stesso rapporto d'aspetto, risoluzione e refresh non superiori al richiesto, il piu grande (poi il refresh piu alto).
    /// Mai un rapporto d'aspetto diverso: il tablet non deve mostrare bande nere ne immagini deformate.
    /// </summary>
    public static DisplayMode Choose(DisplayMode requested, IReadOnlyList<DisplayMode> supported)
    {
        foreach (var m in supported)
            if (m == requested) return m;

        DisplayMode? best = null;
        foreach (var m in supported)
        {
            bool sameAspect = (long)m.Width * requested.Height == (long)requested.Width * m.Height;
            if (!sameAspect || m.Width > requested.Width || m.Height > requested.Height || m.RefreshHz > requested.RefreshHz) continue;
            if (best is null || Better(m, best.Value)) best = m;
        }
        return best ?? throw new NoSuitableModeException(
            $"No driver mode fits {requested.Width}x{requested.Height}@{requested.RefreshHz} (same aspect ratio, not larger, refresh not higher).");
    }

    private static bool Better(DisplayMode a, DisplayMode b)
    {
        long areaA = (long)a.Width * a.Height, areaB = (long)b.Width * b.Height;
        return areaA != areaB ? areaA > areaB : a.RefreshHz > b.RefreshHz;
    }
}
