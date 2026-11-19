# Virtual monitor: Virtual Display Driver

FreeExtend uses a Windows virtual monitor provided by the [Virtual Display Driver](https://github.com/VirtualDrivers/Virtual-Display-Driver)
(a user-mode IDD driver). The driver is not part of this repository: `get-driver.ps1` downloads it from the official release.

## What is used

| Item | Value |
|------|-------|
| Project | Virtual Display Driver (IDD), https://github.com/VirtualDrivers/Virtual-Display-Driver |
| Version | 25.7.23 (package `VDD.Control.25.7.23.zip`, about 68 MB) |
| License | MIT |
| Signature | Signed with a SignPath Foundation certificate: `MttVDD.dll` and `mttvdd.cat` report `Valid` with `Get-AuthenticodeSignature` |
| SHA256 of the zip | `A701F2272E9FCF382849B24F913C6DD07597B3B1116525F2E90182F019609154` (same as the winget manifest) |
| Test signing / Secure Boot | **Not needed** and must not be touched. |

The package also contains a virtual audio driver (`SignedDrivers\x86\VAD`): it is **not installed**. The `x86` folder actually holds the
64-bit driver (the INF declares `NTamd64`). The graphical control program `VDD Control.exe` is not used: on one tested PC it did not start any
installation, so the script does the same steps in a visible way.

## Installation

PowerShell **as administrator** (right-click > Run as administrator), from the project folder:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\virtual-display\install.ps1
```

The script, in order:

1. downloads the driver if it is missing (`get-driver.ps1`): it accepts the zip only if the SHA256 hash matches and the signatures are valid, otherwise it deletes it;
2. picks the graphics card for the virtual monitor and writes the configuration to `C:\VirtualDisplayDriver\vdd_settings.xml`;
3. installs the `Root\MttVDD` device with `devcon.exe` (Microsoft-signed) and prints the resulting screens and device.

Options:

| Option | Effect |
|--------|--------|
| `-Gpu "card name"` | Card that hosts the virtual monitor. It must be **the same one that encodes the video**. If you do not give one: the only card, or the NVIDIA one if there are several, or it asks you. The name is the one in Device Manager > Display adapters. |
| `-ExtraModes 1920x1080,2560x1440` | Extra resolutions for your tablet. By default the driver offers three, all 16:10 (2304x1440, 1920x1200, 2560x1600): a tablet with another aspect ratio would find "no suitable mode". The tablet reports its resolution and the program picks the mode with the same aspect ratio. |
| `-ConfigOnly [-OutFile file.xml]` | Only writes the configuration, without installing (no administrator needed). |

The newly created monitor shows up as "not attached" to the desktop: you do not need to do anything, FreeExtend attaches it by itself when the
tablet connects and detaches it at the end. Manual check: `powershell -NoProfile -File tools\virtual-display\check.ps1` (checks 2304x1440@60 with the 90 Hz mode offered).

## Changing the card or resolutions after installation

You can do it without reinstalling, using the driver's control pipe (`vdd-pipe.ps1`; it accepts commands from any local user):

```powershell
& .\tools\virtual-display\vdd-pipe.ps1 -Command 'SETGPU "NVIDIA GeForce RTX 3050 Laptop GPU"'   # reloads the driver
& .\tools\virtual-display\vdd-pipe.ps1 -Command 'HARDWARECURSOR false'                            # pointer drawn inside the image
```

Or edit `C:\VirtualDisplayDriver\vdd_settings.xml` and send `RELOAD_DRIVER`. Note: every reload changes the screen number
(`\\.\DISPLAY58`, 59, 60...); FreeExtend finds it again by itself.

## Removal

- The FreeExtend window: Settings > Uninstall everything (removes the driver, the graphics preference and the app on the tablet), or
- `remove.ps1` (PowerShell as administrator), or
- Device Manager > Display adapters > Virtual Display Driver > Uninstall device.
- If the screen goes black: boot in safe mode, then uninstall the device from Device Manager.

## Security

The pipe `\\.\pipe\MTTVirtualDisplayPipe` is open to all local users (this is how the upstream driver works): any program on the PC can
change its card and resolutions. That is acceptable for a personal tool; keep it in mind on a shared PC.
