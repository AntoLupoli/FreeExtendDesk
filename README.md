# FreeExtend: an Android tablet as a second Windows monitor, over a USB-C cable

FreeExtend turns an Android tablet into an extended monitor for your Windows PC, connected with a USB-C cable and with low latency.
The PC captures a virtual monitor, encodes it to H.264 with the **hardware** encoder of the graphics card and sends it to the tablet through
`adb reverse` (a TCP tunnel over the USB cable); the app on the tablet decodes it with the hardware decoder and shows it full screen.

> **Status:** personal project, tested on **one system only**: a Windows 11 laptop with an NVIDIA RTX 3050 + integrated AMD and a Galaxy Tab S10 FE
> (Android 16). Other cards and tablets should work but have not been tested: see [Compatibility](#compatibility).

Measurements on that system (details in [`docs/measurements/stage0.md`](docs/measurements/stage0.md), written in Italian): from capture to drawing on the tablet about
30 ms at the median and 60-65 ms at the 95th percentile at 2304x1440 and 60 Hz, no dropped frames; the cable link reaches 218 Mbit/s against about 32 Mbit/s of peak use.

## Why a cable? Unlocking wired screen sharing on tablets that only offer wireless

Many tablets cannot be used as a wired monitor: their USB-C port carries data and charging but **no video** (no DisplayPort Alt Mode), so the
only screen-sharing option they offer is **wireless** (for example Windows "Project to this PC" or Miracast). Wireless sharing adds buffering and a variable
network delay, so the picture lags behind your mouse and keyboard and the experience is not interactive.

FreeExtend does not need the tablet to accept a video signal. It uses the cable only as a **data link**, which every tablet has:

1. **The PC makes a monitor out of nothing.** A virtual display driver adds an extra screen to Windows. You drag windows onto it like onto any second monitor.
2. **The PC captures and encodes it.** The screen is grabbed on the GPU (Desktop Duplication) and compressed to H.264 by the graphics card's hardware encoder
   (NVENC on NVIDIA, the equivalent on AMD and Intel), which takes a few milliseconds.
3. **The cable carries the video.** `adb reverse` opens a TCP tunnel through the USB cable using **USB debugging**, so the tablet reaches the PC at `127.0.0.1` with no Wi-Fi in between.
4. **The tablet decodes and shows it.** The FreeExtend app decodes the stream with the tablet's hardware decoder and draws it full screen.

To unlock it on your tablet you only need to do this once:

1. On the tablet, tap **Build number** seven times to enable Developer options (the menu varies by maker; on Samsung it is **Settings > About tablet > Software information**).
2. In **Developer options**, turn on **USB debugging**.
3. Connect the tablet to the PC with a **USB-C data cable** (a charge-only cable will not work) and accept **Allow USB debugging** on the tablet.
4. Install the driver and start FreeExtend as described below. The tablet then behaves as a wired second monitor, with the full performance of the cable.

## Quick start

### 1. Requirements

- **PC:** Windows 10/11 64-bit with a graphics card that has a **hardware H.264 encoder** (NVIDIA, AMD or Intel, see below).
- **Software on the PC:** the [.NET 8 SDK](https://dotnet.microsoft.com/download), a JDK 17, and the **Android SDK** (you get it by installing Android Studio: you need `adb`,
  platform 35 and the build tools).
- **Tablet:** Android 13 or later, developer options with **USB debugging** on, USB-C data cable.

### 2. Clone and install the virtual monitor driver (once)

```powershell
git clone https://github.com/AntoLupoli/FreeExtendDesk.git
cd FreeExtendDesk
# PowerShell as administrator:
powershell -ExecutionPolicy Bypass -File .\tools\virtual-display\install.ps1
```

It downloads the [Virtual Display Driver](https://github.com/VirtualDrivers/Virtual-Display-Driver) (signed; hash and signature are verified), picks the graphics card and
installs it. Options (`-Gpu`, `-ExtraModes` for other tablets) are in [`tools/virtual-display/README.md`](tools/virtual-display/README.md).
You can also install it later from the FreeExtend window (see below).

### 3. Connect the tablet and start

Plug in the tablet and **accept "Allow USB debugging"** on it. Then open the control window:

```powershell
powershell -NoProfile -File .\Start-FreeExtend-Gui.ps1
```

The first time it builds the window (`-Rebuild` rebuilds it). The window shows what is ready and what is missing:

```
 ✔ Virtual display driver installed
 ✔ FreeExtend host program ready
 ✔ Graphics: NVIDIA GeForce RTX 3050 Laptop GPU (hardware H.264 encoder found)
 ✔ Tablet connected: SM-X520
 ✔ FreeExtend app installed on the tablet
 [ Start streaming ]
```

- Next to anything missing there is a button (**Install** for the driver or the app, **Set** for the graphics preference). The driver install asks Windows for administrator permission.
- Press **Start streaming**, then **open the FreeExtend app on the tablet**. The virtual monitor is attached to the right of the main screen; drag windows towards the right.
- Press **Stop streaming** (or close the window) to stop: the virtual monitor is detached and your windows come back to the main screen.
- **Settings > Uninstall everything** removes the driver, the graphics preference and the app on the tablet (the project folder is kept).

If you prefer the command line, `Start-FreeExtend.ps1` does the same without a window: it builds the program, installs the app on the tablet if needed, opens it and streams.
**Ctrl+C** stops it.

```powershell
powershell -NoProfile -File .\Start-FreeExtend.ps1
```

Options: `-Seconds N` (stops by itself), `-Csv file` (latency time series), `-KeepDisplay` (leave the monitor in the desktop), `-InstallApp` (rebuild and reinstall the app), `-Yes` (accept the questions).
The refresh rate is capped at 60 Hz; the tablet offers up to 90: `FreeExtend.Host.exe stream --fps 90`.

If the tablet stays disconnected for 15 seconds the monitor is detached from the desktop (windows go back to the panel) and it is attached again at the next connection.

## Compatibility

| What | Status |
|---|---|
| NVIDIA with NVENC | **Tested** (RTX 3050 Laptop): recommended, about 4-12 ms of encoding |
| AMD with H.264 encoder | Supported by the code; tested only on an integrated APU: it works but with about 30 ms of fixed encoding delay |
| Intel (Quick Sync) | Supported by the code, **never tested** |
| **CPU only / no card with a hardware encoder** | **Does not work.** The program only looks for hardware H.264 encoders and uses the GPU to convert colors: there is no software fallback. The window says so |
| Android 13+ tablets | Works with any tablet or phone that has a hardware H.264 decoder; **tested only** on a Galaxy Tab S10 FE |
| Aspect ratios other than 16:10 | Add the resolution with `install.ps1 -ExtraModes WIDTHxHEIGHT` (the driver offers three 16:10 modes by default) |
| Several tablets at once, audio, touch/pen | Not supported |

### Laptops with two graphics cards

The virtual monitor and the encoding must be on the **same card**. On a laptop with NVIDIA + an integrated card, Windows only activates the NVIDIA encoder
if it runs the program on the NVIDIA card: you need "High performance" for `FreeExtend.Host.exe` (Settings > System > Display > Graphics). The window offers it with
the **Set** button and `Start-FreeExtend.ps1` offers it too; by hand: `FreeExtend.Host.exe setup-gpu` (undo with `setup-gpu --remove`; status: `gpu-status`). The preference is bound to the **exact path**
of the executable: redo it if you move the folder. If it is missing, the program says so (`ERROR: No usable hardware H.264 encoder...`) and stops.
Always start `Start-FreeExtend-Gui.ps1`, `Start-FreeExtend.ps1` or the `.exe`, not `dotnet FreeExtend.Host.dll`.

## Troubleshooting

- "The tablet has not authorized this PC": on the tablet, accept "Allow USB debugging".
- Black screen on the tablet with the app open: stop and start again; the app reconnects by itself.
- Virtual monitor left in the desktop after an abrupt stop: it is detached at the next start; by hand: `FreeExtend.Host.exe display-test detach`, or
  Settings > Display > "Disconnect this display".
- Check the virtual monitor: `powershell -NoProfile -File .\tools\virtual-display\check.ps1`.
- Remove the driver: Settings > Uninstall everything in the window, or `tools/virtual-display/remove.ps1` (administrator).

## Security and privacy

- The host listens only on `127.0.0.1:27183` (reached by the tablet through `adb reverse`), **without authentication**: another local program could connect and see the virtual monitor.
- The driver's control pipe is open to all local users (as in the original driver).
- USB debugging gives the PC a lot of control over the tablet: turn it on only for PCs you trust.
- `setup-gpu` writes a single entry in the user's registry (`HKCU\Software\Microsoft\DirectX\UserGpuPreferences`).

## Development

```powershell
dotnet test host\FreeExtend.sln                       # tests of the PC program, the protocol and the window
cd android; .\gradlew.bat testDebugUnitTest           # tests of the Android app
```

Documentation (in Italian): [`docs/superpowers/specs/`](docs/superpowers/specs/) (design), [`docs/superpowers/plans/`](docs/superpowers/plans/) (plans and decision ledger),
[`docs/device-profile.md`](docs/device-profile.md) (hardware and encoder choices), [`docs/measurements/stage0.md`](docs/measurements/stage0.md) (measurements).

## License and third-party components

This project is under the **MIT** license ([`LICENSE`](LICENSE)). It uses, without redistributing them in the repository (they are downloaded at build or install time):
[Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows) (MIT), System.Drawing.Common (MIT), the Gradle Wrapper (Apache-2.0), JUnit (EPL-2.0, tests only) and the
[Virtual Display Driver](https://github.com/VirtualDrivers/Virtual-Display-Driver) (MIT, signed by SignPath Foundation).
