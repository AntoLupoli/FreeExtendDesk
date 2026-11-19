using FreeExtend.Host.Capture;

namespace FreeExtend.Host.Display;

public interface IVirtualDisplay
{
    /// <summary>Fa in modo che il monitor virtuale sia attaccato al desktop con questo modo, e lo restituisce.</summary>
    Task<OutputInfo> EnsureAsync(DisplayMode mode, CancellationToken ct);

    /// <summary>Stacca il monitor dal desktop (il driver non permette di toglierlo del tutto senza amministratore).</summary>
    Task RemoveAsync();
}

/// <summary>
/// Il monitor del Virtual Display Driver. Il driver e gia installato e il suo monitor esiste sempre (<c>SETDISPLAYCOUNT 0</c> diventa 1):
/// "creare" significa attaccarlo al desktop con il modo giusto, "rimuovere" significa staccarlo.
/// </summary>
public sealed class VirtualDisplayDriver : IVirtualDisplay
{
    private string? _deviceName;

    public async Task<OutputInfo> EnsureAsync(DisplayMode mode, CancellationToken ct)
    {
        string name = VirtualOutputFinder.Find()
            ?? throw new InvalidOperationException("Virtual Display Driver non installato (vedi tools/virtual-display/README.md).");
        _deviceName = name;

        DisplayController.AttachWithMode(name, mode);

        // Windows applica la modifica in modo asincrono: si aspetta che l'uscita compaia in DXGI con le dimensioni giuste.
        for (int i = 0; i < 100; i++)
        {
            ct.ThrowIfCancellationRequested();
            var info = DesktopDuplicationSource.ListOutputs().FirstOrDefault(o =>
                o.AttachedToDesktop && string.Equals(o.DeviceName.TrimStart('\\', '.'), name.TrimStart('\\', '.'), StringComparison.OrdinalIgnoreCase));
            if (info != null && info.Width == mode.Width && info.Height == mode.Height) return info;
            await Task.Delay(100, ct);
        }
        throw new TimeoutException($"Il monitor virtuale {name} non e comparso a {mode.Width}x{mode.Height} entro 10 secondi.");
    }

    public Task RemoveAsync()
    {
        if (_deviceName != null) DisplayController.Detach(_deviceName);
        return Task.CompletedTask;
    }
}
