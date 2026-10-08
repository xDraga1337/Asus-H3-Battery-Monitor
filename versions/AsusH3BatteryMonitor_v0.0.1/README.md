# ASUS TUF Gaming H3 Wireless Battery Monitor

Version **v0.0.1**. Supported device: **ASUS TUF Gaming H3 Wireless**.

This Windows x64 console reader performs one known-working HID feature-report
transaction, then shows an approximate battery percentage and the measured
millivolts. The protocol is reverse engineered and experimental.

## Run

Copy `publish\AsusH3BatteryMonitor.exe` anywhere and double-click it. The console
stays open until Enter when launched in its own console. From PowerShell:

```powershell
.\AsusH3BatteryMonitor.exe
```

Example:

```text
ASUS TUF Gaming H3 Wireless
Battery: 25%
Voltage: 3694 mV
```

Unavailable devices, an unavailable required collection, rejected responses or
failed feature transactions produce `Headset not connected or unavailable.`

For detailed HID paths, report metadata, packets, timestamps, every response byte
and exceptions:

```powershell
.\AsusH3BatteryMonitor.exe --diagnostic
```

`--inspect` prints device metadata without sending a feature transaction.
`--self-test` runs synthetic offline checks without accessing HID devices.
`--help` lists options and exit codes. There is no continuous polling, GUI or tray.

## Approximate battery levels

These are explicitly approximate stepped discharge estimates, not calibrated
state-of-charge percentages or direct percentages reported by the headset.

| Millivolts | Displayed level |
| --- | --- |
| >= 3950 | 100% |
| >= 3850 and < 3950 | 75% |
| >= 3750 and < 3850 | 50% |
| >= 3500 and < 3750 | 25% |
| < 3500 | 0% (critically low) |

User-observed discharge data:

| Voltage | Armoury Crate / observed state |
| --- | --- |
| 3996 mV | 100% |
| 3917 mV | 75% |
| 3768 mV | 50% |
| 3750 mV | 25% |
| 3694 mV | 25% |
| 3691 mV | 25% |
| 3659 mV | 25% |
| 3407 mV | Effectively empty; could not power back on until charging |

The requested mapping deliberately shows 50% at 3750 mV despite the observed 25%
reading. More measurements are needed before changing the approximation.

## Charging limitation

**Charging detection is not confirmed in v0.0.1.** Observed charging voltages
include 3998 mV and 4303 mV. Charging elevates voltage and can make this mapping
show 100% when the battery is not full. The normal reader behavior is preserved;
use its percentage as a rough indication while unplugged. A displayed 100%
does not establish that charging is complete.

The available evidence contains a H3 response fixture and voltage observations,
but no paired full responses labelled charging/unplugged that establish a H3
charging field. Another headset model's charging flag cannot confirm this one.
No voltage heuristic or speculative flag is used, and no new HID commands were
added. `BatteryReading.IsCharging` remains unknown (`null`) in normal execution.
`BatteryDisplay` is prepared for future confirmed detection: charging shows
`Battery: Unknown`, or `Battery: ~25%` when an independently supported estimate
is available. It never derives that charging estimate from elevated voltage.

## Preserved protocol

`src/Protocol.cs` is copied byte-for-byte from the read-only working probe.
Device selection remains VID `0B05`, PID `1963`, interface `MI_03`, collection
`FF00:0001`, feature report ID `FF`, 64 bytes including ID, and exactly one
eligible path. The same A -> B -> C -> D packets and three 35 ms waits are used
by default. Existing optional delays (20/35/50/100 ms) are preserved.

The existing response-prefix check remains unchanged. Bytes `[11]` and `[12]`
are decoded little-endian as millivolts by the existing parser. Transaction
cleanup, stream options and timeout settings remain unchanged. No retries,
additional queries or charging commands are introduced.

## Build and layout

The project publishes a **self-contained single-file Windows x64 EXE**, bundling
the .NET runtime and HidSharp. No separate .NET installation or companion DLLs
are needed. Bundled native runtime libraries may extract to a temporary folder
when run; that folder must be writable.

With the pinned .NET SDK 10.0.401 (the build script uses the existing installation
at `C:\Users\M\.dotnet\dotnet.exe`):

```powershell
python build.py
```

From WSL, use `python3 build.py`. The script resolves its own version folder and
keeps configured build output, package caches and temporary build files there.
Restore uses the local HidSharp archive when present and official NuGet for
missing packages, including the Windows runtime pack.

- `src/Program.cs`: CLI, unchanged device selection, HID handoff, errors and pause.
- `src/Protocol.cs`: preserved packets, transaction, parser and diagnostics.
- `src/BatteryDisplay.cs`: approximate mapping and presentation.
- `src/OfflineChecks.cs`: synthetic protocol and presentation checks.
- `publish/`: final executable; ignored by Git.
- `tmp/` and `packages/`: generated build output/caches; ignored by Git.
- `notes/`: build and verification evidence.

Future versions must use new folders under
`D:\programming\BIGPROJECTS\AsusH3BatteryMonitor\versions`, named
`AsusH3BatteryMonitor_vX.Y.Z`. Never overwrite an older version. The Desktop
working project is read-only reference material and must remain untouched.
