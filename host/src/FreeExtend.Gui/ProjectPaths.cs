namespace FreeExtend.Gui;

/// <summary>Where things live inside the FreeExtend folder. The host exe is always the canonical Debug build: the Windows graphics preference is bound to its exact path.</summary>
internal sealed record ProjectPaths(string Root)
{
    public string HostExe => Path.Combine(Root, @"host\src\FreeExtend.Host\bin\Debug\net8.0-windows\FreeExtend.Host.exe");
    public string InstallDriverScript => Path.Combine(Root, @"tools\virtual-display\install.ps1");
    public string RemoveDriverScript => Path.Combine(Root, @"tools\virtual-display\remove.ps1");
    public string InstallAppScript => Path.Combine(Root, @"tools\install-app.ps1");

    /// <summary>Walks up from <paramref name="startDir"/> to the first folder that contains Start-FreeExtend.ps1; null if none.</summary>
    public static string? FindRoot(string startDir, Func<string, bool> fileExists)
    {
        string? dir = startDir;
        while (!string.IsNullOrEmpty(dir))
        {
            if (fileExists(Path.Combine(dir, "Start-FreeExtend.ps1"))) return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
