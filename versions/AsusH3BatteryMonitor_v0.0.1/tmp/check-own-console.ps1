$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ConsoleCheck {
    [StructLayout(LayoutKind.Explicit, Size = 20)]
    public struct InputRecord {
        [FieldOffset(0)] public ushort EventType;
        [FieldOffset(4)] public int KeyDown;
        [FieldOffset(8)] public ushort RepeatCount;
        [FieldOffset(10)] public ushort VirtualKeyCode;
        [FieldOffset(14)] public char UnicodeChar;
    }
    [DllImport("kernel32.dll")] public static extern bool FreeConsole();
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool AttachConsole(uint pid);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    public static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    public static extern bool WriteConsoleInput(IntPtr input, InputRecord[] records, uint count, out uint written);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
    public static void Enter(uint pid) {
        FreeConsole();
        if (!AttachConsole(pid)) throw new Exception("AttachConsole failed: " + Marshal.GetLastWin32Error());
        IntPtr input = CreateFile("CONIN$", 0x40000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        try {
            if (input == new IntPtr(-1)) throw new Exception("Opening console input failed");
            var records = new InputRecord[] {
                new InputRecord { EventType=1, KeyDown=1, RepeatCount=1, VirtualKeyCode=13, UnicodeChar='\r' },
                new InputRecord { EventType=1, KeyDown=0, RepeatCount=1, VirtualKeyCode=13, UnicodeChar='\r' }
            };
            uint written;
            if (!WriteConsoleInput(input, records, 2, out written) || written != 2)
                throw new Exception("Writing Enter failed");
        } finally { CloseHandle(input); FreeConsole(); }
    }
}
'@
$exe = 'D:\programming\BIGPROJECTS\AsusH3BatteryMonitor\versions\AsusH3BatteryMonitor_v0.0.1\publish\AsusH3BatteryMonitor.exe'
foreach ($case in @(@{Arg='--help'; Expected=0}, @{Arg='--invalid'; Expected=1})) {
    $process = Start-Process -FilePath $exe -ArgumentList $case.Arg -PassThru
    Start-Sleep -Seconds 2
    if ($process.HasExited) { throw "$($case.Arg) closed before Enter" }
    [ConsoleCheck]::Enter([uint32]$process.Id)
    if (!$process.WaitForExit(10000)) { throw "$($case.Arg) did not exit after Enter" }
    if ($process.ExitCode -ne $case.Expected) { throw "Unexpected exit code: $($process.ExitCode)" }
    Write-Output "$($case.Arg): separate console stayed open until Enter; exit $($process.ExitCode)"
}
