using System.Runtime.InteropServices;

namespace FreeExtend.Host.Display;

/// <summary>
/// Stacca il monitor virtuale anche quando il programma non finisce con Ctrl+C: chiusura della finestra della console, logoff,
/// spegnimento, eccezione non gestita su un altro thread. Il distacco e idempotente (se il monitor e gia staccato non fa nulla).
/// </summary>
internal static class ConsoleExitGuard
{
    private delegate bool HandlerRoutine(uint ctrlType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCtrlHandler(HandlerRoutine handler, bool add);

    private const uint CtrlCloseEvent = 2, CtrlLogoffEvent = 5, CtrlShutdownEvent = 6;
    private static HandlerRoutine? _handler;               // deve restare referenziato: il GC non deve raccoglierlo

    public static void Register(Action onExit)
    {
        _handler = ctrlType =>
        {
            if (ctrlType is CtrlCloseEvent or CtrlLogoffEvent or CtrlShutdownEvent) Safe(onExit);
            return false;                                  // lascia che Windows termini il processo
        };
        SetConsoleCtrlHandler(_handler, true);
        AppDomain.CurrentDomain.UnhandledException += (_, _) => Safe(onExit);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Safe(onExit);
    }

    private static void Safe(Action a)
    {
        try { a(); } catch { /* si sta chiudendo: nulla da fare */ }
    }
}
