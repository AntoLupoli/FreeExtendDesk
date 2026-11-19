using FreeExtend.Host.Display;
using Microsoft.Win32;
using Xunit;

namespace FreeExtend.Host.Tests;

public class GpuPreferenceTests : IDisposable
{
    private readonly string _key = @"Software\FreeExtendTests\UserGpuPreferences-" + Guid.NewGuid().ToString("N");

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(_key, throwOnMissingSubKey: false);

    [Fact]
    public void Not_set_until_someone_sets_it()
    {
        var store = new GpuPreference(_key);
        Assert.False(store.IsHighPerformance(@"C:\Programmi\FreeExtend\FreeExtend.Host.exe"));
    }

    [Fact]
    public void Set_writes_the_windows_format_for_that_exe_only()
    {
        var store = new GpuPreference(_key);
        store.SetHighPerformance(@"C:\A\FreeExtend.Host.exe");

        Assert.True(store.IsHighPerformance(@"C:\A\FreeExtend.Host.exe"));
        Assert.False(store.IsHighPerformance(@"C:\B\FreeExtend.Host.exe"));      // un altro percorso non e coperto
        using var k = Registry.CurrentUser.OpenSubKey(_key)!;
        Assert.Equal("GpuPreference=2;", k.GetValue(@"C:\A\FreeExtend.Host.exe"));   // formato di Impostazioni > Grafica
    }

    [Fact]
    public void Other_preferences_do_not_count_as_high_performance()
    {
        using (var k = Registry.CurrentUser.CreateSubKey(_key)) k.SetValue(@"C:\A\x.exe", "GpuPreference=1;");   // risparmio energetico
        Assert.False(new GpuPreference(_key).IsHighPerformance(@"C:\A\x.exe"));
    }

    [Fact]
    public void Remove_deletes_only_that_entry()
    {
        var store = new GpuPreference(_key);
        store.SetHighPerformance(@"C:\A\x.exe");
        store.SetHighPerformance(@"C:\B\x.exe");

        store.Remove(@"C:\A\x.exe");

        Assert.False(store.IsHighPerformance(@"C:\A\x.exe"));
        Assert.True(store.IsHighPerformance(@"C:\B\x.exe"));
    }
}
