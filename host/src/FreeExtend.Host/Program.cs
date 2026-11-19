using FreeExtend.Host.Capture;

namespace FreeExtend.Host;

public static class Program
{
    [System.Runtime.InteropServices.DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint ms);

    public static int Main(string[] args)
    {
        // Timer di sistema a 1 ms: le attese interne degli encoder hardware si risvegliano in tempo (default 15,6 ms).
        if (Environment.GetEnvironmentVariable("FX_NO_TIMER_RES") != "1") timeBeginPeriod(1);

        if (args.Length == 0)
        {
            Console.WriteLine("Uso: FreeExtend.Host <comando>\n  list-outputs\n  capture-test [--output <nome>] [--seconds N] [--png file]");
            return 1;
        }

        switch (args[0])
        {
            case "list-outputs":
                foreach (var o in DesktopDuplicationSource.ListOutputs())
                    Console.WriteLine($"{o.DeviceName,-14} {o.Width}x{o.Height,-6} attached={o.AttachedToDesktop,-5} adapter={o.AdapterName}");
                return 0;

            case "encoder-probe":
                return Encode.EncoderProbe.Run();

            case "encoder-probe-adapters":
                return Encode.EncoderProbe.RunForAdapters();

            case "gpu-status":
                return SetupGpu.Status();

            case "setup-gpu":
                return SetupGpu.Run(args.Length > 1 && args[1] == "--remove");

            case "virtual-output":
                Console.WriteLine(Display.VirtualOutputFinder.Find() ?? "");
                return Display.VirtualOutputFinder.Find() == null ? 2 : 0;

            case "display-test":
                return Display.DisplayTest.Run(args[1..]).GetAwaiter().GetResult();

            case "stream":
                return StreamCommand.Run(args[1..]);

            case "tunnel-test":
                return Net.TunnelTest.Run(args[1..]).GetAwaiter().GetResult();

            case "encoder-cycle-test":
                return Encode.EncoderCycleTest.Run(args[1..]);

            case "encode-synthetic":
                return Encode.SyntheticTest.Run(args[1..]);

            case "encode-test":
                return Encode.EncodeTest.Run(args[1..]);

            case "capture-test":
                return CaptureTest.Run(args[1..]);

            default:
                Console.Error.WriteLine($"Comando sconosciuto: {args[0]}");
                return 1;
        }
    }
}
