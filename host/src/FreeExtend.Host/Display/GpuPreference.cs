using Microsoft.Win32;

namespace FreeExtend.Host.Display;

/// <summary>
/// La preferenza grafica per singola app di Windows (Impostazioni > Sistema > Schermo > Grafica), salvata nel registro dell'utente come
/// <c>percorso exe = "GpuPreference=2;"</c>. 2 = prestazioni elevate. Su un portatile con due schede serve al programma per far attivare
/// l'encoder NVIDIA. La preferenza e legata al percorso esatto dell'eseguibile.
/// </summary>
public sealed class GpuPreference
{
    public const string DefaultKeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private const string HighPerformanceValue = "GpuPreference=2;";

    private readonly string _keyPath;

    public GpuPreference(string keyPath = DefaultKeyPath) => _keyPath = keyPath;

    public bool IsHighPerformance(string exePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(_keyPath);
        return key?.GetValue(exePath) is string v && v.Contains("GpuPreference=2", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Scrive solo la voce di questo eseguibile.</summary>
    public void SetHighPerformance(string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(_keyPath);
        key.SetValue(exePath, HighPerformanceValue, RegistryValueKind.String);
    }

    public void Remove(string exePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(_keyPath, writable: true);
        key?.DeleteValue(exePath, throwOnMissingValue: false);
    }
}
