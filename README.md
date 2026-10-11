# USB_G-Scope

A lightweight, modern USB topology viewer for Windows — a fresh take on tools like Microsoft USBView and UsbTreeView, built with **WinUI 3** and **.NET 10**.

> **Status:** early development (scaffold). Device enumeration lands in milestone 1.

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

## Publish a single portable EXE

```powershell
dotnet publish src/UsbGScope.App -c Release -r win-x64 -p:Platform=x64
```

The output is `src/UsbGScope.App/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/publish/USB_G-Scope.exe`.

> WinUI 3 single-file apps unpack their native runtime to a temporary folder on first launch. This is how the Windows App SDK supports single-file apps; see [Microsoft's docs](https://learn.microsoft.com/windows/apps/package-and-deploy/unpackage-winui-app#single-file-exe).

## Layout

```
src/UsbGScope.Core/   USB enumeration (Win32 interop) and the topology model
src/UsbGScope.App/    WinUI 3 desktop app
```

## License

MIT — see [LICENSE](LICENSE). Third-party attributions are listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
