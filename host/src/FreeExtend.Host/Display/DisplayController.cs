using static FreeExtend.Host.Display.Win32Display;

namespace FreeExtend.Host.Display;

/// <summary>
/// Attacca o stacca il monitor virtuale dal desktop di Windows e ne imposta la modalita (come "Estendi questo desktop" /
/// "Scollega questo schermo" nelle Impostazioni). Tocca solo lo schermo indicato; le altre posizioni non cambiano.
/// </summary>
public static class DisplayController
{
    public static bool IsAttached(string deviceName)
    {
        foreach (var d in EnumerateDevices())
            if (NameEquals(d.DeviceName, deviceName)) return (d.StateFlags & AttachedToDesktop) != 0;
        return false;
    }

    /// <summary>Modo attuale dello schermo, o null se non e attaccato.</summary>
    public static DisplayMode? CurrentMode(string deviceName)
    {
        var dm = NewMode();
        if (!EnumDisplaySettingsW(deviceName, ENUM_CURRENT_SETTINGS, ref dm) || dm.dmPelsWidth == 0) return null;
        return new DisplayMode(dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency);
    }

    /// <summary>Lo schermo e attaccato con questo modo? Se si non fa nulla. Altrimenti lo attacca (a destra degli altri) o ne cambia il modo.</summary>
    public static void AttachWithMode(string deviceName, DisplayMode mode)
    {
        bool attached = IsAttached(deviceName);
        if (attached && CurrentMode(deviceName) == mode) return;

        var dm = NewMode();
        dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY | DM_POSITION;
        dm.dmPelsWidth = mode.Width;
        dm.dmPelsHeight = mode.Height;
        dm.dmDisplayFrequency = mode.RefreshHz;
        if (attached)
        {
            // gia nel desktop: si tiene la posizione attuale
            var cur = NewMode();
            EnumDisplaySettingsW(deviceName, ENUM_CURRENT_SETTINGS, ref cur);
            dm.dmPositionX = cur.dmPositionX;
            dm.dmPositionY = cur.dmPositionY;
        }
        else
        {
            dm.dmPositionX = RightEdgeOfAttachedDisplays(except: deviceName);
            dm.dmPositionY = 0;
        }
        Apply(deviceName, ref dm);
    }

    /// <summary>Stacca lo schermo dal desktop: le finestre che c'erano tornano sugli altri schermi.</summary>
    public static void Detach(string deviceName)
    {
        if (!IsAttached(deviceName)) return;
        var dm = NewMode();
        dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_POSITION;   // larghezza e altezza 0 = scollega
        Apply(deviceName, ref dm);
    }

    private static void Apply(string deviceName, ref DEVMODE dm)
    {
        int r = ChangeDisplaySettingsExW(deviceName, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY | CDS_NORESET, IntPtr.Zero);
        if (r != DISP_CHANGE_SUCCESSFUL)
            throw new InvalidOperationException($"Windows ha rifiutato la modifica di {deviceName} (codice {r}: 1=riavvio, -1=non riuscita, -2=modo non valido, -5=flag non validi).");
        r = ChangeDisplaySettingsExW(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);   // applica le modifiche in sospeso
        if (r != DISP_CHANGE_SUCCESSFUL)
            throw new InvalidOperationException($"Windows non ha applicato la configurazione degli schermi (codice {r}).");
    }

    private static int RightEdgeOfAttachedDisplays(string except)
    {
        int right = 0;
        foreach (var d in EnumerateDevices())
        {
            if ((d.StateFlags & AttachedToDesktop) == 0 || NameEquals(d.DeviceName, except)) continue;
            var dm = NewMode();
            if (EnumDisplaySettingsW(d.DeviceName, ENUM_CURRENT_SETTINGS, ref dm))
                right = Math.Max(right, dm.dmPositionX + dm.dmPelsWidth);
        }
        return right;
    }

    private static bool NameEquals(string a, string b) =>
        string.Equals(a.TrimStart('\\', '.'), b.TrimStart('\\', '.'), StringComparison.OrdinalIgnoreCase);
}
