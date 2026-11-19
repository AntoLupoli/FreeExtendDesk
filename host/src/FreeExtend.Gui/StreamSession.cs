using System.Diagnostics;
using FreeExtend.Host.Net;

namespace FreeExtend.Gui;

/// <summary>
/// One run of <c>FreeExtend.Host.exe stream</c> as a hidden child process. Status lines (<see cref="HostStatusLine"/>) become <see cref="Status"/>
/// events; stop is a line "stop" on its stdin, and if this program disappears the closed pipe stops the host too. Events come from thread-pool threads.
/// </summary>
internal sealed class StreamSession : IDisposable
{
    private readonly string _hostExe;
    private Process? _process;
    private volatile string? _lastError;

    public StreamSession(string hostExe) => _hostExe = hostExe;

    public event Action<HostStatus>? Status;
    public event Action<string>? Log;
    public event Action<int, string?>? Exited;

    public bool IsRunning
    {
        get
        {
            try { return _process is { HasExited: false }; }
            catch (Exception) { return false; }
        }
    }

    public void Start()
    {
        if (IsRunning) throw new InvalidOperationException("Already streaming.");
        _lastError = null;
        var psi = new ProcessStartInfo(_hostExe, "stream --status --stop-on-stdin")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            if (HostStatusLine.TryParse(e.Data, out var s)) Status?.Invoke(s);
            else Log?.Invoke(e.Data);
        };
        p.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            _lastError = e.Data;
            Log?.Invoke(e.Data);
        };
        p.Exited += (_, _) =>
        {
            p.WaitForExit();                                   // lets the async readers finish so the last error line is captured
            Exited?.Invoke(p.ExitCode, _lastError);
        };
        _process = p;
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
    }

    /// <summary>Asks the host to stop and waits; kills it (and its children) if it has not exited within <paramref name="timeout"/>.</summary>
    public async Task StopAsync(TimeSpan timeout)
    {
        var p = _process;
        if (p == null) return;
        try { p.StandardInput.WriteLine("stop"); p.StandardInput.Flush(); } catch (Exception) { /* already closed or exited */ }
        bool exited = await Task.Run(() => { try { return p.WaitForExit((int)timeout.TotalMilliseconds); } catch (Exception) { return true; } });
        if (!exited)
        {
            try { p.Kill(entireProcessTree: true); } catch (Exception) { }
        }
    }

    public void Dispose()
    {
        // Closing stdin makes a still-running host stop by itself; we do not kill it here.
        try { _process?.StandardInput.Close(); } catch (Exception) { }
    }
}
