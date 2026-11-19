# FreeExtend

**Use an Android tablet as a wired second monitor for Windows, over USB-C, with low latency.**

Many tablets can't take a video signal over USB-C, so the only screen sharing they offer is wireless, which lags. FreeExtend uses the cable as a
*data* link instead (USB debugging + `adb reverse`), so it works on any tablet: Windows gets a virtual monitor, the GPU encodes it to H.264, the cable
carries it, and the app on the tablet decodes and shows it full screen. Measured: about 30 ms from capture to screen (Galaxy Tab S10 FE, 2304x1440 at 60 Hz).

## Set up

1. **Tablet:** turn on Developer options and **USB debugging**, plug it in with a USB-C *data* cable, accept the prompt.
2. **PC:** install the [.NET 8 SDK](https://dotnet.microsoft.com/download), a JDK 17 and Android Studio (for `adb`). Then:

```powershell
git clone https://github.com/AntoLupoli/FreeExtendDesk.git
cd FreeExtendDesk
powershell -ExecutionPolicy Bypass -File .\tools\virtual-display\install.ps1     # once, as administrator: installs the virtual monitor
powershell -NoProfile -File .\Start-FreeExtend-Gui.ps1                           # opens the control window
```

In the window press **Start streaming**, open the FreeExtend app on the tablet, and drag windows to the right. Anything missing (driver, app on the tablet) has an **Install** button.
Prefer a terminal? `.\Start-FreeExtend.ps1` does the same; Ctrl+C stops it.

## Good to know

- Needs a GPU with a **hardware H.264 encoder** (NVIDIA, AMD or Intel). No CPU-only mode.
- Tested only on an RTX 3050 laptop and a Galaxy Tab S10 FE: other hardware should work but is unverified.
- Laptop with two GPUs? Click **Set** next to the GPU row (or run `FreeExtend.Host.exe setup-gpu`).
- Remove everything: window > **Settings > Uninstall everything**.

More (troubleshooting, dual-GPU, security, development): [`docs/details.md`](docs/details.md). License: MIT.
