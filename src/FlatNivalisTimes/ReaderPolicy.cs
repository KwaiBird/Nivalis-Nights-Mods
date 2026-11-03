namespace NivalisNights.FlatNivalisTimes;

[Flags]
internal enum ReaderCommand { None = 0, View = 1, Background = 2, Contrast = 4 }
internal enum PaperBackground { Original, Faded, Plain }

internal static class ReaderPalette
{
    internal const float PatternOpacity = .55f;
    internal static (float R, float G, float B, float A) Paper => (.88f, .85f, .77f, 1);
    // One opaque warm palette for every background. Avoid near-white article
    // fills without attenuating the hand material, text or photographs.
    internal static (float R, float G, float B, float A) Panel(bool forecastRow)
        => forecastRow ? (.80f, .75f, .65f, 1) : (.68f, .64f, .55f, 1);
}

internal sealed class ReaderEventToken
{
    private int device = -1;
    private double time;
    private bool valid;
    internal void Set(int eventDevice, double eventTime) { device = eventDevice; time = eventTime; valid = true; }
    internal void Clear() { valid = false; device = -1; }
    internal bool Matches(int eventDevice, double eventTime, bool started) => valid && started && device == eventDevice && time == eventTime;
}

// Post-state snapshots, one per device event. No frame-wide or held-key reservation.
internal sealed class ReaderPressPolicy
{
    private HashSet<long> previous = new();
    internal void Seed(IEnumerable<long> held) => previous = new(held);
    internal ReaderCommand Sample(IEnumerable<long> held, IReadOnlyDictionary<long, ReaderCommand> bindings, bool allowed)
    {
        var current = new HashSet<long>(held);
        var down = current.Where(key => !previous.Contains(key)).ToArray();
        previous = current;
        if (!allowed || down.Length == 0 || down.Any(key => !bindings.ContainsKey(key))) return ReaderCommand.None;
        ReaderCommand result = ReaderCommand.None;
        foreach (long key in down) result |= bindings[key];
        return result;
    }
}

internal static class ReaderGeometry
{
    internal static (int Width, int Height) Target(int width, int height, int limit)
    {
        int units = Math.Min(Math.Min(width / 16, height / 9), Math.Min(limit / 16, limit / 9));
        return units < 1 ? (0, 0) : (16 * units, 9 * units);
    }
}

internal static class ReaderHintOrder
{
    internal static int Pad(string? control) => control?.ToLowerInvariant() switch
    {
        "leftshoulder" or "lefttrigger" => 0,
        "rightshoulder" or "righttrigger" => 1,
        _ => 2
    };
}
