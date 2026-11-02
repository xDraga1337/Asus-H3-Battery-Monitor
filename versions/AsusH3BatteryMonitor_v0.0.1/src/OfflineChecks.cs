namespace AsusH3BatteryProbe;

// Runs only when --self-test is explicitly supplied. No HidSharp/DeviceList calls.
internal static class OfflineChecks
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool ok, string name)
        {
            if (!ok) throw new InvalidOperationException("Offline check failed: " + name);
            Console.WriteLine("PASS: " + name);
            checks++;
        }

        var expected = new[]
        {
            (Protocol.StepA(), "FF0800FF040CF1030200"),
            (Protocol.StepB(), "FF0700FD040CF10201"),
            (Protocol.StepD(), "FF0800FF040CF1030201"),
            (Protocol.GetBuffer(), "FF")
        };
        foreach (var (packet, prefixHex) in expected)
        {
            byte[] prefix = Convert.FromHexString(prefixHex);
            Check(packet.Length == 64 && packet.AsSpan(0, prefix.Length).SequenceEqual(prefix) &&
                  packet.Skip(prefix.Length).All(value => value == 0), "exact prefix and zero padding: " + prefixHex);
        }
        var independentPacket = Protocol.StepA();
        independentPacket[0] = 0;
        Check(Protocol.StepA()[0] == 0xFF, "packet buffers are independent");

        var fixture = new byte[64];
        Convert.FromHexString("FF1205FE0C041F0B0103054D0F").CopyTo(fixture, 0);
        Check(Protocol.Parse(fixture) is { PatternMatched: true, Millivolts: 3917 }, "capture prefix and little-endian 4D 0F -> 3917");
        Check(Protocol.Parse(fixture).Volts == 3.917, "millivolts to volts");
        fixture[11] = 0x4C;
        Check(Protocol.Parse(fixture).Millivolts == 3916, "4C 0F -> 3916");
        var mismatch = new byte[64];
        Convert.FromHexString("FF0100FE00040F0581AAC6010F00").CopyTo(mismatch, 0);
        Check(!Protocol.Parse(mismatch).PatternMatched, "host manual-test shape is not accepted as capture pattern");
        Check(!Protocol.Parse(new byte[64]).PatternMatched, "all-zero reply is not a match");
        bool rejectedLength = false;
        try { Protocol.Parse(new byte[13]); } catch (ArgumentException) { rejectedLength = true; }
        Check(rejectedLength, "short reply rejected");

        var printLines = new List<string>();
        Protocol.PrintResponse(mismatch, printLines.Add);
        Check(printLines.Any(line => line.Contains("must not be treated as a battery measurement")), "mismatched shape labels decoded number as untrusted");
        Check(printLines.Count(line => line.StartsWith("[")) == 64, "all 64 byte indexes printed");

        foreach (int delay in new[] { 20, 35, 50, 100 })
        {
            var transport = new FakeTransport(fixture);
            var waits = new List<int>();
            var result = Protocol.Run(transport, delay, waits.Add, _ => { });
            Check(transport.Calls.SequenceEqual(new[] { "A", "B", "C", "D" }) &&
                  waits.SequenceEqual(new[] { delay, delay, delay }) && result.Errors.Count == 0 &&
                  result.Response!.SequenceEqual(fixture), "one A/B/C/D sequence and three waits at " + delay + " ms");
        }
        foreach (string failure in new[] { "A", "B", "C", "D" })
        {
            var transport = new FakeTransport(fixture, failure);
            var result = Protocol.Run(transport, 35, _ => { }, _ => { });
            string[] calls = failure == "A" ? ["A", "D"] : failure == "B" ? ["A", "B", "D"] : ["A", "B", "C", "D"];
            Check(transport.Calls.SequenceEqual(calls) && result.Errors.Count == 1,
                "failure at " + failure + " produces no retries and only one closing-packet attempt");
        }
        var bothFailures = new FakeTransport(fixture, "B,D");
        var bothResult = Protocol.Run(bothFailures, 35, _ => { }, _ => { });
        Check(bothResult.Errors.Count == 2 && bothFailures.Calls.SequenceEqual(new[] { "A", "B", "D" }), "primary and closing errors both retained");

        var forbidden = new FakeTransport(fixture);
        bool rejectedDelay = false;
        try { Protocol.Run(forbidden, 36, _ => { }, _ => { }); }
        catch (ArgumentOutOfRangeException) { rejectedDelay = true; }
        Check(rejectedDelay && forbidden.Calls.Count == 0, "unapproved delay rejected before any transport call");

        foreach (var (millivolts, percent) in new[]
        {
            (3996, 100), (3917, 75), (3768, 50), (3750, 50), (3694, 25), (3691, 25), (3659, 25), (3407, 0),
            (3950, 100), (3949, 75), (3850, 75), (3849, 50),
            (3749, 25), (3500, 25), (3499, 0)
        })
            Check(BatteryDisplay.ApproximatePercent(millivolts) == percent,
                $"approximate battery: {millivolts} mV -> {percent}%");

        var originalOutput = Console.Out;
        using var summary = new StringWriter();
        try
        {
            Console.SetOut(summary);
            BatteryDisplay.Print(new BatteryReading(3917));
            BatteryDisplay.Print(new BatteryReading(3407));
            // Synthetic future-status inputs test presentation only, not detection.
            BatteryDisplay.Print(new BatteryReading(3998, IsCharging: true));
            BatteryDisplay.Print(new BatteryReading(3998, IsCharging: true, EstimatedPercentWhileCharging: 25));
        }
        finally
        {
            Console.SetOut(originalOutput);
        }
        Check(summary.ToString() == string.Join(Environment.NewLine,
            "ASUS TUF Gaming H3 Wireless", "Battery: 75%", "Voltage: 3917 mV",
            "ASUS TUF Gaming H3 Wireless", "Battery: 0% (critically low)", "Voltage: 3407 mV",
            "ASUS TUF Gaming H3 Wireless", "Battery: Unknown", "Status: Charging", "Voltage: 3998 mV",
            "ASUS TUF Gaming H3 Wireless", "Battery: ~25%", "Status: Charging", "Voltage: 3998 mV", ""),
            "concise summary, critical-low label and future charging presentation");
        Check(new BatteryReading(4303).IsCharging is null,
            "high voltage alone does not claim charging detection");
        CheckAutomaticRefresh(Check);
        Console.WriteLine($"OFFLINE CHECKS PASSED: {checks}. Synthetic data only; no HID enumeration or device access.");
        return 0;
    }

    private static void CheckAutomaticRefresh(Action<bool, string> check)
    {
        int calls = 0;
        int inFlight = 0;
        int maxInFlight = 0;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim(true);
        using var completed = new AutoResetEvent(false);
        using var refresh = new InteractiveRefresh(output =>
        {
            int current = Interlocked.Increment(ref inFlight);
            maxInFlight = Math.Max(maxInFlight, current);
            int attempt = Interlocked.Increment(ref calls);
            entered.Set();
            try
            {
                if (!release.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("Synthetic query was not released");
                if (attempt == 2) throw new IOException("Synthetic disconnect");
                output.WriteLine("Synthetic battery reading " + attempt);
            }
            finally
            {
                Interlocked.Decrement(ref inFlight);
                completed.Set();
            }
        });
        void AwaitQuery()
        {
            if (!completed.WaitOne(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Synthetic query did not finish");
        }
        string Collect(TimeSpan time)
        {
            string? result = null;
            if (!SpinWait.SpinUntil(() => (result = refresh.Update(time)) is not null,
                    TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Query result was not collected");
            return result!;
        }

        check(!refresh.Automatic && InteractiveRefresh.Interval == TimeSpan.FromSeconds(5),
            "automatic refresh defaults OFF with fixed 5-second interval");
        refresh.Update(TimeSpan.Zero);
        AwaitQuery();
        Collect(TimeSpan.Zero);
        refresh.Update(TimeSpan.FromHours(1));
        check(calls == 1, "startup query occurs once; OFF never polls");

        refresh.ToggleAutomatic();
        refresh.Update(TimeSpan.FromHours(1));
        AwaitQuery();
        check(Collect(TimeSpan.FromHours(1)).Contains("Synthetic disconnect"),
            "enabling automatic refresh queries immediately; polling errors become display results");
        refresh.Update(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(4.999));
        check(calls == 2, "no automatic query before 5 seconds");
        refresh.Update(TimeSpan.FromHours(1) + InteractiveRefresh.Interval);
        AwaitQuery();
        check(Collect(TimeSpan.FromSeconds(3605)).Contains("Synthetic battery reading 3"),
            "polls at 5 seconds and recovers after a failed query");

        entered.Reset();
        release.Reset();
        try
        {
            refresh.RequestManual();
            refresh.Update(TimeSpan.FromSeconds(3606));
            check(entered.Wait(TimeSpan.FromSeconds(5)), "manual refresh starts while automatic mode is ON");
            refresh.RequestManual();
            refresh.ToggleAutomatic(); // OFF
            refresh.ToggleAutomatic(); // ON: immediate request while busy
            refresh.Update(TimeSpan.FromHours(2));
            check(calls == 4 && maxInFlight == 1, "manual, toggle and overdue polling never overlap HID queries");
            refresh.ToggleAutomatic(); // OFF cancels its queued immediate request.
        }
        finally { release.Set(); }
        AwaitQuery();
        Collect(TimeSpan.FromHours(2)); // The coalesced manual request still runs.
        AwaitQuery();
        Collect(TimeSpan.FromHours(2));
        refresh.Update(TimeSpan.FromHours(3));
        check(calls == 5 && !refresh.Automatic && maxInFlight == 1,
            "OFF cancels automatic polling while preserving a queued manual refresh");

        refresh.ToggleAutomatic();
        refresh.Dispose();
        refresh.Update(TimeSpan.FromDays(1));
        refresh.RequestManual();
        refresh.ToggleAutomatic();
        refresh.Update(TimeSpan.FromDays(2));
        check(calls == 5 && !refresh.Automatic, "quit cancels pending work without waiting for the polling interval");
    }

    private sealed class FakeTransport(byte[] response, string failure = "") : IFeatureTransport
    {
        public List<string> Calls { get; } = [];
        public void SetFeature(byte[] packet)
        {
            string step = packet.SequenceEqual(Protocol.StepA()) ? "A" :
                packet.SequenceEqual(Protocol.StepB()) ? "B" :
                packet.SequenceEqual(Protocol.StepD()) ? "D" : throw new InvalidOperationException("Unobserved packet");
            Call(step);
        }
        public void GetFeature(byte[] buffer)
        {
            if (!buffer.SequenceEqual(Protocol.GetBuffer())) throw new InvalidOperationException("Wrong GET buffer");
            Call("C");
            response.CopyTo(buffer, 0);
        }
        private void Call(string step)
        {
            Calls.Add(step);
            if (failure.Split(',').Contains(step)) throw new IOException("Synthetic failure at " + step);
        }
    }
}
