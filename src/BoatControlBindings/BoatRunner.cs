using Il2CppInterop.Runtime.Attributes;
using Nivalis;
using Nivalis.Boat;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;
using static NivalisNights.BoatControlBindings.BoatContext;
using PlayerInputManager = Nivalis.PlayerInputManager;

namespace NivalisNights.BoatControlBindings;

public sealed class BoatRunner : MonoBehaviour
{
    public BoatRunner(IntPtr pointer) : base(pointer) { }
    internal static BoatRunner? Current;
    private readonly BoatInput input = new();
    private readonly OwnerEpoch epoch = new();
    private CrosshairUI? crosshair;
    private BoatHints? hints;
    private BoatContext? context;
    private Pending? pending;
    private bool dispatching, failed, stopped, hintsFailed;
    private int originalFrame = -1, boundaryFrame = -1;
    private float nextSearch, nextSettings;
    private long settingsStamp;
    private BoatMode previousMode;
    private string reported = "";

    private sealed class Pending
    {
        internal BoatContext Context = null!;
        internal DiegeticButton Button = null!;
        internal Coroutine? Sequence;
        internal long Generation;
        internal string Devices = "";
        internal bool Committed, Rejected;
    }

    public void Start() { Current = this; }
    public void Update()
    {
        try
        {
            if (failed || stopped) return;
            if (Time.unscaledTime >= nextSettings)
            {
                nextSettings = Time.unscaledTime + 1;
                long stamp = File.GetLastWriteTimeUtc(Plugin.Settings.ConfigFilePath).Ticks;
                if (stamp != settingsStamp)
                {
                    Plugin.Settings.Reload(); settingsStamp = stamp; Boundary(); input.Dispose();
                }
            }
            if ((!Alive(crosshair) || !crosshair!.isActiveAndEnabled) && Time.unscaledTime >= nextSearch)
            {
                nextSearch = Time.unscaledTime + .5f;
                crosshair = Object.FindObjectOfType<CrosshairUI>();
            }
            Refresh();
            if (context != null && input.Ensure(context.Manager))
            {
                CancelPending();
                input.Reset(); boundaryFrame = Time.frameCount;
            }
        }
        catch (Exception ex) { Fail(ex); }
    }

    public void LateUpdate()
    {
        try
        {
            if (failed || stopped) return;
            Refresh();
            bool allowed = context != null && !context.Busy && pending == null
                && Time.frameCount > boundaryFrame && originalFrame != Time.frameCount;
            BoatLightDiagnostics.Availability(context, allowed, pending != null,
                originalFrame == Time.frameCount, Time.frameCount <= boundaryFrame);
            var operation = input.Poll(allowed);
            if (operation.HasValue && context != null && BoatPolicy.Visible(operation.Value, context.Mode, context.CanDock, context.Busy))
                Execute(context, operation.Value);
            Refresh();
            UpdateHints();
        }
        catch (Exception ex) { Fail(ex); }
    }

    // Only Unity lifecycle entry points need native registration; helpers stay managed.
    [HideFromIl2Cpp]
    private void Refresh()
    {
        MaintainPending();
        var next = BoatContext.Read(crosshair, pending?.Button);
        // A committed stop legitimately removes the driver before the native hand-return animation ends.
        if (next == null && pending?.Committed == true)
            next = pending.Context with { Mode = BoatMode.Unavailable, CanDock = false, Busy = true };
        if (next == null)
        {
            if (context != null) { epoch.Invalidate(); input.InvalidateBindings(); boundaryFrame = Time.frameCount; }
            context = null; previousMode = BoatMode.Unavailable;
            hints?.Hide();
            return;
        }
        if (epoch.Set(next.Key))
        {
            CancelPending(); input.Reset(); boundaryFrame = Time.frameCount;
            hints?.Dispose(); hints = null;
            hintsFailed = false;
        }
        if (previousMode != next.Mode)
        {
            previousMode = next.Mode; input.Reset(); boundaryFrame = Time.frameCount;
            string info = next.Mode + ":" + next.Boat.Pointer;
            if (info != reported) { reported = info; Plugin.Logger.LogInfo($"Boat context: {next.Mode}; RuntimeAccepted=false."); }
        }
        context = next;
    }

    [HideFromIl2Cpp]
    private void Execute(BoatContext owner, Operation operation)
    {
        var live = BoatContext.Read(crosshair);
        if (live == null || live.Key != owner.Key || live.Busy || !BoatPolicy.Visible(operation, live.Mode, live.CanDock, live.Busy)) return;
        dispatching = true;
        try
        {
            if (operation == Operation.Engine && live.Mode == BoatMode.Ready)
            {
                if (live.Cockpit.CanInteract()) live.Cockpit.DoInteraction();
                boundaryFrame = Time.frameCount; input.Reset();
                return;
            }
            var button = operation == Operation.Engine ? live.Cockpit.exitButton : operation == Operation.Dock ? live.Cockpit.dockButton : live.Cockpit.lightButton;
            if (operation == Operation.Light) BoatLightDiagnostics.Write("request", live.Cockpit);
            if (!ButtonUsable(button) || button.sequence != null) return;
            pending = new Pending { Context = live, Button = button, Generation = epoch.Generation, Devices = BoatContext.Devices(live.Manager) };
            button.DoInteraction();
            if (pending != null) pending.Sequence = button.sequence;
        }
        finally { dispatching = false; }
    }

    [HideFromIl2Cpp]
    private void MaintainPending()
    {
        if (pending == null) return;
        var p = pending;
        if (!Alive(p.Button) || p.Button.sequence == null) { pending = null; input.Reset(); return; }
        if (p.Sequence != null && !Same(p.Sequence, p.Button.sequence)) { pending = null; input.Reset(); return; }
        bool valid = !p.Rejected && (p.Committed || epoch.Matches(p.Context.Key, p.Generation)) && Common(p.Context.Manager)
            && p.Devices == BoatContext.Devices(p.Context.Manager)
            && PlayerManager.InstanceNotNull(out var players) && Same(players.LocalPlayer?.Character, p.Context.Player)
            && Same(BoatController.Instance, p.Context.Boat) && Alive(p.Context.Canvas) && Alive(p.Context.Crosshair)
            && p.Context.Crosshair.isActiveAndEnabled;
        if (!p.Committed)
        {
            var live = valid ? BoatContext.Read(crosshair, p.Button) : null;
            valid &= live != null && live.Key == p.Context.Key && live.Mode == BoatMode.Driving;
        }
        if (!valid) CancelPending();
    }

    [HideFromIl2Cpp]
    private void CancelPending()
    {
        var p = pending; pending = null; input.Reset();
        if (p == null || !Alive(p.Button) || p.Button.sequence == null) return;
        if (p.Sequence != null && !Same(p.Sequence, p.Button.sequence)) return;
        try { p.Button.StopCurrentInteraciton(); }
        catch (Exception ex) { Plugin.Logger.LogWarning($"Boat sequence cleanup: {ex.Message}"); }
    }

    [HideFromIl2Cpp]
    internal bool GuardCallback(BoatCockpit cockpit, Operation operation)
    {
        try
        {
            bool allowed = GuardCallbackCore(cockpit, operation);
            if (operation == Operation.Light) BoatLightDiagnostics.Write("callback allowed=" + allowed, cockpit);
            return allowed;
        }
        catch (Exception ex) { if (pending != null) pending.Rejected = true; Plugin.Logger.LogError($"Boat delayed callback rejected: {ex}"); return false; }
    }

    [HideFromIl2Cpp]
    private bool GuardCallbackCore(BoatCockpit cockpit, Operation operation)
    {
        var p = pending;
        if (p == null || !Same(p.Context.Cockpit, cockpit)) return true; // Preserve original interactions.
        var expected = operation == Operation.Engine ? cockpit.exitButton : operation == Operation.Dock ? cockpit.dockButton : cockpit.lightButton;
        if (!Same(p.Button, expected)) return true;
        var live = BoatContext.Read(crosshair, p.Button);
        bool valid = !p.Rejected && !p.Committed && (p.Sequence == null || Same(p.Sequence, p.Button.sequence))
            && epoch.Matches(p.Context.Key, p.Generation)
            && p.Devices == BoatContext.Devices(p.Context.Manager)
            && live != null && live.Key == p.Context.Key && live.Mode == BoatMode.Driving
            && ButtonUsable(p.Button, false) && (operation != Operation.Dock || Alive(live.Boat.dockInRange));
        if (!valid) { p.Rejected = true; return false; } // Cancel safely after the current coroutine step.
        p.Committed = true; return true;
    }

    [HideFromIl2Cpp]
    internal void OriginalInteraction(DiegeticButton button)
    {
        try
        {
            if (dispatching) return;
            var boat = BoatController.Instance;
            if (!Alive(boat) || !Alive(boat.Cockpit)) return;
            var c = boat.Cockpit;
            if (!Same(button, c.exitButton) && !Same(button, c.dockButton) && !Same(button, c.lightButton)) return;
            originalFrame = Time.frameCount; CancelPending(); input.Reset();
        }
        catch (Exception ex) { Fail(ex); }
    }
    [HideFromIl2Cpp]
    internal void CockpitChanged(BoatCockpit cockpit)
    {
        try
        {
            if (dispatching || pending?.Committed == true) return;
            if (!Same(BoatController.Instance?.Cockpit, cockpit)) return;
            originalFrame = Time.frameCount; CancelPending(); input.Reset();
        }
        catch (Exception ex) { Fail(ex); }
    }

    [HideFromIl2Cpp]
    internal bool SuppressPickup(PlayerObjectHolder holder)
    {
        try
        {
            var live = BoatContext.Read(crosshair, pending?.Button);
            return live != null && Same(live.Player.ObjectHolder, holder)
                && input.Reserves(live.Manager.GameplayControls.FindAction("Pick Up", false));
        }
        catch (Exception ex) { Fail(ex); return false; }
    }
    [HideFromIl2Cpp]
    internal bool SuppressPlacement(InputAction.CallbackContext ctx)
    {
        try
        {
            var live = BoatContext.Read(crosshair, pending?.Button);
            return live != null && input.Reserves(live.Manager.GameplayControls.FindAction("PlacementMode", false), ctx.control);
        }
        catch (Exception ex) { Fail(ex); return false; }
    }

    [HideFromIl2Cpp]
    private void UpdateHints()
    {
        if (context == null || hintsFailed) { hints?.Hide(); return; }
        if (hints == null)
        {
            try { hints = new BoatHints(context); }
            catch (Exception ex) { HideFailedHints(ex); return; }
        }
        try { hints.Update(context, input, pending != null); }
        catch (Exception ex) { HideFailedHints(ex); }
    }
    [HideFromIl2Cpp]
    private void HideFailedHints(Exception ex)
    {
        if (!hintsFailed) Plugin.Logger.LogWarning($"Boat hints hidden until reconnect: {ex}");
        hintsFailed = true; hints?.Hide();
    }
    [HideFromIl2Cpp]
    internal void Boundary()
    {
        CancelPending(); epoch.Invalidate(); context = null; previousMode = BoatMode.Unavailable;
        input.InvalidateBindings();
        try { hints?.Dispose(); }
        catch (Exception ex) { Plugin.Logger.LogWarning($"Boat hint cleanup: {ex.Message}"); }
        hints = null; hintsFailed = false; boundaryFrame = Time.frameCount;
    }
    [HideFromIl2Cpp]
    private void Fail(Exception ex)
    {
        if (!failed) Plugin.Logger.LogError($"Boat shortcuts disabled after error: {ex}");
        failed = true; Shutdown();
    }
    [HideFromIl2Cpp]
    public void Shutdown()
    {
        if (stopped) return;
        stopped = true;
        try { Boundary(); }
        finally
        {
            try { input.Dispose(); }
            catch (Exception ex) { Plugin.Logger.LogWarning($"Boat input cleanup: {ex.Message}"); }
            if (ReferenceEquals(Current, this)) Current = null;
        }
    }
    public void OnDisable() => Shutdown();
    public void OnDestroy() => Shutdown();
}
