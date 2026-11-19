using System.Text.RegularExpressions;
using Vortice.DXGI;

namespace FreeExtend.Host.Display;

/// <summary>The real graphics adapters of this PC (no software, remote or virtual ones), by name.</summary>
public static class GpuAdapters
{
    private static readonly Regex NotReal = new("Microsoft Basic|Remote|Virtual Display|Parsec|Meta Virtual|Mirage", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsRealAdapter(string name) => !string.IsNullOrWhiteSpace(name) && !NotReal.IsMatch(name);

    public static IReadOnlyList<string> ListNames()
    {
        var names = new List<string>();
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        for (uint i = 0; factory.EnumAdapters1(i, out IDXGIAdapter1? adapter).Success; i++)
        {
            using (adapter)
            {
                string name = adapter!.Description1.Description;
                if (IsRealAdapter(name) && !names.Contains(name)) names.Add(name);
            }
        }
        return names;
    }
}
