using System.Runtime.InteropServices;
using FreeExtend.Host.Capture;
using Vortice.Direct3D11;
using Vortice.DXGI;
using SharpGen.Runtime;
using Vortice.MediaFoundation;

namespace FreeExtend.Host.Encode;

/// <summary>
/// Encoder H.264 hardware via Media Foundation (MFT asincrono). Pipeline tutta su GPU: il frame BGRA catturato viene convertito
/// in NV12 con il video processor D3D11 (BT.709, 16-235) e dato all'MFT. Impostazioni a bassa latenza: CBR, nessun B-frame,
/// LowLatencyMode, GOP lungo con keyframe a richiesta. Le texture di ingresso vivono sullo stesso adattatore del device
/// della sorgente di cattura: niente copie tra schede.
/// </summary>
public sealed class MfH264Encoder : IVideoEncoder
{
    private const int Nv12PoolSize = 4;
    private const int EventNeedInput = 601;   // METransformNeedInput
    private const int EventHaveOutput = 602;  // METransformHaveOutput

    private readonly EncoderConfig _config;
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11VideoDevice _videoDevice;
    private readonly ID3D11VideoContext _videoContext;
    private readonly ID3D11VideoProcessorEnumerator _enumerator;
    private readonly ID3D11VideoProcessor _processor;
    private readonly ID3D11Texture2D[] _nv12 = new ID3D11Texture2D[Nv12PoolSize];
    private readonly ID3D11VideoProcessorOutputView[] _outViews = new ID3D11VideoProcessorOutputView[Nv12PoolSize];
    private readonly IMFSample[] _samples = new IMFSample[Nv12PoolSize];
    private ID3D11Texture2D? _lastInputTexture;
    private ID3D11VideoProcessorInputView? _inView;

    private readonly IMFDXGIDeviceManager _deviceManager;
    private readonly IMFTransform _mft;
    private readonly IMFMediaEventGenerator _events;
    private readonly ICodecApi? _codecApi;
    private readonly IMFActivate _activate;
    private readonly IMFActivateCollection _activates;
    private readonly object _mftLock = new();
    private readonly Thread _eventThread;
    private readonly long _frameIntervalUs;

    private int _needInput;
    private int _poolIndex;
    private long _lastAcceptedUs = long.MinValue / 2;
    private volatile bool _stopping;
    private byte[] _parameterSets = Array.Empty<byte>();

    public byte[] ParameterSets => _parameterSets;
    public string EncoderName { get; }

    /// <summary>Dopo Dispose: il thread degli eventi si e fermato (verifica dello spegnimento corretto dell'MFT).</summary>
    public bool EventThreadStopped { get; private set; }
    public IReadOnlyList<string> CodecApiLog => _codecApiLog;
    private readonly List<string> _codecApiLog = new();

    /// <summary>Frame non codificati perche l'encoder non aveva richiesto ingresso (occupato).</summary>
    public long DroppedBusy => Interlocked.Read(ref _droppedBusy);
    /// <summary>Frame saltati per rispettare <see cref="EncoderConfig.Fps"/> (la cattura arriva anche a 120 fps).</summary>
    public long SkippedForRate => Interlocked.Read(ref _skippedForRate);
    private long _droppedBusy, _skippedForRate;

    // Diagnostica: dove va il tempo?  prepare = cattura -> ProcessInput (conversione colore inclusa); encode = ProcessInput -> pacchetto.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<long, long> _inputAtUs = new();
    private double _prepareUsSum, _encodeUsSum; private long _timedFrames;
    public (double PrepareMs, double EncodeMs, long Frames) Timing
    {
        get { lock (_timingLock) return _timedFrames == 0 ? (0, 0, 0) : (_prepareUsSum / _timedFrames / 1000.0, _encodeUsSum / _timedFrames / 1000.0, _timedFrames); }
    }
    private readonly object _timingLock = new();
    private static readonly bool SyncBlt = Environment.GetEnvironmentVariable("FX_SYNC_BLT") == "1";
    private double _bltUsSum; private long _bltCount;
    private ID3D11Query? _syncQuery;
    public double BltGpuMs { get { lock (_timingLock) return _bltCount == 0 ? 0 : _bltUsSum / _bltCount / 1000.0; } }

    public event Action<EncodedPacket>? PacketReady;

    public MfH264Encoder(ID3D11Device device, EncoderConfig config)
    {
        _device = device;
        _config = config;
        _context = device.ImmediateContext;
        _frameIntervalUs = 1_000_000L / config.Fps;

        // MF e il video processor usano il device da piu thread.
        using (var mt = device.QueryInterface<ID3D11Multithread>()) mt.SetMultithreadProtected(true);
        MediaFactory.MFStartup();

        // --- conversione BGRA -> NV12 sulla GPU ---
        _videoDevice = device.QueryInterface<ID3D11VideoDevice>();
        if (SyncBlt) _syncQuery = device.CreateQuery(new QueryDescription(QueryType.Event, QueryFlags.None));
        _videoContext = _context.QueryInterface<ID3D11VideoContext>();
        var content = new VideoProcessorContentDescription
        {
            InputFrameFormat = VideoFrameFormat.Progressive,
            InputFrameRate = new Rational((uint)config.Fps, 1),
            InputWidth = (uint)(config.SourceWidth ?? config.Width),
            InputHeight = (uint)(config.SourceHeight ?? config.Height),
            OutputFrameRate = new Rational((uint)config.Fps, 1),
            OutputWidth = (uint)config.Width,
            OutputHeight = (uint)config.Height,
            Usage = VideoUsage.OptimalSpeed,
        };
        _enumerator = _videoDevice.CreateVideoProcessorEnumerator(content);
        _processor = _videoDevice.CreateVideoProcessor(_enumerator, 0);
        // Ingresso RGB pieno (desktop), uscita YCbCr BT.709 16-235: stessi valori dichiarati nel flusso H.264.
        _videoContext.VideoProcessorSetStreamColorSpace(_processor, 0, new VideoProcessorColorSpace { RGB_Range = 0, YCbCr_Matrix = 1, Nominal_Range = 1 });
        _videoContext.VideoProcessorSetOutputColorSpace(_processor, new VideoProcessorColorSpace { RGB_Range = 0, YCbCr_Matrix = 1, Nominal_Range = 1 });
        _videoContext.VideoProcessorSetStreamFrameFormat(_processor, 0, VideoFrameFormat.Progressive);

        _deviceManager = MediaFactory.MFCreateDXGIDeviceManager();
        _deviceManager.ResetDevice(device);

        for (int i = 0; i < Nv12PoolSize; i++)
        {
            _nv12[i] = device.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)config.Width,
                Height = (uint)config.Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.NV12,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget,
            });
            _outViews[i] = _videoDevice.CreateVideoProcessorOutputView(_nv12[i], _enumerator,
                new VideoProcessorOutputViewDescription { ViewDimension = VideoProcessorOutputViewDimension.Texture2D });

            using var buffer = MediaFactory.MFCreateDXGISurfaceBuffer(typeof(ID3D11Texture2D).GUID, _nv12[i], 0, false);
            buffer.CurrentLength = config.Width * config.Height * 3 / 2;
            _samples[i] = MediaFactory.MFCreateSample();
            _samples[i].AddBuffer(buffer);
        }

        // --- MFT hardware ---
        (_mft, _activate, _activates, EncoderName) = ActivateHardwareEncoder(config.MftIndex, AdapterNameOf(device));
        _mft.Attributes.Set(TransformAttributeKeys.TransformAsyncUnlock, 1u);
        _mft.ProcessMessage(TMessageType.MessageSetD3DManager, (UIntPtr)(ulong)(long)_deviceManager.NativePointer);

        // ICodecAPI prima dei tipi di media: la modalita low-latency va impostata prima della negoziazione dei formati.
        _codecApi = TryGetCodecApi(_mft);
        ConfigureCodecApi();
        ConfigureMediaTypes();

        _events = _mft.QueryInterface<IMFMediaEventGenerator>();
        _mft.ProcessMessage(TMessageType.MessageNotifyBeginStreaming, UIntPtr.Zero);
        _mft.ProcessMessage(TMessageType.MessageNotifyStartOfStream, UIntPtr.Zero);

        _eventThread = new Thread(EventLoop) { IsBackground = true, Name = "mf-encoder-events", Priority = ThreadPriority.AboveNormal };
        _eventThread.Start();
    }

    private static string AdapterNameOf(ID3D11Device device)
    {
        using var dxgi = device.QueryInterface<IDXGIDevice>();
        using var adapter = dxgi.GetAdapter();
        using var adapter1 = adapter.QueryInterface<IDXGIAdapter1>();
        return adapter1.Description1.Description;
    }

    private static (IMFTransform Mft, IMFActivate Activate, IMFActivateCollection Collection, string Name) ActivateHardwareEncoder(int skip, string adapterName)
    {
        var output = new RegisterTypeInfo { GuidMajorType = MediaTypeGuids.Video, GuidSubtype = VideoFormatGuids.H264 };
        uint flags = (uint)(EnumFlag.EnumFlagHardware | EnumFlag.EnumFlagSortandfilter);
        // La collezione NON si libera qui: l'IMFActivate scelto serve a spegnere l'MFT (ShutdownObject) alla Dispose.
        var activates = MediaFactory.MFTEnumEx(TransformCategoryGuids.VideoEncoder, flags, null, output);
        var errors = new List<string>();
        int seen = 0;
        foreach (var act in activates)
        {
            string name = act.GetString(TransformAttributeKeys.MftFriendlyNameAttribute);
            if (!EncoderVendor.IsCompatible(adapterName, name))
            {
                errors.Add($"{name}: e di un altro produttore rispetto alla scheda del monitor ({adapterName})");
                continue;                                 // con un device di un altra scheda fallirebbe con un E_FAIL generico
            }
            if (seen++ < skip) continue;                  // MftIndex: per provare gli altri encoder elencati
            try
            {
                return (act.ActivateObject<IMFTransform>(), act, activates, name);
            }
            catch (Exception ex)
            {
                errors.Add($"{name}: {ex.Message.Trim()}"); // es. l'MFT NVIDIA su questo portatile ibrido
            }
        }
        activates.Dispose();
        throw new EncoderUnavailableException(adapterName, errors);
    }

    private void ConfigureMediaTypes()
    {
        ulong size = ((ulong)(uint)_config.Width << 32) | (uint)_config.Height;
        ulong rate = ((ulong)(uint)_config.Fps << 32) | 1u;
        ulong aspect = (1UL << 32) | 1u;

        // Prima l'uscita (H.264), poi l'ingresso: e l'ordine che gli MFT hardware richiedono.
        using (var outType = MediaFactory.MFCreateMediaType())
        {
            outType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            outType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.H264);
            outType.Set(MediaTypeAttributeKeys.AvgBitrate, (uint)(_config.BitrateKbps * 1000));
            outType.Set(MediaTypeAttributeKeys.FrameSize, size);
            outType.Set(MediaTypeAttributeKeys.FrameRate, rate);
            outType.Set(MediaTypeAttributeKeys.PixelAspectRatio, aspect);
            outType.Set(MediaTypeAttributeKeys.InterlaceMode, 2u);       // MFVideoInterlace_Progressive
            outType.Set(MediaTypeAttributeKeys.Mpeg2Profile, 100u);      // eAVEncH264VProfile_High
            outType.Set(MediaTypeAttributeKeys.VideoPrimaries, 2u);      // MFVideoPrimaries_BT709
            outType.Set(MediaTypeAttributeKeys.TransferFunction, 5u);    // MFVideoTransFunc_709
            outType.Set(MediaTypeAttributeKeys.YuvMatrix, 1u);           // MFVideoTransferMatrix_BT709
            outType.Set(MediaTypeAttributeKeys.VideoNominalRange, 2u);   // MFNominalRange_16_235
            _mft.SetOutputType(0, outType, 0);
            for (int i = 0; i < 16; i++)
            {
                try { using var t = _mft.GetInputAvailableType(0, i); _codecApiLog.Add("ingresso accettato: " + SubtypeName(t.GetGUID(MediaTypeAttributeKeys.Subtype))); }
                catch { break; }
            }
        }

        using var inType = MediaFactory.MFCreateMediaType();
        inType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        inType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.NV12);
        inType.Set(MediaTypeAttributeKeys.FrameSize, size);
        inType.Set(MediaTypeAttributeKeys.FrameRate, rate);
        inType.Set(MediaTypeAttributeKeys.PixelAspectRatio, aspect);
        inType.Set(MediaTypeAttributeKeys.InterlaceMode, 2u);
        inType.Set(MediaTypeAttributeKeys.VideoPrimaries, 2u);
        inType.Set(MediaTypeAttributeKeys.TransferFunction, 5u);
        inType.Set(MediaTypeAttributeKeys.YuvMatrix, 1u);
        inType.Set(MediaTypeAttributeKeys.VideoNominalRange, 2u);
        _mft.SetInputType(0, inType, 0);
    }

    private static string SubtypeName(Guid g)
    {
        foreach (var f in typeof(VideoFormatGuids).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            if (f.FieldType == typeof(Guid) && (Guid)f.GetValue(null)! == g) return f.Name;
        return g.ToString();
    }

    private static ICodecApi? TryGetCodecApi(IMFTransform mft)
    {
        var iid = typeof(ICodecApi).GUID;
        int hr = Marshal.QueryInterface(mft.NativePointer, in iid, out IntPtr p);
        if (hr < 0 || p == IntPtr.Zero) return null;
        try { return (ICodecApi)Marshal.GetObjectForIUnknown(p); }
        finally { Marshal.Release(p); }
    }

    private void ConfigureCodecApi()
    {
        if (_codecApi == null) { _codecApiLog.Add("ICodecAPI non disponibile su questo MFT"); return; }
        SetCodec("LowLatencyMode", CodecApiKeys.LowLatencyMode, true);
        SetCodec("CommonLowLatency", CodecApiKeys.CommonLowLatency, true);
        SetCodec("CommonRealTime", CodecApiKeys.CommonRealTime, true);
        SetCodec("RateControlMode=CBR", CodecApiKeys.RateControlMode, CodecApiKeys.RateControlCbr);
        SetCodec("MeanBitRate", CodecApiKeys.MeanBitRate, (uint)(_config.BitrateKbps * 1000));
        SetCodec("DefaultBPictureCount=0", CodecApiKeys.DefaultBPictureCount, 0u);
        SetCodec("GopSize", CodecApiKeys.GopSize, (uint)(_config.Fps * 10));
        if (_config.QualityVsSpeed is int q) SetCodec($"QualityVsSpeed={q}", CodecApiKeys.QualityVsSpeed, (uint)q);
    }

    private void SetCodec(string label, Guid key, object value)
    {
        var k = key;
        object v = value;
        int hr = _codecApi!.SetValue(ref k, ref v);
        _codecApiLog.Add($"{label}: {(hr >= 0 ? "ok" : $"HRESULT 0x{hr:X8}")}");
    }

    public bool WantsFrame() =>
        CaptureClock.NowUs() - _lastAcceptedUs >= _frameIntervalUs * 9 / 10 && Volatile.Read(ref _needInput) > 0;

    public bool Encode(CapturedFrame frame, bool forceKeyframe)
    {
        if (frame.CaptureUs - _lastAcceptedUs < _frameIntervalUs * 9 / 10)
        {
            Interlocked.Increment(ref _skippedForRate);
            return false;
        }
        if (Volatile.Read(ref _needInput) <= 0)
        {
            Interlocked.Increment(ref _droppedBusy);
            return false;
        }

        if (!ReferenceEquals(frame.Texture, _lastInputTexture))
        {
            _inView?.Dispose();
            _inView = _videoDevice.CreateVideoProcessorInputView(frame.Texture, _enumerator,
                new VideoProcessorInputViewDescription { ViewDimension = VideoProcessorInputViewDimension.Texture2D });
            _lastInputTexture = frame.Texture;
        }

        int idx = _poolIndex++ % Nv12PoolSize;
        var stream = new VideoProcessorStream { Enable = true, InputSurface = _inView };
        long bltStart = CaptureClock.NowUs();
        _videoContext.VideoProcessorBlt(_processor, _outViews[idx], 0, new[] { stream }).CheckError();
        if (SyncBlt)
        {
            _context.End(_syncQuery!);
            _context.Flush();
            while (!_context.GetData(_syncQuery!, out RawBool done) || !done) Thread.SpinWait(50);
            lock (_timingLock) { _bltUsSum += CaptureClock.NowUs() - bltStart; _bltCount++; }
        }

        var sample = _samples[idx];
        sample.SampleTime = frame.CaptureUs * 10;                 // unita da 100 ns
        sample.SampleDuration = _frameIntervalUs * 10;

        lock (_mftLock)
        {
            if (_stopping) return false;                            // chiusura in corso: l'MFT sta per essere spento
            if (forceKeyframe && _codecApi != null) SetCodec("ForceKeyFrame", CodecApiKeys.ForceKeyFrame, 1u);
            _mft.ProcessInput(0, sample, 0);
            long inputUs = CaptureClock.NowUs();
            _inputAtUs[frame.CaptureUs] = inputUs;
            lock (_timingLock) _prepareUsSum += inputUs - frame.CaptureUs;
            Interlocked.Decrement(ref _needInput);
        }
        _lastAcceptedUs = frame.CaptureUs;
        return true;
    }

    private void EventLoop()
    {
        while (!_stopping)
        {
            IMFMediaEvent ev;
            try { ev = _events.GetEvent(0); }
            catch { break; }                                       // MFT chiuso
            using (ev)
            {
                int type = (int)ev.EventType;
                if (type == EventNeedInput) Interlocked.Increment(ref _needInput);
                else if (type == EventHaveOutput) DrainOutput();
            }
        }
    }

    private void DrainOutput()
    {
        var buffer = new OutputDataBuffer { StreamID = 0 };
        Result hr;
        lock (_mftLock)
        {
            if (_stopping) return;                                  // chiusura in corso: l'MFT sta per essere spento
            hr = _mft.ProcessOutput(ProcessOutputFlags.None, 1, ref buffer, out _);
        }
        if (hr.Failure || buffer.Sample == null) return;

        using var sample = buffer.Sample;
        long captureUs = sample.SampleTime / 10;
        using var contiguous = sample.ConvertToContiguousBuffer();
        contiguous.Lock(out IntPtr ptr, out _, out int length);
        byte[] data = new byte[length];
        try { Marshal.Copy(ptr, data, 0, length); }
        finally { contiguous.Unlock(); }

        if (_inputAtUs.TryRemove(captureUs, out long inAt))
            lock (_timingLock) { _encodeUsSum += CaptureClock.NowUs() - inAt; _timedFrames++; }

        bool keyframe = NalInspector.ContainsKeyframe(data);
        if (keyframe)
        {
            var ps = NalInspector.ExtractParameterSets(data);
            if (ps.Length > 0) _parameterSets = ps;
        }
        PacketReady?.Invoke(new EncodedPacket(data, keyframe, captureUs));
    }

    public void Dispose()
    {
        _stopping = true;
        try { lock (_mftLock) { _mft.ProcessMessage(TMessageType.MessageNotifyEndOfStream, UIntPtr.Zero); _mft.ProcessMessage(TMessageType.MessageCommandFlush, UIntPtr.Zero); } } catch { }

        // Un MFT asincrono si ferma solo con ShutdownObject: GetEvent(0) e bloccante e liberare gli oggetti non lo sveglia.
        try { _activate.ShutdownObject(); } catch { }
        EventThreadStopped = _eventThread.Join(2000);
        if (!EventThreadStopped)
        {
            // Il thread e ancora dentro l'MFT: liberare l'MFT sotto i suoi piedi potrebbe far crashare il processo. Meglio perdere
            // qualche oggetto COM fino alla fine del processo.
            Console.Error.WriteLine("Encoder: il thread degli eventi non si e fermato; l'MFT non viene liberato.");
            return;
        }
        try { _events.Dispose(); } catch { }
        foreach (var s in _samples) s?.Dispose();
        foreach (var v in _outViews) v?.Dispose();
        foreach (var t in _nv12) t?.Dispose();
        _inView?.Dispose();
        _processor.Dispose();
        _enumerator.Dispose();
        _videoContext.Dispose();
        _videoDevice.Dispose();
        if (_codecApi != null) Marshal.ReleaseComObject(_codecApi);    // il riferimento COM tiene vivo l'MFT (e la sua sessione NVENC)
        _mft.Dispose();
        _activate.Dispose();
        _activates.Dispose();
        _deviceManager.Dispose();
        MediaFactory.MFShutdown();
    }
}
