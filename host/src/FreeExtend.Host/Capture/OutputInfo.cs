namespace FreeExtend.Host.Capture;

/// <summary>Un'uscita video vista da DXGI. <see cref="DeviceName"/> e il nome Windows (es. <c>\\.\DISPLAY58</c>).</summary>
public sealed record OutputInfo(string DeviceName, string AdapterName, int Width, int Height, bool AttachedToDesktop);
