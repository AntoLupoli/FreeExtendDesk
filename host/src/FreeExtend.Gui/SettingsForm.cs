namespace FreeExtend.Gui;

/// <summary>Settings: only "Uninstall everything". Returns <see cref="DialogResult.Yes"/> when the user confirmed it; the main window does the work.</summary>
internal sealed class SettingsForm : Form
{
    public SettingsForm(bool tabletConnected)
    {
        Text = "FreeExtend settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(380, 120);

        var info = new Label
        {
            Text = "Remove everything FreeExtend installed on this PC (and on the tablet, if it is connected).\nThe FreeExtend folder itself is not deleted.",
            Location = new Point(16, 14), Size = new Size(348, 40),
        };
        var uninstall = new Button { Text = "Uninstall everything...", Location = new Point(16, 66), Size = new Size(180, 36) };
        var close = new Button { Text = "Close", Location = new Point(284, 66), Size = new Size(80, 36), DialogResult = DialogResult.Cancel };
        uninstall.Click += (_, _) =>
        {
            string items =
                "This will remove:\n" +
                "  - the virtual display driver (Windows will ask for administrator permission)\n" +
                "  - the \"High performance\" graphics preference for FreeExtend.Host.exe\n" +
                (tabletConnected ? "  - the FreeExtend app on the tablet\n" : "  - (the tablet is not connected, so the app stays on it)\n") +
                "\nThe FreeExtend folder is kept. Continue?";
            if (MessageBox.Show(this, items, "Uninstall everything", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                DialogResult = DialogResult.Yes;
        };
        Controls.AddRange(new Control[] { info, uninstall, close });
        CancelButton = close;
    }
}
