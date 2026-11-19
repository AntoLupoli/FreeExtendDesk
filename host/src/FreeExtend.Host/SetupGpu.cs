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
        Console.WriteLine(set ? $"Prestazioni elevate: impostato per {exe}" : $"Prestazioni elevate: NON impostato per {exe}");
        return set ? 0 : 1;
    }

    public static int Run(bool remove = false)
    {
        string? exe = Environment.ProcessPath;
        if (exe == null || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Lancia questo comando dall'eseguibile FreeExtend.Host.exe (non con 'dotnet ...'): la preferenza si lega al percorso dell'exe.");
            return 2;
        }
        var pref = new GpuPreference();
        if (remove)
        {
            pref.Remove(exe);
            Console.WriteLine($"Preferenza grafica rimossa per: {exe}");
            return 0;
        }
        bool was = pref.IsHighPerformance(exe);
        pref.SetHighPerformance(exe);
        Console.WriteLine(was ? $"Gia impostata: {exe}" : $"Impostato 'Prestazioni elevate' solo per: {exe}");
        Console.WriteLine("Si annulla con: setup-gpu --remove  (oppure da Impostazioni > Sistema > Schermo > Grafica). Ora riavvia FreeExtend.");
        return 0;
    }
}
