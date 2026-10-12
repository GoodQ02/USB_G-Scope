# USB_G-Scope

A lightweight, modern USB topology viewer for Windows — a fresh take on tools like Microsoft USBView and UsbTreeView, built with **WinUI 3** and **.NET 10**.

> **Status:** v0.1.0 functional release. The app is a complete, single-file, standalone USB explorer with real-time hot-plug updates, device resource mapping (drive letters, COM ports, Windows child nodes), multi-term search (Ctrl+F), an issues filter, dark/light themes, and JSON export (Ctrl+S).

## Features

- **Topology tree:** Host Controllers → Root Hubs → Hubs → Ports → Devices.
- **Speed & power:** Bus speed badges (1.5M, 12M, 480M, 5G, 10G+), max power draw, and warnings when a SuperSpeed-capable device is throttled by a USB 2.0 port or cable.
- **Resource mapping:** Directly displays mounted drive letters (e.g. `E:`), COM ports (`COM3`), and Windows PnP child devices.
- **Hot-plug watcher:** Automatic background debounced refresh on device arrival/removal with non-intrusive connect/disconnect notifications.
- **Descriptor decoding:** Full device and configuration descriptor breakdown with endpoint attributes and hex viewer.
- **Search & filter:** Multi-word search across names, VID:PID, drive letters, and ports; instant issues toggle for devices reporting Windows errors or speed downgrades.
- **Device nicknames:** Assign custom friendly names (F2, right-click, or details header) that persist across app launches in local JSON. Never touches hardware firmware or the Windows Registry (strictly read-only and zero-admin). Automatically follows devices with unique serial numbers across different USB ports.
- **Privacy & export:** Full JSON export (Ctrl+S) with one-click serial masking.
- **Standalone:** Ships as a self-contained, single-file executable requiring no installation or admin elevation.

## Build

Requirements: Windows 10 1809+ and the .NET 10 SDK.

```powershell
dotnet build src/UsbGScope.App -p:Platform=x64
```

## Test

```powershell
dotnet test tests/UsbGScope.Core.Tests
```

## Publish single-file executables

### GUI App (`USB_G-Scope.exe`)

```powershell
dotnet publish src/UsbGScope.App -c Release -r win-x64 -p:Platform=x64
```

Output: `src/UsbGScope.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/publish/USB_G-Scope.exe`
- **Binary size:** ~90 MB (completely self-contained, zero runtime dependencies)
- **Startup time:** ~1.2s cold start, < 500ms warm launch

> WinUI 3 single-file apps unpack their native runtime to a temporary folder on first launch. This is how the Windows App SDK supports single-file apps; see [Microsoft's docs](https://learn.microsoft.com/windows/apps/package-and-deploy/unpackage-winui-app#single-file-exe).

### Command-Line Tool (`usbgscope.exe`)

```powershell
dotnet publish tools/UsbGScope.Cli -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Output: `tools/UsbGScope.Cli/bin/Release/net10.0-windows/win-x64/publish/usbgscope.exe`

```powershell
usbgscope [--all] [--verbose] [--json] [--mask-serials]
```

| Flag | Effect |
| :--- | :--- |
| `--all` | Include empty ports |
| `--verbose` | Show serial numbers, instance IDs, driver details, and child devices |
| `--json` | Emit the full snapshot as JSON |
| `--mask-serials` | Replace serial numbers in descriptors and child IDs (safe for sharing) |

## Layout

```
src/UsbGScope.Core/          USB enumeration (Win32 IOCTL), descriptor parsing, topology model
src/UsbGScope.App/           WinUI 3 desktop application
tools/UsbGScope.Cli/         usbgscope command-line tool (text / JSON)
tests/UsbGScope.Core.Tests/  85 xUnit tests with real device descriptor fixtures
```

## License

MIT — see [LICENSE](LICENSE). Third-party attributions are listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
