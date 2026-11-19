namespace FreeExtend.Gui.Tests;

public class PowerShellQuotingTests
{
    [Fact] public void Wraps_in_single_quotes() => Assert.Equal(@"'C:\a b\x.ps1'", PowerShellQuoting.Quote(@"C:\a b\x.ps1"));
    [Fact] public void Doubles_embedded_single_quotes() => Assert.Equal(@"'C:\a b\x''y.ps1'", PowerShellQuoting.Quote(@"C:\a b\x'y.ps1"));
    [Fact] public void Empty_string_is_an_empty_literal() => Assert.Equal("''", PowerShellQuoting.Quote(""));
}
