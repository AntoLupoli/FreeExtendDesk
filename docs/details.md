# FreeExtend: details

## How it works

1. **The PC makes a monitor out of nothing.** A virtual display driver adds an extra screen to Windows.
2. **The PC captures and encodes it.** The screen is grabbed on the GPU (Desktop Duplication) and compressed to H.264 by the graphics card's hardware encoder
   (NVENC on NVIDIA, the equivalent on AMD and Intel), which takes a few milliseconds.
3. **The cable carries the video.** `adb reverse` opens a TCP tunnel through the USB cable using USB debugging, so the tablet reaches the PC at `127.0.0.1` with no Wi-Fi in between.
4. **The tablet decodes and shows it.** The app decodes the stream with the tablet's hardware decoder and draws it full screen.

Measurements (see [`measurements/stage0.md`](measurements/stage0.md), in Italian): about 30 ms at the median and 60-65 ms at the 95th percentile at 2304x1440 and 60 Hz,
no dropped frames; the cable link reaches 218 Mbit/s against about 32 Mbit/s of peak use.

## Enabling USB debugging

Tap **Build number** seven times to enable Developer options (the menu varies by maker; on Samsung it is Settings > About tablet > Software information), then turn on
**USB debugging** in Developer options. Use a USB-C *data* cable (a charge-only cable will not work) and accept "Allow USB debugging" on the tablet.

## Command line

`Start-FreeExtend.ps1` builds the program, installs the app on the tablet if needed, opens it and streams. Options: `-Seconds N` (stops by itself), `-Csv file` (latency time series),
`-KeepDisplay` (leave the monitor in the desktop), `-InstallApp` (rebuild and reinstall the app), `-Yes` (accept the questions).
The refresh rate is capped at 60 Hz; the tablet offers up to 90: `FreeExtend.Host.exe stream --fps 90`.
If the tablet stays disconnected for 15 seconds the monitor is detached from the desktop and attached again at the next connection.

## Compatibility

| What | Status |
|---|---|
| NVIDIA with NVENC | **Tested** (RTX 3050 Laptop): recommended, about 4-12 ms of encoding |
| AMD with H.264 encoder | Supported by the code; tested only on an integrated APU: it works but with about 30 ms of fixed encoding delay |
| Intel (Quick Sync) | Supported by the code, **never tested** |
| **CPU only / no card with a hardware encoder** | **Does not work** (no software fallback). The window says so |
| Android 13+ tablets | Any tablet or phone with a hardware H.264 decoder; **tested only** on a Galaxy Tab S10 FE |
| Aspect ratios other than 16:10 | Add the resolution with `install.ps1 -ExtraModes WIDTHxHEIGHT` (the driver offers three 16:10 modes by default) |
| Several tablets at once, audio, touch/pen | Not supported |

Driver options (`-Gpu`, `-ExtraModes`, `-ConfigOnly`) are in [`../tools/virtual-display/README.md`](../tools/virtual-display/README.md).

## Laptops with two graphics cards

The virtual monitor and the encoding must be on the **same card**. On a laptop with NVIDIA + an integrated card, Windows only activates the NVIDIA encoder
if it runs the program on the NVIDIA card: you need "High performance" for `FreeExtend.Host.exe` (Settings > System > Display > Graphics). The window offers it with
the **Set** button and `Start-FreeExtend.ps1` offers it too; by hand: `FreeExtend.Host.exe setup-gpu` (undo with `setup-gpu --remove`; status: `gpu-status`). The preference is bound to the **exact path**
of the executable: redo it if you move the folder. If it is missing, the program says so (`ERROR: No usable hardware H.264 encoder...`) and stops.
Always start the window, `Start-FreeExtend.ps1` or the `.exe`, not `dotnet FreeExtend.Host.dll`.

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

Other documentation (in Italian): [`superpowers/specs/`](superpowers/specs/) (design), [`superpowers/plans/`](superpowers/plans/) (plans and decision ledger),
[`device-profile.md`](device-profile.md) (hardware and encoder choices).

## License and third-party components

MIT ([`../LICENSE`](../LICENSE)). It uses, without redistributing them in the repository (they are downloaded at build or install time):
[Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows) (MIT), System.Drawing.Common (MIT), the Gradle Wrapper (Apache-2.0), JUnit (EPL-2.0, tests only) and the
[Virtual Display Driver](https://github.com/VirtualDrivers/Virtual-Display-Driver) (MIT, signed by SignPath Foundation).
