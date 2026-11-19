using System.Runtime.InteropServices;

namespace FreeExtend.Host.Encode;

/// <summary>
/// ICodecAPI di Windows (strmif.h), non esposta da Vortice. Ordine dei metodi preso da strmif.h: serve fino a SetValue,
/// ma tutti i metodi sono dichiarati per mantenere corretta la vtable.
/// </summary>
[ComImport, Guid("901db4c7-31ce-41a2-85dc-8fa0bf41b8da"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ICodecApi
{
    [PreserveSig] int IsSupported(ref Guid api);
    [PreserveSig] int IsModifiable(ref Guid api);
    [PreserveSig] int GetParameterRange(ref Guid api, out object valueMin, out object valueMax, out object steppingDelta);
    [PreserveSig] int GetParameterValues(ref Guid api, out IntPtr values, out uint valuesCount);
    [PreserveSig] int GetDefaultValue(ref Guid api, out object value);
    [PreserveSig] int GetValue(ref Guid api, out object value);
    [PreserveSig] int SetValue(ref Guid api, [In, MarshalAs(UnmanagedType.Struct)] ref object value);
    [PreserveSig] int RegisterForEvent(ref Guid api, IntPtr userData);
    [PreserveSig] int UnregisterForEvent(ref Guid api);
    [PreserveSig] int SetAllDefaults();
    [PreserveSig] int SetValueWithNotify(ref Guid api, [In, MarshalAs(UnmanagedType.Struct)] ref object value, out IntPtr changedParam, out uint changedParamCount);
    [PreserveSig] int SetAllDefaultsWithNotify(out IntPtr changedParam, out uint changedParamCount);
    [PreserveSig] int GetAllSettings(IntPtr stream);
    [PreserveSig] int SetAllSettings(IntPtr stream);
    [PreserveSig] int SetAllSettingsWithNotify(IntPtr stream, out IntPtr changedParam, out uint changedParamCount);
}

/// <summary>GUID di codecapi.h (verificati sull'header mingw-w64) e valori usati dall'encoder.</summary>
internal static class CodecApiKeys
{
    public static readonly Guid LowLatencyMode = new("9c27891a-ed7a-40e1-88e8-b22727a024ee");        // AVLowLatencyMode (bool)
    public static readonly Guid CommonLowLatency = new("9d3ecd55-89e8-490a-970a-0c9548d5a56e");      // AVEncCommonLowLatency (bool)
    public static readonly Guid CommonRealTime = new("143a0ff6-a131-43da-b81e-98fbb8ec378e");        // AVEncCommonRealTime (bool)
    public static readonly Guid RateControlMode = new("1c0608e9-370c-4710-8a58-cb6181c42423");       // AVEncCommonRateControlMode (uint)
    public static readonly Guid MeanBitRate = new("f7222374-2144-4815-b550-a37f8e12ee52");           // AVEncCommonMeanBitRate (uint, bit/s)
    public static readonly Guid DefaultBPictureCount = new("8d390aac-dc5c-4200-b57f-814d04babab2");  // AVEncMPVDefaultBPictureCount (uint)
    public static readonly Guid GopSize = new("95f31b26-95a4-41aa-9303-246a7fc6eef1");               // AVEncMPVGOPSize (uint)
    public static readonly Guid QualityVsSpeed = new("98332df8-03cd-476b-89fa-3f9e442dec9f");         // AVEncCommonQualityVsSpeed (uint 0..100)
    public static readonly Guid ForceKeyFrame = new("398c1b98-8353-475a-9ef2-8f265d260345");         // AVEncVideoForceKeyFrame (uint)

    public const uint RateControlCbr = 0;                // eAVEncCommonRateControlMode_CBR
}
