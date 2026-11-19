namespace FreeExtend.Host.Display;

/// <summary>Verifica manuale (comando <c>display-test detach|attach [LxA@Hz]</c>): attacca o stacca il monitor virtuale.</summary>
internal static class DisplayTest
{
    public static async Task<int> Run(string[] args)
    {
        string action = args.Length > 0 ? args[0] : "status";
        string? name = VirtualOutputFinder.Find();
        Console.WriteLine($"Monitor virtuale: {name ?? "NON TROVATO"}");
        if (name == null) return 2;
        Console.WriteLine($"Modi del driver: {string.Join(", ", DriverModes.ReadInstalled().Select(m => $"{m.Width}x{m.Height}@{m.RefreshHz}"))}");

        var driver = new VirtualDisplayDriver();
        switch (action)
        {
            case "attach":
                var mode = new DisplayMode(2304, 1440, 60);
                if (args.Length > 1)
                {
                    var p = args[1].Split('x', '@');
                    mode = new DisplayMode(int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]));
                }
                var info = await driver.EnsureAsync(mode, CancellationToken.None);
                Console.WriteLine($"Attaccato: {info.DeviceName} {info.Width}x{info.Height} su {info.AdapterName}");
                break;
            case "detach":
                await new VirtualDisplayDriver().EnsureAsync(DisplayController.CurrentMode(name) ?? new DisplayMode(2304, 1440, 60), CancellationToken.None); // fissa il nome
                DisplayController.Detach(name);
                break;
        }
        Console.WriteLine($"Stato: attaccato={DisplayController.IsAttached(name)}, modo={DisplayController.CurrentMode(name)}");
        return 0;
    }
}
