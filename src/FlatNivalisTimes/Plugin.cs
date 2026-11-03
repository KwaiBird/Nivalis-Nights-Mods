using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using MagicaCloth;
using Nivalis;

namespace NivalisNights.FlatNivalisTimes;

[BepInPlugin(Id, Name, Version)]
public sealed class Plugin : BasePlugin
{
    public const string Id = "NivalisNights.FlatNivalisTimes";
    public const string Name = "Flat Nivalis Times";
    public const string Subtitle = "Newspaper Readability Mod";
    public const string Version = "1.1.14";
    internal static BepInEx.Logging.ManualLogSource Logger = null!;
    private Harmony? harmony;
    private static readonly HashSet<string> Reported = new();

    public override void Load()
    {
        Logger = Log;
        AccessibilitySettings.Initialize(Config);
        ReaderSettings.Initialize(Config);
        harmony = new Harmony(Id);
        try { harmony.PatchAll(typeof(Plugin).Assembly); AddComponent<ReaderRunner>(); }
        catch { harmony.UnpatchSelf(); throw; }
        Log.LogInfo($"{Name} {Version} loaded. {Subtitle}. Reading controls V/B/H (RB/LB/X); mod config only. Enabled={AccessibilitySettings.Enabled}. RuntimeAccepted=false.");
    }

    public override bool Unload()
    {
        var cleanup = new CleanupBatch();
        cleanup.Attempt(() => ReaderRunner.Current?.Shutdown());
        cleanup.Attempt(SettingsUi.Shutdown);
        try { cleanup.ThrowIfFailed(); }
        catch (Exception error) { ReportOnce("unload cleanup retained", error); return false; }
        // Keep boundary patches while any native owner still requires restoration.
        if (PaperStates.HasOwnedResources || ReadingGrip.HasOwnedState || ReaderRunner.Current?.HasOwnedResources == true) return false;
        harmony?.UnpatchSelf();
        return true;
    }

    internal static void ReportOnce(string operation, Exception error)
    {
        if (Reported.Add(operation)) Logger.LogWarning($"{Name} {operation}: {error}");
    }
}

[HarmonyPatch(typeof(PlayerHandsAnimator), nameof(PlayerHandsAnimator.ManualUpdate))]
internal static class ReadingManualUpdatePatch
{
    private static void Prefix(PlayerHandsAnimator __instance)
    {
        try { ReadingGrip.Begin(__instance); }
        catch (Exception error) { ReadingGrip.Reject(error); }
    }
    private static void Postfix()
    {
        try { ReadingGrip.EndManual(); }
        catch (Exception error) { ReadingGrip.Reject(error); }
    }
    private static Exception? Finalizer(Exception? __exception)
    {
        ReadingGrip.EndScope();
        return __exception;
    }
}

[HarmonyPatch(typeof(AnimatorExtensions), nameof(AnimatorExtensions.SafeUpdate))]
internal static class ReadingIdlePatch
{
    private static void Prefix(UnityEngine.Animator animator, ref float deltaTime)
    {
        try { ReadingGrip.FreezeIdle(animator, ref deltaTime); }
        catch (Exception error) { ReadingGrip.Reject(error); }
    }
}

[HarmonyPatch(typeof(PlayerCharacter), "Update")]
internal static class ReadingAfterIKPatch
{
    private static void Postfix(PlayerCharacter __instance)
    {
        try { ReadingGrip.AfterPlayerUpdate(__instance); }
        catch (Exception error) { ReadingGrip.Reject(error); }
    }
}

[HarmonyPatch(typeof(Newspaper), nameof(Newspaper.UpdateAnimation))]
internal static class NewspaperAnimationPatch
{
    private static void Postfix(Newspaper __instance)
    {
        try { PaperStates.UpdateAnimation(__instance); }
        catch (Exception error) { Plugin.ReportOnce("animation skipped", error); }
    }
}

// Original OnPostUpdate is the last action in DefaultUpdate for synchronous mode,
// and in AfterRendering after completion/swap/sync for delayed mode. Patch those
// exits rather than changing a shared UnityEvent's listeners or using arbitrary LateUpdate.
[HarmonyPatch(typeof(MagicaPhysicsManager), "DefaultUpdate")]
internal static class SynchronousClothBoundaryPatch
{
    private static void Postfix(MagicaPhysicsManager __instance)
    {
        try { if (!__instance.useDelay) PaperStates.CompleteBoundary(__instance); }
        catch (Exception error) { Plugin.ReportOnce("synchronous boundary skipped", error); }
    }
}

[HarmonyPatch(typeof(MagicaPhysicsManager), "AfterRendering")]
internal static class DelayedClothBoundaryPatch
{
    private static void Postfix(MagicaPhysicsManager __instance)
    {
        try { if (__instance.useDelay) PaperStates.CompleteBoundary(__instance); }
        catch (Exception error) { Plugin.ReportOnce("delayed boundary skipped", error); }
    }
}
