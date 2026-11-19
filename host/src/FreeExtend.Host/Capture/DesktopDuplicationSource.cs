using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace FreeExtend.Host.Capture;

/// <summary>
/// Cattura un'uscita video con DXGI Desktop Duplication. Il device D3D11 nasce sullo stesso adattatore dell'uscita.
/// Quando Windows toglie l'accesso (blocco con Win+L, UAC, cambio risoluzione, sospensione) la duplicazione viene
/// ricreata in automatico: nel frattempo <see cref="TryAcquireFrame"/> restituisce <c>false</c>.
/// </summary>
public sealed class DesktopDuplicationSource : IFrameSource
{
    private const int RecreateRetryIntervalMs = 100;

    private readonly string _outputDeviceName;
    private IDXGIAdapter1 _adapter = null!;
    private IDXGIOutput1 _output = null!;
    private ID3D11Device _device = null!;
    private ID3D11DeviceContext _context = null!;
    private IDXGIOutputDuplication? _duplication;
    private ID3D11Texture2D? _copy;
    private long _lastRecreateAttemptTicks;

    public ID3D11Device Device => _device;
    /// <summary>La GPU e stata persa (device removed/reset): questa sorgente non puo piu funzionare e va ricostruita da capo.</summary>
    public bool DeviceLost { get; private set; }

    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <param name="outputDeviceName">Nome Windows dell'uscita (es. <c>\\.\DISPLAY58</c>); null = prima uscita attaccata al desktop.</param>
    public DesktopDuplicationSource(string? outputDeviceName)
    {
        _outputDeviceName = Bind(outputDeviceName);
        CreateDevice();
        TryCreateDuplication();
    }

    public static IReadOnlyList<OutputInfo> ListOutputs()
    {
        var result = new List<OutputInfo>();
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        for (uint a = 0; factory.EnumAdapters1(a, out IDXGIAdapter1? adapter).Success; a++)
        {
            using (adapter)
            {
                string adapterName = adapter!.Description1.Description;
                for (uint o = 0; adapter.EnumOutputs(o, out IDXGIOutput? output).Success; o++)
                {
                    using (output)
                    {
                        var d = output!.Description;
                        var r = d.DesktopCoordinates;
                        result.Add(new OutputInfo(d.DeviceName, adapterName, r.Right - r.Left, r.Bottom - r.Top, d.AttachedToDesktop));
                    }
                }
            }
        }
        return result;
    }

    private string Bind(string? wanted)
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        for (uint a = 0; factory.EnumAdapters1(a, out IDXGIAdapter1? adapter).Success; a++)
        {
            for (uint o = 0; adapter!.EnumOutputs(o, out IDXGIOutput? output).Success; o++)
            {
                var d = output!.Description;
                bool match = wanted == null ? d.AttachedToDesktop : NameMatches(d.DeviceName, wanted);
                if (match)
                {
                    _adapter = adapter;
                    _output = output.QueryInterface<IDXGIOutput1>();
                    output.Dispose();
                    var r = d.DesktopCoordinates;
                    Width = r.Right - r.Left;
                    Height = r.Bottom - r.Top;
                    return d.DeviceName;
                }
                output.Dispose();
            }
            adapter!.Dispose();
        }
        throw new InvalidOperationException(wanted == null
            ? "Nessuna uscita attaccata al desktop"
            : $"Uscita '{wanted}' non trovata (usa il comando list-outputs)");
    }

    /// <summary>Accetta sia <c>\\.\DISPLAY58</c> sia la forma corta <c>DISPLAY58</c> (comoda da riga di comando).</summary>
    internal static bool NameMatches(string deviceName, string wanted) =>
        string.Equals(deviceName.TrimStart('\\', '.'), wanted.TrimStart('\\', '.'), StringComparison.OrdinalIgnoreCase);

    private void CreateDevice()
    {
        var result = D3D11.D3D11CreateDevice(
            _adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
            new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 },
            out _device!, out _context!);
        result.CheckError();
        try { using var dxgi = _device.QueryInterface<IDXGIDevice>(); dxgi.GPUThreadPriority = 7; } catch { /* priorita non concessa: si prosegue */ }
    }

    /// <summary>Prova a (ri)creare la duplicazione; restituisce false se Windows non la concede ancora.</summary>
    private bool TryCreateDuplication()
    {
        _duplication?.Dispose();
        _duplication = null;
        try
        {
            _duplication = _output.DuplicateOutput(_device);
        }
        catch (Exception ex) when (ex is SharpGen.Runtime.SharpGenException or InvalidOperationException)
        {
            return false;
        }

        // Dimensioni reali dei frame: le legge la duplicazione stessa, senza dipendere dal DPI del processo.
        var mode = _duplication.Description.ModeDescription;
        int w = (int)mode.Width, h = (int)mode.Height;
        if (_copy == null || w != Width || h != Height)
        {
            Width = w;
            Height = h;
            _copy?.Dispose();
            _copy = null;
        }
        _copy ??= _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)Width,
            Height = (uint)Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
        });
        return true;
    }

    public bool TryAcquireFrame(int timeoutMs, out CapturedFrame frame, Func<bool>? wanted = null)
    {
        frame = default;

        if (_duplication == null)
        {
            // Accesso perso in precedenza: riprova a intervalli brevi senza bloccare chi chiama.
            long now = Environment.TickCount64;
            if (now - _lastRecreateAttemptTicks < RecreateRetryIntervalMs)
            {
                Thread.Sleep(Math.Min(timeoutMs, RecreateRetryIntervalMs));
                return false;
            }
            _lastRecreateAttemptTicks = now;
            if (!TryCreateDuplication())
            {
                Thread.Sleep(Math.Min(timeoutMs, RecreateRetryIntervalMs));
                return false;
            }
        }

        // Interrogazione con timeout 0 + Sleep(1) invece di un'unica attesa lunga: una AcquireNextFrame in attesa tiene bloccato
        // il device D3D11 condiviso con l'encoder (misurato: l'encoder scendeva da 60 a ~10 fps con la cattura in parallelo).
        long deadline = Environment.TickCount64 + timeoutMs;
        var hr = _duplication!.AcquireNextFrame(0, out OutduplFrameInfo info, out IDXGIResource? resource);
        while (hr.Failure && hr.Code == Vortice.DXGI.ResultCode.WaitTimeout.Code && Environment.TickCount64 < deadline)
        {
            resource?.Dispose();
            Thread.Sleep(1);
            hr = _duplication.AcquireNextFrame(0, out info, out resource);
        }

        if (hr.Failure)
        {
            resource?.Dispose();
            if (hr.Code == Vortice.DXGI.ResultCode.WaitTimeout.Code) return false;      // desktop fermo
            if (hr.Code == Vortice.DXGI.ResultCode.DeviceRemoved.Code || hr.Code == Vortice.DXGI.ResultCode.DeviceReset.Code)
            {
                // La GPU e stata persa o resettata (TDR, aggiornamento driver, riattivazione dopo sospensione su portatile ibrido):
                // questo device D3D11 e morto per sempre, ricrearne la duplicazione non serve. Si segnala e chi ci usa ricostruisce la pipeline.
                DeviceLost = true;
                return false;
            }
            if (hr.Code == Vortice.DXGI.ResultCode.AccessLost.Code ||
                hr.Code == Vortice.DXGI.ResultCode.AccessDenied.Code ||
                hr.Code == Vortice.DXGI.ResultCode.SessionDisconnected.Code)
            {
                _duplication.Dispose();
                _duplication = null;                // si ricrea al prossimo giro
                _lastRecreateAttemptTicks = 0;
                return false;
            }
            hr.CheckError();
            return false;
        }

        try
        {
            // LastPresentTime == 0: e cambiato solo il puntatore, l'immagine no.
            if (info.LastPresentTime == 0) return false;
            if (wanted != null && !wanted()) return false;     // nessuna copia GPU per i frame che verrebbero scartati

            using var texture = resource!.QueryInterface<ID3D11Texture2D>();
            long captureUs = CaptureClock.NowUs();
            _context.CopyResource(_copy!, texture);
            frame = new CapturedFrame(_copy!, captureUs);
            return true;
        }
        finally
        {
            resource!.Dispose();
            _duplication.ReleaseFrame();
        }
    }

    /// <summary>Solo per prove manuali: come se la GPU fosse stata persa.</summary>
    internal void SimulateDeviceLoss() => DeviceLost = true;

    /// <summary>Solo per prove manuali: comportati come se Windows avesse tolto l'accesso (stesso percorso di AccessLost).</summary>
    internal void SimulateAccessLoss()
    {
        _duplication?.Dispose();
        _duplication = null;
        _lastRecreateAttemptTicks = 0;
    }

    public void Dispose()
    {
        _duplication?.Dispose();
        _copy?.Dispose();
        _context?.Dispose();
        _device?.Dispose();
        _output?.Dispose();
        _adapter?.Dispose();
    }
}
