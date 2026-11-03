using Nivalis;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Nivalis.Boat;
using Object = UnityEngine.Object;

namespace NivalisNights.BoatControlBindings;

internal sealed record BoatContext(PlayerInputManager Manager, PlayerCharacter Player, BoatController Boat,
    BoatCockpit Cockpit, CrosshairUI Crosshair, Canvas Canvas, BoatMode Mode, bool CanDock, bool Busy)
{
    internal OwnerKey Key => new(Manager.Pointer.ToInt64(), Player.Pointer.ToInt64(), Boat.Pointer.ToInt64(),
        Cockpit.Pointer.ToInt64(), Canvas.Pointer.ToInt64());
    internal static string Devices(PlayerInputManager manager)
    {
        var devices = manager.user.pairedDevices;
        return string.Join(",", Enumerable.Range(0, devices.Count).Select(i => devices[i].deviceId + ":" + devices[i].layout));
    }
    internal static bool Alive(Object? o) => o != null && o;
    internal static bool Same(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase? a, Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase? b)
        => a != null && b != null && a.Pointer != IntPtr.Zero && a.Pointer == b.Pointer;

    internal static bool Common(PlayerInputManager? manager)
    {
        if (!GameReady() || !Application.isFocused || ApplicationState.IsQuitting || Time.timeScale <= 0
            || !Alive(manager) || manager!.isRebind || manager.currentMap == null
            || manager.currentMap.name != "GameplayControls" || !manager.currentMap.enabled
            || CursorModeManager.IsCursorActive) return false;
        if (manager.PreventAllMenuShortcuts?.Value == true || manager.PreventGameplayMenuShortcuts?.Value == true) return false;
        if (!UIManager.InstanceNotNull(out var ui)) return false;
        // IsUiShown calls FirstChildCanvas, which may add a Canvas. Probe only existing UI.
        var canvas = ui.firstChildCanvas;
        if (!Alive(canvas))
        {
            var root = ui.transform;
            if (root.childCount == 0) return false;
            canvas = root.GetChild(0).GetComponent<Canvas>();
        }
        var windows = UIWindowManager._instance;
        if (!Alive(canvas) || !canvas!.enabled || !Alive(windows) || windows!._openWindows == null || windows._openWindows.Count != 0) return false;
        var selection = EventSystem.current?.currentSelectedGameObject;
        if (Alive(selection) && (selection!.GetComponent<TMPro.TMP_InputField>()?.isFocused == true
            || selection.GetComponent<InputField>()?.isFocused == true)) return false;
        return true;
    }

    internal static BoatContext? Read(CrosshairUI? crosshair, DiegeticButton? ownPending = null)
    {
        if (!GameReady() || !Alive(crosshair) || !crosshair!.isActiveAndEnabled
            || !PlayerInputManager.InstanceNotNull(out var manager) || !Common(manager)) return null;
        var canvas = crosshair.GetComponentInParent<Canvas>();
        if (!Alive(canvas)) return null;
        canvas = canvas.rootCanvas;
        if (!Alive(canvas) || !canvas.isActiveAndEnabled) return null;
        if (!PlayerManager.InstanceNotNull(out var players)) return null;
        var player = players.LocalPlayer?.Character;
        if (!Alive(player) || !Alive(player!.Controller) || !Alive(player.Interaction)
            || !Alive(player.ObjectHolder) || player.ObjectHolder.IsHoldingObject
            || (Alive(player.PlacementSystem) && player.PlacementSystem.IsHoldingObject)) return null;
        var boat = BoatController.Instance;
        if (!Alive(boat) || !Alive(boat!.Cockpit)) return null;
        var cockpit = boat.Cockpit;
        if (!cockpit.isActiveAndEnabled || !cockpit.IsActive) return null;
        var interaction = player.Interaction._interactionActive;
        // The native button owns one interaction lock during its hand animation. Other owners still block it.
        if (interaction == null) return null;
        if (!interaction.Value && !(Alive(ownPending) && interaction._overrideOwners != null
                && interaction._overrideOwners.Count == 1 && interaction._overrideOwners.Contains(ownPending!))) return null;
        var hands = player.Controller.HandsAnimator;
        if (Alive(hands) && (hands.IsNewspaperEquipped || hands.IsPlayingMealAnimation || hands.IsHoldingTray)) return null;
        BoatMode mode;
        if (cockpit.Occupied && Same(cockpit.Player, player) && Same(player.DrivenBoat, boat)) mode = BoatMode.Driving;
        else if (!cockpit.Occupied && !Alive(player.DrivenBoat) && Alive(cockpit.boatInterior)
            && cockpit.boatInterior.Entered && cockpit.CanInteract() && NearConsole(player, cockpit)) mode = BoatMode.Ready;
        else return null;
        bool busy = false;
        foreach (var button in new[] { cockpit.exitButton, cockpit.dockButton, cockpit.lightButton })
            busy |= Alive(button) && button.sequence != null;
        return new(manager, player, boat, cockpit, crosshair, canvas, mode,
            mode == BoatMode.Driving && ButtonUsable(cockpit.dockButton, ownPending == null), busy);
    }

    private static bool GameReady() => Plugin.Enabled.Value && GameSceneManager.InstanceNotNull(out var scene)
        && scene.IsGame && !GameSceneManager.IsUnloadingGameplay
        && !scene.levelLoading && !scene.IsLoadingExternal && !scene.IsLoading;

    internal static bool ButtonUsable(DiegeticButton? button, bool checkInteraction = true) => Alive(button)
        && button!.isActiveAndEnabled && button.IsActive && (!checkInteraction || button.CanInteract());

    private static bool NearConsole(PlayerCharacter player, BoatCockpit cockpit)
    {
        var camera = player.Controller.Camera;
        var raycaster = player.Interaction.Raycaster;
        if (!Alive(camera) || !Alive(raycaster) || cockpit.interactionColliders == null) return false;
        Vector3 origin = camera.transform.position;
        float range = raycaster.focusDistance;
        if (!(range > 0 && float.IsFinite(range))) return false;
        foreach (var collider in cockpit.interactionColliders)
        {
            if (!Alive(collider) || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
            Vector3 target = collider.ClosestPoint(origin);
            float distance = Vector3.Distance(origin, target);
            if (distance > range) continue;
            bool blocked = false;
            if (distance > .01f)
                foreach (var hit in Physics.RaycastAll(origin, (target - origin).normalized, distance, ~0, QueryTriggerInteraction.Ignore))
                {
                    var hitCollider = hit.collider;
                    if (!Alive(hitCollider) || Same(hitCollider, collider) || hitCollider.transform.IsChildOf(player.transform)
                        || hitCollider.transform.IsChildOf(cockpit.transform)) continue;
                    blocked = true; break;
                }
            if (!blocked) return true;
        }
        return false;
    }
}
