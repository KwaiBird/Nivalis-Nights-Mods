using Nivalis;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using PlayerInputManager = Nivalis.PlayerInputManager;

namespace NivalisNights.BoatControlBindings;

internal sealed class BoatInput : IDisposable
{
    internal sealed record Binding(Operation Operation, bool Pad, string Path, InputAction Action, PressGate Gate);
    private readonly List<Binding> bindings = new();
    private InputActionMap? map;
    private long managerId;
    private string stamp = "";
    private bool validated;
    internal IReadOnlyList<Binding> Bindings => bindings;

    internal bool Ensure(PlayerInputManager manager)
    {
        var devices = manager.user.pairedDevices;
        string current = string.Join(",", Enumerable.Range(0, devices.Count).Select(i => devices[i].deviceId + ":" + devices[i].layout))
            + ":" + string.Join("|", Plugin.Bindings.OrderBy(x => x.Key).Select(x => x.Value.Pad.Value + ":" + x.Value.Key.Value));
        if (map != null && managerId == manager.Pointer.ToInt64() && current == stamp) return false;
        Dispose(); stamp = current; managerId = manager.Pointer.ToInt64();
        if (devices.Count == 0) return true;
        map = new InputActionMap("BoatControlBindings");
        foreach (var (op, entries) in Plugin.Bindings)
            foreach (bool pad in new[] { false, true })
            {
                try
                {
                    string? path = BoatBindings.Parse(pad ? entries.Pad.Value : entries.Key.Value, pad);
                    if (path == null) continue;
                    var action = map.AddAction(op + (pad ? "Pad" : "Key"), InputActionType.Button, path);
                    bindings.Add(new(op, pad, path, action, new PressGate()));
                }
                catch (Exception ex) { Plugin.Logger.LogError($"{op} binding disabled: {ex.Message}"); }
            }
        map.devices = new Il2CppSystem.Nullable<UnityEngine.InputSystem.Utilities.ReadOnlyArray<InputDevice>>(devices);
        map.Enable(); validated = false;
        Validate(manager);
        return true;
    }

    private void Validate(PlayerInputManager manager)
    {
        if (validated) return;
        validated = true;
        // Allowed cross-mode uses are guarded at their native execution boundaries.
        var allowed = new HashSet<string> { "Pick Up", "PlacementMode", "Rotate", "Skip Dialogue Line", "Skip Cinematic", "Any Key" };
        var rejected = new HashSet<Binding>();
        foreach (var b in bindings)
        {
            var devices = manager.user.pairedDevices;
            bool devicePresent = Enumerable.Range(0, devices.Count).Any(i => b.Pad
                ? devices[i].TryCast<Gamepad>() != null : devices[i].TryCast<Keyboard>() != null);
            if (devicePresent && b.Action.controls.Count == 0) rejected.Add(b);
            for (int i = 0; i < b.Action.controls.Count; i++)
                if (b.Action.controls[i].TryCast<ButtonControl>() == null) rejected.Add(b);
            foreach (var native in manager.GameplayControls.actions)
                if (!allowed.Contains(native.name) && Overlap(b.Action, native)) rejected.Add(b);
        }
        for (int i = 0; i < bindings.Count; i++)
            for (int j = i + 1; j < bindings.Count; j++)
                if (string.Equals(bindings[i].Path, bindings[j].Path, StringComparison.OrdinalIgnoreCase)
                    || Overlap(bindings[i].Action, bindings[j].Action)) { rejected.Add(bindings[i]); rejected.Add(bindings[j]); }
        foreach (var b in rejected)
        {
            b.Action.Disable(); bindings.Remove(b);
            Plugin.Logger.LogError($"{b.Operation} {b.Path}: unresolved, non-button or conflicting control; disabled.");
        }
    }

    internal Binding? Find(Operation op, bool pad) => bindings.FirstOrDefault(x => x.Operation == op && x.Pad == pad && x.Action.enabled);
    internal void InvalidateBindings() { stamp = ""; Reset(); }
    internal void Reset() { foreach (var b in bindings) b.Gate.Reset(); }
    internal Operation? Poll(bool allowed)
    {
        if (!allowed) { Reset(); return null; }
        var pressed = new List<Operation>();
        foreach (var b in bindings)
            if (b.Action.enabled && b.Gate.Sample(b.Action.ReadValue<float>()))
            {
                if (b.Operation == Operation.Light) BoatLightDiagnostics.Input(b.Path);
                pressed.Add(b.Operation);
            }
        return BoatPolicy.Single(pressed);
    }
    internal bool Reserves(InputAction? native, InputControl? control = null)
    {
        if (native == null || !native.enabled) return false;
        foreach (var b in bindings)
        {
            // Native callbacks may run before this map's action state is updated. Read hardware controls directly.
            bool pressed = false;
            for (int i = 0; i < b.Action.controls.Count; i++)
                pressed |= b.Action.controls[i].TryCast<ButtonControl>()?.isPressed == true;
            if (!b.Action.enabled || !pressed) continue;
            if (control != null ? HasControl(b.Action, control) : Overlap(b.Action, native)) return true;
        }
        return false;
    }
    private static bool HasControl(InputAction action, InputControl control)
    {
        for (int i = 0; i < action.controls.Count; i++) if (action.controls[i].Pointer == control.Pointer) return true;
        return false;
    }
    private static bool Overlap(InputAction a, InputAction b)
    {
        for (int i = 0; i < a.controls.Count; i++) if (HasControl(b, a.controls[i])) return true;
        return false;
    }
    public void Dispose()
    {
        map?.Disable(); map?.Dispose(); map = null; bindings.Clear(); validated = false;
    }
}
