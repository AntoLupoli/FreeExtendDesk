using FreeExtend.Host;

namespace FreeExtend.Host.Tests;

public class StdinStopWatcherTests
{
    private sealed class ThrowingReader : TextReader
    {
        public override string? ReadLine() => throw new IOException("pipe closed");
    }

    private static int RunAndCount(TextReader reader, Action? extra = null)
    {
        int calls = 0;
        using var fired = new ManualResetEventSlim();
        var t = StdinStopWatcher.Start(reader, () => { Interlocked.Increment(ref calls); fired.Set(); extra?.Invoke(); });
        Assert.True(fired.Wait(5000), "onStop was not called");
        Assert.True(t.Join(5000), "watcher thread did not end");
        return calls;
    }

    [Fact] public void Stop_line_triggers_once() => Assert.Equal(1, RunAndCount(new StringReader("stop\n")));
    [Fact] public void Other_lines_are_ignored_and_stop_is_case_and_space_insensitive() => Assert.Equal(1, RunAndCount(new StringReader("hello\nSTOP \n")));
    [Fact] public void End_of_input_triggers() => Assert.Equal(1, RunAndCount(new StringReader("")));
    [Fact] public void Read_error_triggers() => Assert.Equal(1, RunAndCount(new ThrowingReader()));
    [Fact] public void Stop_followed_by_end_of_input_still_calls_once() => Assert.Equal(1, RunAndCount(new StringReader("stop\nstop\n")));
    [Fact] public void Exception_from_onStop_does_not_escape() => Assert.Equal(1, RunAndCount(new StringReader("stop\n"), () => throw new ObjectDisposedException("cts")));
}
