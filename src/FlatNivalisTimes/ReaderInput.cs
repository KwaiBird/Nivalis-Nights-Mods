using HarmonyLib;
using Nivalis;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using PlayerInputManager = Nivalis.PlayerInputManager;

namespace NivalisNights.FlatNivalisTimes;

internal sealed class ReaderInput : IDisposable
{
    internal sealed record Binding(ReaderCommand Command, bool Pad, ButtonControl Control, string Name);
    private sealed record Device(InputDevice Input, ReaderPressPolicy Policy, Dictionary<long, ReaderCommand> Bindings);
    private readonly Dictionary<int, Device> devices = new();
    internal readonly List<Binding> Bindings = new();
    private readonly Il2CppSystem.Action<InputDevice, InputEventPtr> changed;
    private readonly Il2CppSystem.Action afterUpdate;
    private string signature = "";
    private readonly ReaderEventToken token = new();
    private readonly SubscriptionPair subscriptions;
    internal bool HasOwnedSubscriptions => subscriptions.HasOwnedSubscriptions;
    internal static bool CloseScope;

    internal ReaderInput()
    {
        changed = (Action<InputDevice, InputEventPtr>)Changed;
        afterUpdate = (Action)ClearEvent;
        subscriptions = new(() => InputState.add_onChange(changed), () => InputState.remove_onChange(changed),
            () => InputSystem.add_onAfterUpdate(afterUpdate), () => InputSystem.remove_onAfterUpdate(afterUpdate));
    }

    internal void Subscribe() => subscriptions.Subscribe();

    internal void Ensure(PlayerInputManager manager)
    {
        var paired = manager.user.pairedDevices;
        string stamp = manager.Pointer + ":" + string.Join(",", Enumerable.Range(0, paired.Count).Select(i => paired[i].deviceId))
            + ":" + string.Join("|", ReaderSettings.Bindings.Select(b => b.Value.Key.Value + ":" + b.Value.Pad.Value));
        if (stamp == signature) return;
        signature = stamp; devices.Clear(); Bindings.Clear(); ClearEvent();
        for (int i = 0; i < paired.Count; i++)
        {
            var device = paired[i];
            bool pad = device.TryCast<Gamepad>() != null;
            if (!pad && device.TryCast<Keyboard>() == null) continue;
            var map = new Dictionary<long, ReaderCommand>();
            var duplicates = new HashSet<long>();
            foreach (var (command, entries) in ReaderSettings.Bindings)
            {
                string name = (pad ? entries.Pad.Value : entries.Key.Value).Trim();
                if (name.Length == 0) continue;
                if (name.Contains('/') || name.Contains('<') || name.Contains('+')) { Plugin.Logger.LogWarning($"Reading {command}: use a single short control name."); continue; }
                ButtonControl? match = null;
                foreach (var control in device.allControls)
                    if (string.Equals(control.name, name, StringComparison.OrdinalIgnoreCase)) { match = control.TryCast<ButtonControl>(); break; }
                if (match == null || match.synthetic || !Allowed(manager, match)) { Plugin.Logger.LogWarning($"Reading {command} {name}: missing, synthetic or conflicting control; disabled."); continue; }
                long id = match.Pointer.ToInt64();
                if (duplicates.Contains(id)) continue;
                if (map.ContainsKey(id))
                {
                    duplicates.Add(id);
                    map.Remove(id); Bindings.RemoveAll(b => b.Control.Pointer == match.Pointer);
                    Plugin.Logger.LogWarning($"Duplicate reading binding {name}; disabled."); continue;
                }
                map[id] = command; Bindings.Add(new(command, pad, match, name));
            }
            var policy = new ReaderPressPolicy(); policy.Seed(Held(device));
            devices[device.deviceId] = new(device, policy, map);
        }
    }

    private static bool Allowed(PlayerInputManager manager, ButtonControl control)
    {
        // These gameplay actions are disabled by the newspaper's existing player/menu locks.
        // AnyKey closes are handled only at TryUnequipNewspaper, inside their exact callback scope.
        var allowed = new HashSet<string> { "Any Key", "Pick Up", "PlacementMode", "Rotate" };
        foreach (var action in manager.GameplayControls.actions)
            foreach (var native in action.controls)
                if (native.Pointer == control.Pointer && !allowed.Contains(action.name)) return false;
        return true;
    }

    private static IEnumerable<long> Held(InputDevice device)
    {
        foreach (var control in device.allControls)
        {
            if (control.TryCast<AnyKeyControl>() != null) continue;
            var button = control.TryCast<ButtonControl>();
            if (button?.isPressed == true) yield return button.Pointer.ToInt64();
        }
    }

    private void Changed(InputDevice device, InputEventPtr evt)
    {
        ClearEvent();
        if (subscriptions.Stopped) return;
        try
        {
            if (!devices.TryGetValue(device.deviceId, out var entry)) return;
            bool allowed = evt.valid && ReaderRunner.Current?.CanOperate == true;
            var command = entry.Policy.Sample(Held(device), entry.Bindings, allowed);
            if (command == ReaderCommand.None) return;
            ReaderSettings.Execute(command); // Small in-memory changes only; render and disk I/O run in Update.
            // Native FireStateChangeNotifications subtracts the runtime time offset before
            // invoking monitors. CallbackContext.time therefore matches event.time, not internalTime.
            token.Set(device.deviceId, evt.time);
        }
        catch (Exception error) { ClearEvent(); Plugin.ReportOnce("reading input rejected", error); }
    }

    internal bool Reserves(InputAction.CallbackContext context)
        => context.control?.device != null && token.Matches(context.control.device.deviceId, context.time, context.started)
            && ReaderRunner.Current?.CanOperate == true;

    internal void Reset()
    {
        ClearEvent();
        foreach (var device in devices.Values) device.Policy.Seed(Held(device.Input));
    }
    internal void Invalidate() => signature = "";
    private void ClearEvent() => token.Clear();
    public void Dispose()
    {
        // Inert immediately, including a callback whose native unsubscribe fails.
        devices.Clear(); Bindings.Clear(); CloseScope = false;
        ClearEvent();
        subscriptions.Dispose();
    }
}

[HarmonyPatch(typeof(PlayerInputManager), "AnyKeyListener")]
internal static class ReaderAnyKeyScope
{
    private static void Prefix(InputAction.CallbackContext __0, out bool __state)
    {
        __state = ReaderInput.CloseScope;
        ReaderInput.CloseScope = false;
        try { ReaderInput.CloseScope = ReaderRunner.Current?.Input?.Reserves(__0) == true; }
        catch (Exception error) { Plugin.ReportOnce("reader close classification rejected", error); }
    }
    private static Exception? Finalizer(Exception? __exception, bool __state) { ReaderInput.CloseScope = __state; return __exception; }
}

[HarmonyPatch(typeof(PlayerHandsAnimator), "TryUnequipNewspaper")]
internal static class ReaderClosePatch
{
    private static bool Prefix(PlayerHandsAnimator __instance)
        => !(ReaderInput.CloseScope && ReaderRunner.Current?.Owns(__instance) == true);
}
