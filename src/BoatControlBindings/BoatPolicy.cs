namespace NivalisNights.BoatControlBindings;

public enum Operation { Engine, Dock, Light }
public enum BoatMode { Unavailable, Ready, Driving }

public static class BoatHintGeometry
{
    // Native keyboard images already contain both lettering and frame, even when
    // the device's separate TMP display policy requests input text.
    public static bool NativeIcon(bool pad, bool displayInputText, bool hasIcon, string? spriteName) =>
        hasIcon && (!pad || !displayInputText) && !string.Equals(spriteName, "Default", StringComparison.OrdinalIgnoreCase);

    // TMP may report zero before its first active layout. Keep localized text visible until measurement settles.
    public static float TextWidth(float measured, float fontSize, int length) => length <= 0 ? 0
        : (float.IsFinite(measured) && measured > 0 ? measured : Math.Max(1, fontSize) * length) + 4;
}

// Kept independent of Unity for boundary and held-input checks.
public static class BoatPolicy
{
    public static bool Visible(Operation op, BoatMode mode, bool canDock, bool busy) => !busy &&
        (op == Operation.Engine ? mode is BoatMode.Ready or BoatMode.Driving :
         op == Operation.Dock ? mode == BoatMode.Driving && canDock : mode == BoatMode.Driving);

    public static Operation? Single(IEnumerable<Operation> operations)
    {
        var distinct = operations.Distinct().ToArray();
        return distinct.Length == 1 ? distinct[0] : null;
    }
}

public sealed class PressGate
{
    private bool armed;
    public void Reset() => armed = false;
    public bool Sample(float value)
    {
        if (value <= .2f) { armed = true; return false; }
        if (!armed || value < .5f) return false;
        armed = false;
        return true;
    }
}

public readonly record struct OwnerKey(long Manager, long Player, long Boat, long Cockpit, long Root);

public sealed class OwnerEpoch
{
    private OwnerKey key;
    public long Generation { get; private set; }
    public bool Set(OwnerKey next)
    {
        if (key == next) return false;
        key = next; Generation++; return true;
    }
    public void Invalidate() { key = default; Generation++; }
    public bool Matches(OwnerKey owner, long generation) => key != default && key == owner && Generation == generation;
}

public static class BoatBindings
{
    public static readonly (Operation Op, string Pad, string Key)[] Defaults =
    {
        (Operation.Engine, "buttonNorth", "v"),
        (Operation.Dock, "buttonWest", "b"),
        (Operation.Light, "leftShoulder", "h")
    };
    public static string? Parse(string value, bool pad)
    {
        value = value.Trim();
        if (value.Length == 0) return null;
        string prefix = pad ? "<Gamepad>/" : "<Keyboard>/";
        if (!value.StartsWith('<')) value = prefix + value;
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || value.Length <= prefix.Length
            || value.IndexOfAny(new[] { '+', '*', ';', '{', '}', '\r', '\n' }) >= 0)
            throw new FormatException("Use a single exact button control path; empty disables it.");
        return value;
    }
}
