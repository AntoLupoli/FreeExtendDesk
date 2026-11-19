namespace FreeExtend.Gui.Tests;

public class HostExitMessagesTests
{
    [Fact] public void Zero_is_a_normal_stop() => Assert.Equal("Stopped", HostExitMessages.Describe(0, null));
    [Fact] public void One_includes_the_last_error_line() => Assert.Equal("Streaming failed: boom", HostExitMessages.Describe(1, "boom"));
    [Fact] public void One_without_detail() => Assert.Equal("Streaming failed", HostExitMessages.Describe(1, null));
    [Fact] public void Two_is_the_driver() => Assert.Equal("Virtual display driver not installed or no display modes configured", HostExitMessages.Describe(2, "ignored"));
    [Fact] public void Three_is_the_tablet() => Assert.Equal("Tablet not connected or not authorized", HostExitMessages.Describe(3, null));
    [Fact] public void Four_is_the_encoder_with_detail() => Assert.Equal("No usable hardware H.264 encoder: x", HostExitMessages.Describe(4, "x"));
    [Fact] public void Unknown_code_is_reported() => Assert.Equal("Host stopped unexpectedly (code 99)", HostExitMessages.Describe(99, null));
}
