namespace FreeExtend.Gui.Tests;

public class ProjectPathsTests
{
    [Fact]
    public void FindRoot_walks_up_to_the_folder_with_Start_FreeExtend()
    {
        string? root = ProjectPaths.FindRoot(@"C:\x\host\src\FreeExtend.Gui\bin\Debug", p => p == @"C:\x\Start-FreeExtend.ps1");
        Assert.Equal(@"C:\x", root);
    }

    [Fact]
    public void FindRoot_returns_null_when_nothing_is_found() =>
        Assert.Null(ProjectPaths.FindRoot(@"C:\x\y", _ => false));

    [Fact]
    public void Paths_are_the_canonical_ones_even_with_spaces_in_the_root()
    {
        var p = new ProjectPaths(@"C:\My Projects\Free Extend");
        Assert.Equal(@"C:\My Projects\Free Extend\host\src\FreeExtend.Host\bin\Debug\net8.0-windows\FreeExtend.Host.exe", p.HostExe);
        Assert.Equal(@"C:\My Projects\Free Extend\tools\virtual-display\install.ps1", p.InstallDriverScript);
        Assert.Equal(@"C:\My Projects\Free Extend\tools\virtual-display\remove.ps1", p.RemoveDriverScript);
        Assert.Equal(@"C:\My Projects\Free Extend\tools\install-app.ps1", p.InstallAppScript);
    }
}
