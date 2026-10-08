using System.Globalization;

namespace AsusH3BatteryProbe;

public interface IFeatureTransport
{
    void SetFeature(byte[] packet);
    void GetFeature(byte[] buffer);
}

public sealed record TransactionResult(byte[]? Response, IReadOnlyList<string> Errors);
public sealed record ResponseDetails(bool PatternMatched, int Millivolts)
{
    public double Volts => Millivolts / 1000.0;
}

// Only the user-provided H3 capture packets are present here.
// No Strix Go packet, configuration query, input/output write or retry loop.
public static class Protocol
{
    public const int ReportSize = 64;
    public const byte ReportId = 0xFF;
    private static readonly byte[] Pattern = [0xFF, 0x12, 0x05, 0xFE, 0x0C, 0x04];

    public static bool IsAllowedDelay(int milliseconds) => milliseconds is 20 or 35 or 50 or 100;

    public static byte[] StepA() => Pad([0xFF, 0x08, 0x00, 0xFF, 0x04, 0x0C, 0xF1, 0x03, 0x02, 0x00]);
    public static byte[] StepB() => Pad([0xFF, 0x07, 0x00, 0xFD, 0x04, 0x0C, 0xF1, 0x02, 0x01]);
    public static byte[] StepD() => Pad([0xFF, 0x08, 0x00, 0xFF, 0x04, 0x0C, 0xF1, 0x03, 0x02, 0x01]);
    public static byte[] GetBuffer() => Pad([ReportId]);

    private static byte[] Pad(byte[] prefix)
    {
        var buffer = new byte[ReportSize];
        prefix.CopyTo(buffer, 0);
        return buffer;
    }

    public static string Hex(byte[] packet) => BitConverter.ToString(packet).Replace('-', ' ');

    public static ResponseDetails Parse(byte[] response)
    {
        if (response.Length != ReportSize)
            throw new ArgumentException("Expected exactly 64 bytes, including report ID.", nameof(response));
        bool matched = response.AsSpan(0, Pattern.Length).SequenceEqual(Pattern);
        int value = response[11] | (response[12] << 8);
        return new ResponseDetails(matched, value);
    }

    public static void PrintResponse(byte[] response, Action<string> log)
    {
        var details = Parse(response);
        log("GET_FEATURE byte indexes (zero-based, including report ID):");
        for (int i = 0; i < response.Length; i++)
            log($"[{i:D2}] = {response[i]:X2} (decimal {response[i]})");

        log(details.PatternMatched
            ? "ARMOURY CRATE RESPONSE PATTERN MATCHED"
            : "ARMOURY CRATE RESPONSE PATTERN NOT MATCHED");
        log($"Bytes [11] and [12]: {response[11]:X2} {response[12]:X2}; little-endian value = {details.Millivolts}");
        log($"Possible battery voltage: {details.Millivolts} mV");
        log(string.Format(CultureInfo.InvariantCulture, "{0} mV = {1:F3} V", details.Millivolts, details.Volts));
        log(details.PatternMatched
            ? "The prefix matches the supplied H3 capture. The voltage interpretation remains a hypothesis; no percentage is inferred."
            : "The response shape differs from the supplied capture. This decoded value must not be treated as a battery measurement.");
    }

    public static TransactionResult Run(IFeatureTransport transport, int delayMs,
        Action<int> wait, Action<string> log)
    {
        if (!IsAllowedDelay(delayMs))
            throw new ArgumentOutOfRangeException(nameof(delayMs), "Allowed delays: 20, 35, 50, 100 ms.");

        byte[]? response = null;
        var errors = new List<string>();
        string step = "A SET_FEATURE";
        bool setupAttempted = false;
        try
        {
            var packet = StepA();
            log($"A SET_FEATURE attempt (64 bytes): {Hex(packet)}");
            setupAttempted = true;
            transport.SetFeature(packet);
            log("A SET_FEATURE completed.");
            Wait(delayMs, wait, log);

            step = "B SET_FEATURE";
            packet = StepB();
            log($"B SET_FEATURE attempt (64 bytes): {Hex(packet)}");
            transport.SetFeature(packet);
            log("B SET_FEATURE completed.");
            Wait(delayMs, wait, log);

            step = "C GET_FEATURE";
            var buffer = GetBuffer();
            log($"C GET_FEATURE request buffer (64 bytes): {Hex(buffer)}");
            transport.GetFeature(buffer);
            response = buffer;
            log($"C GET_FEATURE received (64 bytes): {Hex(buffer)}");
        }
        catch (Exception ex)
        {
            errors.Add($"{step} failed: {ex.GetType().Name}: {ex.Message} (HRESULT 0x{ex.HResult:X8})");
        }
        finally
        {
            if (setupAttempted)
            {
                // Attempt the captured closing packet once, even if a feature call
                // reports failure after the device may already have accepted it.
                // Its firmware meaning is unknown; this is not a guarantee of recovery.
                try
                {
                    Wait(delayMs, wait, log);
                    var closingPacket = StepD();
                    log($"D SET_FEATURE attempt (64 bytes): {Hex(closingPacket)}");
                    transport.SetFeature(closingPacket);
                    log("D SET_FEATURE completed.");
                }
                catch (Exception ex)
                {
                    errors.Add($"D SET_FEATURE failed: {ex.GetType().Name}: {ex.Message} (HRESULT 0x{ex.HResult:X8}). The captured closing packet did not complete; device transaction state is unknown.");
                }
            }
        }
        return new TransactionResult(response, errors);
    }

    private static void Wait(int delayMs, Action<int> wait, Action<string> log)
    {
        log($"Wait {delayMs} ms.");
        wait(delayMs);
    }
}
