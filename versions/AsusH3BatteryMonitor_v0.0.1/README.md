# ASUS TUF Gaming H3 Wireless Battery Monitor

Version **v0.0.2 Alpha** (project version `0.0.2-alpha`). Supported device: **ASUS TUF Gaming H3 Wireless**.

This Windows x64 console reader performs one known-working HID feature-report
transaction, then shows an approximate battery percentage and the measured
millivolts. The protocol is reverse engineered and experimental.

## Run

Copy `publish\AsusH3BatteryMonitor.exe` anywhere and double-click it. The console
stays open with **R** to refresh, **A** to toggle automatic refresh, and **Q** to
quit (no Enter). Automatic refresh is **OFF by default**. Enabling it requests a
battery query immediately, then polls every 5 seconds. A busy query always
finishes before another starts; missed polls never build a backlog.
Manual and automatic requests share one worker; transactions never overlap.
The display is replaced on refresh, including in diagnostic mode. Errors remain
in the menu so manual or automatic refresh can recover after the headset or USB
dongle reconnects. With automatic refresh enabled, recovery is attempted on
the next scheduled query after reconnection; with it disabled, press R.
Q stops scheduling immediately and lets any active transaction
finish its existing cleanup before exiting; it never waits for the next poll. From PowerShell:

```powershell
.\AsusH3BatteryMonitor.exe
```

Example:

```text
ASUS TUF Gaming H3 Wireless
Battery: 50%
Voltage: 3782 mV

Automatic refresh: OFF (5 seconds)
[R] Refresh    [A] Toggle automatic refresh    [Q] Quit

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
`--help` lists options and exit codes. Redirected runs remain one-shot. There is no GUI or tray.

## Approximate battery levels

These are explicitly approximate stepped discharge estimates, not calibrated
state-of-charge percentages or direct percentages reported by the headset.
Battery percentage estimates are experimental. Voltage accuracy has not been
independently verified against a calibrated meter; displayed millivolts should
not be treated as a validated physical measurement.

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

**Charging detection is not confirmed in v0.0.2 Alpha.** Observed charging voltages
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
cleanup, stream options and timeout settings remain unchanged. Each refresh uses
one transaction with no internal retries or additional charging commands.

## Build and layout

The project publishes a **self-contained single-file Windows x64 EXE**, bundling
the .NET runtime and HidSharp. No separate .NET installation or companion DLLs
are needed. Bundled native runtime libraries may extract to a temporary folder
when run; that folder must be writable.

From PowerShell in this version folder, use the pinned Windows .NET SDK
10.0.401:

```powershell
& 'C:\Users\M\.dotnet\dotnet.exe' run --project '.\src\AsusH3BatteryMonitor.csproj' -- --self-test
& 'C:\Users\M\.dotnet\dotnet.exe' publish '.\src\AsusH3BatteryMonitor.csproj' -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o '.\publish'
```

Build output and intermediate files stay under `tmp/`. Restore uses the local
HidSharp archive when present and official NuGet for missing packages, including
the Windows runtime pack. The executable is `publish/AsusH3BatteryMonitor.exe`.

- `src/Program.cs`: CLI, console controls, unchanged device selection and HID handoff.
- `src/InteractiveRefresh.cs`: serialized refresh scheduling and buffered query output.
- `src/Protocol.cs`: preserved packets, transaction, parser and diagnostics.
- `src/BatteryDisplay.cs`: approximate mapping and presentation.
- `src/OfflineChecks.cs`: synthetic protocol, presentation and refresh scheduling checks.
- `publish/`: final executable; ignored by Git.
- `tmp/` and `packages/`: generated build output/caches; ignored by Git.
- `notes/`: build and verification evidence.

For v0.0.2 Alpha, the source remains in the existing
`versions/AsusH3BatteryMonitor_v0.0.1` directory; the folder name is retained for
compatibility and does not indicate the application version. No source tree was
reorganized or duplicated. The Desktop working project remains read-only.

See the repository-root `RELEASE_NOTES.md` for changes since v0.0.1.
