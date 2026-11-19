namespace FreeExtend.Host;

/// <summary>
/// Lets a controlling program stop <c>stream</c> cleanly without a console: a line "stop" on stdin, or stdin closing (the controller
/// exited or crashed), calls <c>onStop</c> once. Other lines are ignored. Exceptions from <c>onStop</c> are swallowed (the host is already closing).
/// </summary>
internal static class StdinStopWatcher
{
    public static Thread Start(TextReader input, Action onStop)
    {
        var thread = new Thread(() =>
        {
            try
            {
                while (true)
                {
                    string? line = input.ReadLine();
                    if (line is null || line.Trim().Equals("stop", StringComparison.OrdinalIgnoreCase)) break;
                }
            }
            catch (Exception) { /* a broken pipe means the controller is gone: stop as well */ }
            try { onStop(); } catch (Exception) { /* closing anyway */ }
        }) { IsBackground = true, Name = "stdin-stop" };
        thread.Start();
        return thread;
    }
}
