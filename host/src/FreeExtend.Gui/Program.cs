namespace FreeExtend.Gui;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // One window at a time: two would fight over the host's TCP port (27183) and the virtual monitor.
        using var mutex = new Mutex(initiallyOwned: true, @"Local\FreeExtend.Gui", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("FreeExtend is already running.", "FreeExtend", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        string? root = ProjectPaths.FindRoot(AppContext.BaseDirectory, File.Exists);
        if (root == null)
        {
            MessageBox.Show("Could not find the FreeExtend folder (Start-FreeExtend.ps1).", "FreeExtend", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        Application.Run(new MainForm(new ProjectPaths(root)));
    }
}
