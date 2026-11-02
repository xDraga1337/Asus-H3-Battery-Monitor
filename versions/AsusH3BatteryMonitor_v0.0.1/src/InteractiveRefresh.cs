namespace AsusH3BatteryProbe;

// Only the console thread calls this controller. The sole worker owns a complete
// query and private output buffer; it never writes to the live console.
internal sealed class InteractiveRefresh(Action<TextWriter> query) : IDisposable
{
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private Task<string>? active;
    private bool manualRequested = true; // Preserve the initial startup reading.
    private bool automaticRequested;
    private bool stopped;
    private TimeSpan nextRefresh;

    internal bool Automatic { get; private set; }

    internal void ToggleAutomatic()
    {
        if (stopped) return;
        Automatic = !Automatic;
        automaticRequested = Automatic; // ON requests an immediate query; OFF cancels it.
    }

    internal void RequestManual()
    {
        if (!stopped) manualRequested = true;
    }

    internal string? Update(TimeSpan now)
    {
        if (stopped) return null;
        string? result = null;
        if (active is not null)
        {
            if (!active.IsCompleted) return null;
            result = active.GetAwaiter().GetResult();
            active = null; // ReadBattery has closed its stream, including on failure.
        }
        if (manualRequested || automaticRequested || (Automatic && now >= nextRefresh))
        {
            manualRequested = false;
            automaticRequested = false;
            nextRefresh = now + Interval;
            active = Task.Run(() =>
            {
                using var output = new StringWriter();
                try { query(output); }
                catch (Exception ex)
                {
                    // A polling/presentation failure must not kill the menu.
                    output.WriteLine("Headset not connected or unavailable.");
                    output.WriteLine($"Refresh failed: {ex.GetType().Name}: {ex.Message}");
                }
                return output.ToString();
            });
        }
        return result;
    }

    public void Dispose()
    {
        stopped = true;
        Automatic = false;
        manualRequested = automaticRequested = false;
        // No timer wait on quit. Finish only an already active transaction so its
        // captured D cleanup and stream disposal keep their original ordering.
        active?.GetAwaiter().GetResult();
        active = null;
    }
}
