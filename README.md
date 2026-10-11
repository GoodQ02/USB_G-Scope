# USB_G-Scope

A lightweight, modern USB topology viewer for Windows — a fresh take on tools like Microsoft USBView and UsbTreeView, built with **WinUI 3** and **.NET 10**.

> **Status:** early development. USB enumeration works and is exposed through a command-line tool; the WinUI tree view comes next.

## Goals

- Live tree of host controllers → hubs → ports → devices, refreshed on plug/unplug
- Clear speed badges (Low / Full / High / SuperSpeed / SuperSpeed+) and a warning when a device runs below its capability
- Device details: VID/PID, strings, class, driver, power, decoded descriptors
- Friendly mapping to drive letters, COM ports, and child devices
- Read-only, no admin rights, ships as a single portable `.exe`

## Build

Requirements: Windows 10 1809+ and the .NET 10 SDK.

```powershell
dotnet build src/UsbGScope.App -p:Platform=x64
```

## Test

```powershell
dotnet test tests/UsbGScope.Core.Tests
```

## Publish a single portable EXE

```powershell
dotnet publish src/UsbGScope.App -c Release -r win-x64 -p:Platform=x64
```

The output is `src/UsbGScope.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/publish/USB_G-Scope.exe`.

> WinUI 3 single-file apps unpack their native runtime to a temporary folder on first launch. This is how the Windows App SDK supports single-file apps; see [Microsoft's docs](https://learn.microsoft.com/windows/apps/package-and-deploy/unpackage-winui-app#single-file-exe).

## Command-line tool

```powershell
dotnet run --project tools/UsbGScope.Cli -- [--all] [--verbose] [--json] [--mask-serials]
```

| Flag | Effect |
| :--- | :--- |
| `--all` | Include empty ports |
| `--verbose` | Show serial numbers, instance IDs, and driver details |
| `--json` | Emit the full snapshot as JSON |
| `--mask-serials` | Replace serial numbers, so output is safe to share |

## Layout

```
src/UsbGScope.Core/    USB enumeration (Win32 interop), descriptor parsing, topology model
src/UsbGScope.App/     WinUI 3 desktop app
tools/UsbGScope.Cli/   usbgscope command-line dump (text / JSON)
tests/UsbGScope.Core.Tests/  unit tests (real descriptor captures in Fixtures/)
```

## License

MIT — see [LICENSE](LICENSE). Third-party attributions are listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
