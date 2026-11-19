# Verifica il monitor virtuale: attaccato al desktop, 2304x1440 a 60 Hz, modi a 90 Hz disponibili.
# Uso: powershell -NoProfile -File tools\virtual-display\check.ps1   (esce con codice 1 se qualcosa non torna)
$src = @'
using System; using System.Runtime.InteropServices; using System.Collections.Generic;
public class VddCheck {
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Ansi)] public struct DISPLAY_DEVICE { public int cb; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string DeviceName; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string DeviceString; public int StateFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string DeviceID; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string DeviceKey; }
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Ansi)] public struct DEVMODE { [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string dmDeviceName; public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra; public int dmFields; public int dmPositionX, dmPositionY; public int dmDisplayOrientation, dmDisplayFixedOutput; public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string dmFormName; public short dmLogPixels; public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency; public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight; }
  [DllImport("user32.dll", CharSet=CharSet.Ansi)] public static extern bool EnumDisplayDevices(string dev, uint i, ref DISPLAY_DEVICE d, uint flags);
  [DllImport("user32.dll", CharSet=CharSet.Ansi)] public static extern bool EnumDisplaySettings(string dev, int mode, ref DEVMODE dm);
  public static int Run(int w, int h, int hz) {
    for (uint i = 0; ; i++) {
      var d = new DISPLAY_DEVICE(); d.cb = Marshal.SizeOf(d);
      if (!EnumDisplayDevices(null, i, ref d, 0)) break;
      if (d.DeviceString.IndexOf("Virtual Display Driver") < 0) continue;
      var cur = new DEVMODE(); cur.dmSize = (short)Marshal.SizeOf(cur);
      bool attached = (d.StateFlags & 1) != 0;
      EnumDisplaySettings(d.DeviceName, -1, ref cur);
      var modes = new SortedSet<string>(); var m = new DEVMODE(); m.dmSize = (short)Marshal.SizeOf(m);
      for (int k = 0; EnumDisplaySettings(d.DeviceName, k, ref m); k++) modes.Add(m.dmPelsWidth + "x" + m.dmPelsHeight + "@" + m.dmDisplayFrequency);
      Console.WriteLine("{0}: attached={1} current={2}x{3}@{4}Hz pos={5},{6}", d.DeviceName, attached, cur.dmPelsWidth, cur.dmPelsHeight, cur.dmDisplayFrequency, cur.dmPositionX, cur.dmPositionY);
      bool ok = attached && cur.dmPelsWidth == w && cur.dmPelsHeight == h && cur.dmDisplayFrequency == hz;
      bool has90 = modes.Contains(w + "x" + h + "@90");
      Console.WriteLine("modo {0}x{1}@{2}: {3} | modo @90 Hz offerto: {4}", w, h, hz, ok ? "OK" : "NON impostato", has90);
      return (ok && has90) ? 0 : 1;
    }
    Console.WriteLine("Monitor 'Virtual Display Driver' non trovato");
    return 1;
  }
}
'@
Add-Type -TypeDefinition $src
exit ([VddCheck]::Run(2304, 1440, 60))
