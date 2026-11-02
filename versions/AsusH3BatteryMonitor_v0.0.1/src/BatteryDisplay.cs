namespace AsusH3BatteryProbe;

// null means charging state is unknown, not that the headset is unplugged.
// v0.0.1 supplies only millivolts: no H3 charging flag has been confirmed.
internal sealed record BatteryReading(int Millivolts, bool? IsCharging = null,
    int? EstimatedPercentWhileCharging = null);

internal static class BatteryDisplay
{
    // Coarse, approximate discharge thresholds supplied by the user.
    // Charging raises voltage, so this mapping cannot measure charge accurately then.
    internal static int ApproximatePercent(int millivolts) => millivolts switch
    {
        >= 3950 => 100,
        >= 3850 => 75,
        >= 3750 => 50,
        >= 3500 => 25,
        _ => 0
    };

    internal static void Print(BatteryReading reading, TextWriter? output = null)
    {
        output ??= Console.Out;
        output.WriteLine("ASUS TUF Gaming H3 Wireless");
        if (reading.IsCharging == true)
        {
            // Reserved for a future confirmed detector. An estimate must come from
            // independent evidence, such as a trusted prior unplugged measurement.
            // Never derive it from the elevated charging voltage.
            output.WriteLine(reading.EstimatedPercentWhileCharging is int estimate
                ? $"Battery: ~{estimate}%"
                : "Battery: Unknown");
            output.WriteLine("Status: Charging");
        }
        else
        {
            // Preserve current behavior while charging state remains unconfirmed.
            int percent = ApproximatePercent(reading.Millivolts);
            output.WriteLine($"Battery: {percent}%" + (percent == 0 ? " (critically low)" : ""));
        }
        output.WriteLine($"Voltage: {reading.Millivolts} mV");
    }
}
