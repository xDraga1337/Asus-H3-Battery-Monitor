# AsusH3BatteryMonitor

A ghetto experimental open-source turbo vibe coded battery monitor for the ASUS TUF Gaming H3 Wireless headset.

The project reads battery data directly from the headset dongle using a reverse-engineered HID protocol.

Current version:

- v0.0.2 Alpha

For more in depth  details, usage, limitations, measured battery data, etc.. check out:

[AsusH3BatteryMonitor v0.0.2 Alpha README](docs/USAGE.md)

## Status

The battery reader is functional on Windows.

Battery percentage mapping is currently approximate, and charging-state handling is still experimental.

## Development build

Development toward v0.0.3 includes configurable automatic refresh: **I** cycles
5/15/30/60 seconds; **R**, **A**, and **Q** retain their existing controls.
Version metadata remains `0.0.2-alpha`; this is not a release.

From Windows PowerShell in the repository root:

```powershell
.\build-dev.ps1
.\dev\AsusH3BatteryMonitor.exe --self-test
.\dev\AsusH3BatteryMonitor.exe
```

The script selects a stable .NET 10 SDK (preferring
`C:\Users\M\.dotnet\dotnet.exe`), runs offline self-tests, publishes a
self-contained single-file Windows x64 EXE, then tests that EXE. A failed build
removes `dev/AsusH3BatteryMonitor.exe` to avoid leaving a stale result. Generated
files under `dev/` and `tmp/` stay local. See [usage and build details](docs/USAGE.md).

## License

MIT License
