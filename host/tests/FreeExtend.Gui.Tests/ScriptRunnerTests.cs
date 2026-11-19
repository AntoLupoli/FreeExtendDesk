namespace FreeExtend.Gui.Tests;

/// <summary>Runs real PowerShell scripts (not elevated: the UAC path is checked by hand).</summary>
public class ScriptRunnerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fx test's dir " + Guid.NewGuid().ToString("N"));

    public ScriptRunnerTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private string Script(string body)
    {
        string path = Path.Combine(_dir, "my script's.ps1");
        File.WriteAllText(path, body);
        return path;
    }

    [Fact]
    public async Task Output_goes_to_the_log_tail_and_exit_code_is_zero_even_with_spaces_and_quotes_in_the_path()
    {
        var r = await ScriptRunner.RunAsync(Script("Write-Output 'hello from script'"), elevated: false);
        Assert.Equal(0, r.ExitCode);
        Assert.False(r.Cancelled);
        Assert.Contains("hello from script", r.LogTail);
    }

    [Fact]
    public async Task Exit_code_of_the_script_is_returned()
    {
        var r = await ScriptRunner.RunAsync(Script("exit 7"), elevated: false);
        Assert.Equal(7, r.ExitCode);
    }

    [Fact]
    public async Task A_script_that_throws_gives_a_non_zero_exit_code_and_its_message()
    {
        var r = await ScriptRunner.RunAsync(Script("$ErrorActionPreference = 'Stop'; throw 'boom happened'"), elevated: false);
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("boom happened", r.LogTail);
    }

    [Fact]
    public async Task Arguments_are_passed_and_quoted()
    {
        var r = await ScriptRunner.RunAsync(Script("param($a) Write-Output \"got:$a\""), elevated: false, "x y'z");
        Assert.Contains("got:x y'z", r.LogTail);
    }
}
