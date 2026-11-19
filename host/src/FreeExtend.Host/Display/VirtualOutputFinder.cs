namespace FreeExtend.Host.Display;

/// <summary>Trova il monitor del Virtual Display Driver. Il suo numero (<c>\\.\DISPLAY58</c>, 59, 60...) cambia a ogni reload del driver.</summary>
public static class VirtualOutputFinder
{
    public const string AdapterDescription = "Virtual Display Driver";

    /// <summary>Il primo schermo la cui descrizione e quella del driver virtuale; null se il driver non e installato.</summary>
    public static string? Pick(IEnumerable<(string DeviceName, string DeviceString)> devices)
    {
        foreach (var (name, description) in devices)
            if (description.Contains(AdapterDescription, StringComparison.OrdinalIgnoreCase)) return name;
        return null;
    }

    /// <summary>Come <see cref="Pick"/> sugli schermi reali del sistema.</summary>
    public static string? Find() =>
        Pick(Win32Display.EnumerateDevices().Select(d => (d.DeviceName, d.DeviceString)));
}
