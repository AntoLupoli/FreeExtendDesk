using FreeExtend.Host.Display;

namespace FreeExtend.Host;

/// <summary>
/// Comando <c>setup-gpu</c>: imposta "Prestazioni elevate" (Impostazioni > Sistema > Schermo > Grafica) solo per questo eseguibile.
/// Serve su un portatile con due schede: senza, l'encoder NVIDIA non si attiva. Si annulla con <c>setup-gpu --remove</c>.
/// </summary>
internal static class SetupGpu
{
    /// <summary>Codice 0 se per questo eseguibile e gia impostato "Prestazioni elevate", 1 altrimenti (lo usa Start-FreeExtend.ps1).</summary>
    public static int Status()
    {
        string? exe = Environment.ProcessPath;
        bool set = exe != null && new GpuPreference().IsHighPerformance(exe);
        Console.WriteLine(set ? $"High performance: set for {exe}" : $"High performance: NOT set for {exe}");
        return set ? 0 : 1;
    }

    public static int Run(bool remove = false)
    {
        string? exe = Environment.ProcessPath;
        if (exe == null || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Run this command from FreeExtend.Host.exe (not with 'dotnet ...'): the preference is bound to the exe path.");
            return 2;
        }
        var pref = new GpuPreference();
        if (remove)
        {
            pref.Remove(exe);
            Console.WriteLine($"Graphics preference removed for: {exe}");
            return 0;
        }
        bool was = pref.IsHighPerformance(exe);
        pref.SetHighPerformance(exe);
        Console.WriteLine(was ? $"Already set: {exe}" : $"Set 'High performance' only for: {exe}");
        Console.WriteLine("Undo with: setup-gpu --remove  (or in Settings > System > Display > Graphics). Now restart FreeExtend.");
        return 0;
    }
}
