using System.Reflection;
using Vortice.MediaFoundation;

namespace FreeExtend.Host.Encode;

/// <summary>Elenca gli encoder H.264 hardware di Media Foundation e i formati di ingresso che accettano (comando <c>encoder-probe</c>).</summary>
internal static class EncoderProbe
{
    public static int Run()
    {
        MediaFactory.MFStartup();
        // Su un portatile ibrido la GPU NVIDIA puo essere "a riposo": un device D3D11 sull'adattatore NVIDIA la attiva
        // prima di provare l'MFT NVENC.
        using var nvDevice = CreateNvidiaDevice();
        Console.WriteLine(nvDevice != null ? "Device D3D11 su NVIDIA creato." : "Nessun adattatore NVIDIA trovato.");
        try
        {
            var output = new RegisterTypeInfo { GuidMajorType = MediaTypeGuids.Video, GuidSubtype = VideoFormatGuids.H264 };
            uint flags = (uint)(EnumFlag.EnumFlagHardware | EnumFlag.EnumFlagSortandfilter);
            using var activates = MediaFactory.MFTEnumEx(TransformCategoryGuids.VideoEncoder, flags, null, output);

            int count = 0;
            foreach (var act in activates)
            {
                count++;
                string name = act.GetString(TransformAttributeKeys.MftFriendlyNameAttribute);
                Console.WriteLine($"- {name}");
                IMFTransform mft;
                try { mft = act.ActivateObject<IMFTransform>(); }
                catch (Exception ex)
                {
                    Console.WriteLine($"    ATTIVAZIONE FALLITA: {ex.Message.Trim()}");
                    continue;
                }
                using var _ = mft;
                bool isAsync = mft.Attributes.GetUInt32(TransformAttributeKeys.TransformAsync) != 0;
                bool d3d11 = mft.Attributes.GetUInt32(TransformAttributeKeys.D3D11Aware) != 0;
                Console.WriteLine($"    asincrono: {isAsync}, D3D11-aware: {d3d11}");
                var inputs = new List<string>();
                for (int i = 0; i < 32; i++)
                {
                    try
                    {
                        using var t = mft.GetInputAvailableType(0, i);
                        inputs.Add(Describe(t.GetGUID(MediaTypeAttributeKeys.Subtype)));
                    }
                    catch { break; }
                }
                Console.WriteLine($"    ingressi prima di impostare l'uscita: {(inputs.Count == 0 ? "(nessuno: serve prima il tipo di uscita)" : string.Join(", ", inputs))}");
            }
            Console.WriteLine($"Encoder H.264 hardware trovati: {count}");
            return count > 0 ? 0 : 2;
        }
        finally
        {
            MediaFactory.MFShutdown();
        }
    }

    /// <summary>MFT_ENUM_ADAPTER_LUID: chiede a Media Foundation gli encoder legati a un preciso adattatore grafico.</summary>
    private static readonly Guid MftEnumAdapterLuid = new("1d39518c-e220-4da8-a07f-ba172552d6b1");

    /// <summary>Prova a elencare e attivare gli encoder H.264 hardware di un adattatore scelto per LUID.</summary>
    public static int RunForAdapters()
    {
        MediaFactory.MFStartup();
        try
        {
            using var factory = Vortice.DXGI.DXGI.CreateDXGIFactory1<Vortice.DXGI.IDXGIFactory1>();
            for (uint a = 0; factory.EnumAdapters1(a, out Vortice.DXGI.IDXGIAdapter1? adapter).Success; a++)
            {
                using (adapter)
                {
                    var desc = adapter!.Description1;
                    ulong luid = ((ulong)(uint)desc.Luid.HighPart << 32) | desc.Luid.LowPart;
                    Console.WriteLine($"Adattatore {desc.Description} (LUID {luid:X}):");

                    using var attrs = MediaFactory.MFCreateAttributes(1);
                    attrs.Set(MftEnumAdapterLuid, luid);

                    var output = new RegisterTypeInfo { GuidMajorType = MediaTypeGuids.Video, GuidSubtype = VideoFormatGuids.H264 };
                    uint flags = (uint)(EnumFlag.EnumFlagHardware | EnumFlag.EnumFlagSortandfilter);
                    MediaFactory.MFTEnum2(TransformCategoryGuids.VideoEncoder, flags, null, output, attrs, out IntPtr array, out uint n);
                    Console.WriteLine($"  MFT trovati per questo adattatore: {n}");
                    for (int i = 0; i < n; i++)
                    {
                        var act = new IMFActivate(System.Runtime.InteropServices.Marshal.ReadIntPtr(array, i * IntPtr.Size));
                        string name = act.GetString(TransformAttributeKeys.MftFriendlyNameAttribute);
                        try
                        {
                            using var mft = act.ActivateObject<IMFTransform>();
                            Console.WriteLine($"  - {name}: ATTIVATO");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"  - {name}: attivazione fallita ({ex.Message.Trim()})");
                        }
                    }
                    if (array != IntPtr.Zero) System.Runtime.InteropServices.Marshal.FreeCoTaskMem(array);
                }
            }
            return 0;
        }
        finally
        {
            MediaFactory.MFShutdown();
        }
    }

    private static Vortice.Direct3D11.ID3D11Device? CreateNvidiaDevice()
    {
        using var factory = Vortice.DXGI.DXGI.CreateDXGIFactory1<Vortice.DXGI.IDXGIFactory1>();
        for (uint a = 0; factory.EnumAdapters1(a, out Vortice.DXGI.IDXGIAdapter1? adapter).Success; a++)
        {
            using (adapter)
            {
                if (!adapter!.Description1.Description.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) continue;
                Vortice.Direct3D11.D3D11.D3D11CreateDevice(adapter, Vortice.Direct3D.DriverType.Unknown,
                    Vortice.Direct3D11.DeviceCreationFlags.BgraSupport | Vortice.Direct3D11.DeviceCreationFlags.VideoSupport,
                    null, out Vortice.Direct3D11.ID3D11Device? device).CheckError();
                return device;
            }
        }
        return null;
    }

    private static string Describe(Guid sub)
    {
        foreach (var f in typeof(VideoFormatGuids).GetFields(BindingFlags.Public | BindingFlags.Static))
            if (f.FieldType == typeof(Guid) && (Guid)f.GetValue(null)! == sub) return f.Name;
        return sub.ToString();
    }
}
