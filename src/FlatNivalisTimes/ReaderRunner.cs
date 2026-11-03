using HarmonyLib;
using Nivalis;
using Nivalis.Localization;
using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

namespace NivalisNights.FlatNivalisTimes;

public sealed class ReaderRunner : MonoBehaviour
{
    public ReaderRunner(IntPtr pointer) : base(pointer) { }
    internal static ReaderRunner? Current;
    internal ReaderInput? Input;
    private ReaderPage? page;
    private ReaderHud? hud;
    private PlayerHandsAnimator? hands;
    private Camera? gameCamera;
    private int language = -1;
    private bool wasReady, stopped, failed, pageLogged, runnerLogged;
    private string? waitingReason;
    internal bool CanOperate
    {
        get
        {
            try
            {
                if (stopped || failed || page == null || !PaperStates.Alive(page.Paper) || !page.Paper.FullyVisible) return false;
                if (!GameReady() || !PlayerManager.InstanceNotNull(out var manager)) return false;
                var player = manager.LocalPlayer?.Character;
                return PaperStates.Alive(page.Paper)
                    && PaperStates.Alive(hands) && !hands!.IsPlayingMealAnimation && !hands.IsHoldingTray
                    && PaperStates.Alive(player) && PaperStates.Alive(player!.PlacementSystem) && PlacementIsIdle(player.PlacementSystem)
                    && ContextReady();
            }
            catch (Exception error) { Plugin.ReportOnce("reader operation guard failed", error); return false; }
        }
    }
    internal bool Owns(PlayerHandsAnimator candidate) => CanOperate && PaperStates.Alive(hands) && candidate.Pointer == hands!.Pointer
        && PaperStates.Alive(candidate.newspaper) && candidate.newspaper.Pointer == page!.Paper.Pointer
        && (!PaperStates.Alive(candidate.poster) || !candidate.poster.Equipped);

    public void Start()
    {
        Current = this;
    }

    private static bool GameReady()
        => GameSceneManager.InstanceNotNull(out var scene) && scene.IsGame && !GameSceneManager.IsUnloadingGameplay
            && !scene.levelLoading && !scene.IsLoadingExternal && !scene.IsLoading;

    private bool ContextReady() => ContextBlockReason() == null;

    private string? ContextBlockReason()
    {
        if (!AccessibilitySettings.Enabled) return "mod-disabled";
        if (!Application.isFocused) return "application-unfocused";
        if (ApplicationState.IsQuitting) return "application-quitting";
        if (Time.timeScale <= 0) return "time-paused";
        if (ReaderFadeGuard.IsFading) return "screen-fading";
        if (!GameReady()) return "scene-not-ready";
        if (!PlayerInputManager.InstanceNotNull(out var manager)) return "input-manager-missing";
        if (!PaperStates.Alive(CursorModeManager._instance)) return "cursor-manager-missing";
        if (manager.isRebind) return "input-rebinding";
        if (manager.currentMap == null) return "input-map-missing";
        if (!manager.currentMap.enabled) return "input-map-disabled";
        if (manager.currentMap.name != "GameplayControls") return "input-map-not-gameplay";
        if (CursorModeManager.IsCursorActive) return "cursor-active";
        if (manager.PreventAllMenuShortcuts == null) return "shortcut-lock-missing";
        if (manager.PreventAllMenuShortcuts.Value && !ReaderWakeup.Allows(manager, page?.Paper, hands))
            return "all-shortcuts-locked";
        var events = EventSystem.current;
        var selected = PaperStates.Alive(events) ? events.currentSelectedGameObject : null;
        if (PaperStates.Alive(selected))
        {
            var tmp = selected!.GetComponent<TMP_InputField>();
            var legacy = selected.GetComponent<UnityEngine.UI.InputField>();
            if ((PaperStates.Alive(tmp) && tmp!.isFocused) || (PaperStates.Alive(legacy) && legacy!.isFocused)) return "text-field-focused";
        }
        // Never call IsUiShown/FirstChildCanvas: the latter can add a Canvas.
        if (!UIManager.InstanceNotNull(out var ui)) return "ui-manager-missing";
        var uiReason = UiBlockReason(ui);
        if (uiReason != null) return uiReason;
        if (!PaperStates.Alive(UIWindowManager._instance)) return "window-manager-missing";
        return UIWindowManager.OpenWindowCount == 0 ? null : "ui-window-open";
    }

    private static string? UiBlockReason(UIManager ui)
    {
        // Original FirstChildCanvas resolves exactly transform.GetChild(0),
        // but also writes its lazy cache and may AddComponent on a miss.
        // The shipped _Global scene already has a Canvas on that direct child.
        // Read that existing component without depending on the cache or creating it.
        var root = ui.transform;
        if (!PaperStates.Alive(root) || root.childCount == 0) return "ui-child-missing";
        var child = root.GetChild(0);
        if (!PaperStates.Alive(child)) return "ui-child-missing";
        var canvas = child.GetComponent<Canvas>();
        if (!PaperStates.Alive(canvas)) return "ui-child-canvas-missing";
        return canvas.enabled ? null : "ui-canvas-disabled";
    }

    private void ReportWaiting(string reason)
    {
        if (waitingReason == reason) return;
        waitingReason = reason;
        Plugin.Logger.LogInfo($"Reader controls state: {reason}; RuntimeAccepted=false.");
    }

    private bool PrepareInput(Newspaper paper)
    {
        if (Input != null) return true;
        if (!paper.FullyVisible) { ReportWaiting("paper-not-fully-visible"); return false; }
        var reason = ContextBlockReason();
        if (reason != null) { ReportWaiting(reason); return false; }
        ReaderFadeGuard.Initialize();
        // Publish before registration so rollback retains its owner.
        Input = new ReaderInput(); Input.Subscribe();
        ReportWaiting("input-subscribed");
        return true;
    }

    public void Update()
    {
        try
        {
            ReaderSettings.Flush();
            if (!runnerLogged) { Plugin.Logger.LogInfo("Reader runner updating; RuntimeAccepted=false."); runnerLogged = true; }
            if (stopped || failed) return;
            // Singleton.Instance creates missing managers in this game. Never probe it
            // while bootstrap/loading is in progress, including the title screen.
            if (!AccessibilitySettings.Enabled || !GameReady() || !PlayerManager.InstanceNotNull(out var manager))
            {
                ReportWaiting("game-or-player-not-ready");
                ReleasePage(); Input?.Dispose(); Input = null; return;
            }
            var player = manager.LocalPlayer?.Character;
            var currentHands = PaperStates.Alive(player) ? player!.Controller?.HandsAnimator : null;
            var paper = PaperStates.Alive(currentHands) ? currentHands!.newspaper : null;
            if (!AccessibilitySettings.Enabled || !PaperStates.Alive(paper) || paper!.isPoster || !paper.Equipped)
            {
                ReportWaiting("paper-not-equipped");
                ReleasePage(); return;
            }
            if (page == null || page.Paper.Pointer != paper.Pointer)
            {
                ReleasePage(); page = new ReaderPage(paper) { TextureChanged = value => hud?.SetTexture(value) }; hands = currentHands;
                language = (int)LocObjectsDB.CurrentLanguage;
            }
            if (!PrepareInput(paper)) return;
            if (!PlayerInputManager.InstanceNotNull(out var inputManager)) { ReportWaiting("input-manager-lost"); hud?.Hide(); return; }
            gameCamera = player!.Controller!.Camera;
            bool ready = CanOperate;
            if (ready != wasReady) Input!.Invalidate();
            Input!.Ensure(inputManager);
            if (ready != wasReady) { Input.Reset(); wasReady = ready; }
            if (!ready)
            {
                hud?.Hide();
                ReportWaiting(OperationBlockReason());
                return;
            }
            if (language != (int)LocObjectsDB.CurrentLanguage)
            {
                language = (int)LocObjectsDB.CurrentLanguage;
                paper.RenderCanvas(); page.Dirty = true;
            }
            if (!PaperStates.Alive(gameCamera)) { ReportWaiting("game-camera-missing"); return; }
            ReportWaiting(inputManager.PreventAllMenuShortcuts.Value ? "ready-wakeup-reading" : "ready");
            page.Draw(gameCamera!);
            if (!pageLogged && PaperStates.Alive(page.Texture))
            {
                Plugin.Logger.LogInfo($"Reader page ready: {page.Texture!.width}x{page.Texture.height}; private visual canvas/RT; RuntimeAccepted=false.");
                pageLogged = true;
            }
            hud ??= new ReaderHud();
            hud.Initialize();
            hud.Show(page, Input, inputManager);
        }
        catch (Exception error)
        {
            failed = true;
            try { Shutdown(); } catch (Exception restoreError) { Plugin.ReportOnce("reader restoration failed", restoreError); }
            Plugin.ReportOnce("reader stopped", error);
        }
    }

    internal void Rendered(Newspaper paper) { if (page?.Paper.Pointer == paper.Pointer) page.Dirty = true; }
    private void ReleasePage()
    {
        var cleanup = new CleanupBatch();
        cleanup.Attempt(() => hud?.Hide());
        cleanup.Attempt(() => Input?.Reset());
        wasReady = false;
        cleanup.Attempt(ReaderSettings.CloseView);
        cleanup.Attempt(() => { page?.Dispose(); page = null; hands = null; pageLogged = false; });
        cleanup.ThrowIfFailed();
    }
    internal bool HasOwnedResources => page != null || hud != null || Input?.HasOwnedSubscriptions == true;
    internal void Shutdown()
    {
        stopped = true;
        var cleanup = new CleanupBatch();
        cleanup.Attempt(() => { Input?.Dispose(); Input = null; });
        cleanup.Attempt(ReleasePage);
        cleanup.Attempt(() => { hud?.Dispose(); hud = null; });
        cleanup.Attempt(ReaderSettings.FlushOnShutdown);
        cleanup.Attempt(ReaderFadeGuard.Clear);
        if (!HasOwnedResources && Current == this) Current = null;
        cleanup.ThrowIfFailed();
    }
    public void OnDestroy() { try { Shutdown(); } catch (Exception error) { Plugin.ReportOnce("reader shutdown failed", error); } }
    // Diagnostic only: CanOperate remains the sole permission predicate.
    // Sample again after a rejection instead of collapsing unrelated guards into
    // paper-hands-or-placement-blocked. A transition between samples is explicit.
    private string OperationBlockReason()
    {
        try
        {
            if (stopped || failed) return "reader-stopped";
            if (page == null) return "reader-page-missing";
            if (!PaperStates.Alive(page.Paper)) return "paper-owner-lost";
            if (!page.Paper.FullyVisible) return "paper-not-fully-visible";
            if (!GameReady() || !PlayerManager.InstanceNotNull(out var manager)) return "game-or-player-not-ready";
            if (!PaperStates.Alive(hands)) return "reader-hands-missing";
            if (hands!.IsPlayingMealAnimation) return "meal-animation-active";
            if (hands.IsHoldingTray) return "tray-active";
            var player = manager.LocalPlayer?.Character;
            if (!PaperStates.Alive(player)) return "player-character-missing";
            var placement = player!.PlacementSystem;
            if (!PaperStates.Alive(placement)) return "placement-system-missing";
            if (placement!.isActiveAndEnabled && !PlacementIsIdle(placement))
                return placement.IsHoldingObject ? "placement-enabled-with-object" : "placement-enabled-without-object";
            return ContextBlockReason() ?? "operation-recheck-passed";
        }
        catch (Exception error)
        {
            Plugin.ReportOnce("reader operation diagnostic failed", error);
            return "operation-diagnostic-failed";
        }
    }

    private bool PlacementIsIdle(PlacementSystem placement)
    {
        bool enabled = placement.isActiveAndEnabled;
        bool holding = enabled && placement.IsHoldingObject;
        // Keep the original disabled-component path. An enabled but empty
        // component is permitted only inside the verified wake-up reading leases.
        bool wakeup = enabled && !holding && PlayerInputManager.InstanceNotNull(out var input)
            && ReaderWakeup.Allows(input, page?.Paper, hands);
        return WakeupLockPolicy.AllowsPlacement(enabled, holding, wakeup);
    }

}

[HarmonyPatch(typeof(Newspaper), "RenderCanvas")]
internal static class ReaderRenderedPatch
{
    private static void Postfix(Newspaper __instance) => ReaderRunner.Current?.Rendered(__instance);
}
