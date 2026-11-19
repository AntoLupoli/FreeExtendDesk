namespace FreeExtend.Gui.Tests;

public class AdbOutputTests
{
    private const string Pkg = "it.freeextend.display";

    [Fact] public void Package_line_is_found() => Assert.True(AdbOutput.IsPackageInstalled("package:it.freeextend.display\r\n", Pkg));
    [Fact] public void A_longer_package_name_does_not_match() => Assert.False(AdbOutput.IsPackageInstalled("package:it.freeextend.display2\n", Pkg));
    [Fact] public void Empty_output_means_not_installed() => Assert.False(AdbOutput.IsPackageInstalled("", Pkg));
    [Fact] public void Model_is_trimmed() => Assert.Equal("SM-X520", AdbOutput.ParseModel("SM-X520\r\n"));
    [Fact] public void Blank_model_is_null() => Assert.Null(AdbOutput.ParseModel("\n"));
}
