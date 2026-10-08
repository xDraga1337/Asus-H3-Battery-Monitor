using System.Diagnostics;
using System.Runtime.InteropServices;
using HidSharp;

namespace AsusH3BatteryProbe;

internal static class Program
{
    private const int VendorId = 0x0B05;
    private const int ProductId = 0x1963;
    private const uint CollectionUsage = 0xFF000001;

    private static int Main(string[] args)
    {
        bool diagnostic = args.Contains("--diagnostic") || args.Contains("--inspect");
        bool menuUsed = false;
        try
        {
            // Handle offline/help modes before any device enumeration or HID access.
            if (args.Length == 1 && args[0] == "--self-test")
                return OfflineChecks.Run(); // No DeviceList access or HidStream creation.
            if (args.Length == 1 && args[0] is "--help" or "-h")
            {
                PrintHelp();
                return 0;
            }

            int delayMs = 35;
            bool inspectOnly = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--inspect") inspectOnly = true;
                else if (args[i] == "--diagnostic") diagnostic = true;
                else if (args[i] == "--delay" && i + 1 < args.Length &&
                         int.TryParse(args[++i], out int requested) && Protocol.IsAllowedDelay(requested))
                    delayMs = requested;
                else
                {
                    Console.Error.WriteLine("Invalid argument. Allowed: --diagnostic, --inspect, --delay 20|35|50|100, --help, --self-test.");
                    return 1;
                }
            }

            if (!OperatingSystem.IsWindows())
            {
                Console.Error.WriteLine("This probe targets Windows only. No HID access attempted.");
                return 1;
            }

            // Redirected CLI runs remain one-shot and unattended. Inspection/help/
            // self-test keep their existing behavior instead of entering the menu.
            if (inspectOnly || Console.IsInputRedirected || Console.IsOutputRedirected)
                return ReadBattery(diagnostic, delayMs, inspectOnly);

            // The menu itself keeps a double-click console alive. Suppress the old
            // Enter-to-close pause so Q exits immediately without a second prompt.
            menuUsed = true;
            while (true)
            {
                Console.Clear();
                // This method owns and disposes its HID stream before returning,
                // including on failures. R re-enumerates and opens a fresh stream,
                // so disconnect/reconnect can be retried without restarting the app.
                ReadBattery(diagnostic, delayMs, inspectOnly: false);
                Console.WriteLine();
                Console.WriteLine("[R] Refresh    [Q] Quit");

                // Block for an explicit key; no Enter, timer, polling or automatic
                // retry. Ignore other keys without issuing another HID transaction.
                ConsoleKey key;
                do { key = Console.ReadKey(true).Key; }
                while (key is not ConsoleKey.R and not ConsoleKey.Q);
                if (key == ConsoleKey.Q) return 0;
            }
        }
        catch (Exception ex)
        {
            PrintUnavailable(diagnostic, ex);
            return 3;
        }
        finally
        {
            // Covers success, early returns and handled exceptions in one place.
            if (!menuUsed) PauseIfConsoleWouldClose();
        }
    }

    // Keep a complete read attempt in its own scope: the existing protocol runs
    // once, D cleanup completes, and the using stream is closed before the menu.
    // A failed attempt returns its status rather than ending the interactive app.
    private static int ReadBattery(bool diagnostic, int delayMs, bool inspectOnly)
    {
        try
        {
            var watch = Stopwatch.StartNew();
            void Log(string text)
            {
                if (diagnostic) Console.WriteLine($"[{watch.Elapsed.TotalMilliseconds,10:F3} ms] {text}");
            }

            Log("ASUS TUF Gaming H3 Wireless monitor v0.0.1; HidSharp 2.6.4; Windows x64.");
            Log($"Target VID=0B05 PID=1963 MI_03 usagePage=FF00 usage=0001; delay={delayMs} ms.");
            Log("Only the user-provided capture packets are enabled. Setup/closing packet meanings are unconfirmed.");

            // Verify the existing interface, collection and feature report requirements.
            var candidates = new List<HidDevice>();
            var devices = DeviceList.Local.GetHidDevices(VendorId, ProductId).ToArray();
            Log($"Matching VID/PID HID paths: {devices.Length}");
            bool uncertainTarget = false;
            foreach (var device in devices)
            {
                Log($"HID path: {device.DevicePath}");
                try
                {
                    Log($"Report sizes including ID: input={device.GetMaxInputReportLength()}, output={device.GetMaxOutputReportLength()}, feature={device.GetMaxFeatureReportLength()} bytes.");
                    var descriptor = device.GetReportDescriptor();
                    bool usageMatches = false;
                    bool featureMatches = false;
                    foreach (var item in descriptor.DeviceItems)
                    {
                        var usages = item.Usages.GetAllValues().ToArray();
                        Log("Collection usages: " + string.Join(", ", usages.Select(value =>
                            $"usagePage=0x{value >> 16:X4} usage=0x{value & 0xFFFF:X4}")));
                        if (!usages.Contains(CollectionUsage)) continue;
                        usageMatches = true;
                        foreach (var report in item.FeatureReports)
                        {
                            Log($"Target collection feature report: ID=0x{report.ReportID:X2}, length={report.Length} bytes including ID.");
                            if (report.ReportID == Protocol.ReportId && report.Length == Protocol.ReportSize)
                                featureMatches = true;
                        }
                    }
                    bool interfaceMatches = device.DevicePath.Contains("&mi_03", StringComparison.OrdinalIgnoreCase);
                    if (usageMatches && interfaceMatches && featureMatches &&
                        device.GetMaxFeatureReportLength() >= Protocol.ReportSize)
                        candidates.Add(device);
                    else
                        Log($"Not selected: usage FF00:0001={usageMatches}, MI_03={interfaceMatches}, feature FF/64={featureMatches}.");
                }
                catch (Exception ex)
                {
                    PrintUnavailable(diagnostic, ex);
                    return 3;
                }
            }

            if (uncertainTarget || candidates.Count != 1)
            {
                Log($"Refusing feature transaction: eligible paths={candidates.Count}; uninspectable MI_03 path={uncertainTarget}. Expected one verified FF00:0001 collection with feature FF/64.");
                PrintUnavailable(diagnostic);
                return 4;
            }

            var selected = candidates[0];
            Log($"Selected HID device path: {selected.DevicePath}");
            Log($"Selected report sizes: input={selected.GetMaxInputReportLength()}, output={selected.GetMaxOutputReportLength()}, feature={selected.GetMaxFeatureReportLength()} bytes.");
            if (inspectOnly)
            {
                Log("Inspection only. No HidStream opened; no SET_FEATURE or GET_FEATURE sent.");
                return 0;
            }

            Log("Performing ONE captured candidate sequence: A -> B -> C -> D; no retries or polling.");
            var configuration = new OpenConfiguration();
            configuration.SetOption(OpenOption.Exclusive, false);
            using var stream = selected.Open(configuration);
            stream.ReadTimeout = 2000;
            stream.WriteTimeout = 2000;
            Log("Device stream opened with Exclusive=false. Read/write timeouts=2000 ms; synchronous feature IO is governed by Windows.");
            // Protocol.Run owns the captured packet sequence and its existing delays.
            var result = Protocol.Run(new HidTransport(stream), delayMs, Thread.Sleep, Log);
            foreach (string error in result.Errors) Log("ERROR: " + error);
            if (diagnostic && result.Response is not null) Protocol.PrintResponse(result.Response, Log);
            Log("Transaction finished.");
            // Preserve the existing per-attempt status codes for one-shot CLI runs.
            if (result.Errors.Count > 0)
            {
                PrintUnavailable(diagnostic);
                return 3;
            }
            if (result.Response is null || !Protocol.Parse(result.Response).PatternMatched)
            {
                PrintUnavailable(diagnostic);
                return 2;
            }

            // Presentation is separate from the unchanged protocol parser and HID transaction.
            BatteryDisplay.Print(new BatteryReading(Protocol.Parse(result.Response).Millivolts));
            return 0;
        }
        catch (Exception ex)
        {
            PrintUnavailable(diagnostic, ex);
            return 3;
        }
    }

    private static void PrintUnavailable(bool diagnostic, Exception? ex = null)
    {
        Console.Error.WriteLine("Headset not connected or unavailable.");
        if (!diagnostic || ex is null) return;
        Console.Error.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message} (HRESULT 0x{ex.HResult:X8})");
        if (ex.InnerException is not null)
            Console.Error.WriteLine($"Inner error: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
        Console.Error.WriteLine("No automatic retry will be attempted. Include this error in the pasted results.");
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetConsoleProcessList([Out] uint[] processList, uint processCount);

    private static void PauseIfConsoleWouldClose()
    {
        // A double-click normally creates a console owned only by this process.
        // A shell shares its console; redirected CLI runs must also remain unattended.
        if (!OperatingSystem.IsWindows() || Console.IsInputRedirected || Console.IsOutputRedirected)
            return;

        if (GetConsoleProcessList(new uint[2], 2) != 1)
            return;

        try
        {
            Console.WriteLine();
            Console.WriteLine("Press Enter to close...");
            Console.ReadLine();
        }
        catch (IOException)
        {
            // A console closed during the pause must not replace the probe's exit code.
        }
        catch (InvalidOperationException)
        {
            // Console input may become unavailable while the program is exiting.
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("AsusH3BatteryMonitor.exe [--diagnostic] [--delay 20|35|50|100] [--inspect]");
        Console.WriteLine("Default: read battery with 35 ms waits; [R] Refresh, [Q] Quit (no Enter).");
        Console.WriteLine("Each refresh opens a fresh HID connection and performs one A/B/C/D transaction. Redirected runs read once.");
        Console.WriteLine("Battery percentage is approximate: >=3950 mV=100%, >=3850=75%, >=3750=50%, >=3500=25%, below=0% (critically low).");
        Console.WriteLine("Charging detection is unconfirmed; voltage-based percentages can be misleading while charging.");
        Console.WriteLine("--diagnostic: show verbose HID metadata, packets, timestamps and response bytes.");
        Console.WriteLine("--inspect: enumerate matching devices and report metadata; send no feature reports.");
        Console.WriteLine("--self-test: offline synthetic checks only; never access DeviceList or HID devices.");
        Console.WriteLine("No custom packets, polling, automatic delay sweep, firmware or configuration commands.");
        Console.WriteLine("Exit codes: 0=match/inspection/checks OK; 1=arguments/platform; 2=pattern mismatch; 3=IO/check failure; 4=selection refused.");
    }

    private sealed class HidTransport(HidStream stream) : IFeatureTransport
    {
        public void SetFeature(byte[] packet) => stream.SetFeature(packet);
        public void GetFeature(byte[] buffer) => stream.GetFeature(buffer);
    }
}
