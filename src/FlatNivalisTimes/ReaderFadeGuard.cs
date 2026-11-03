using HarmonyLib;
using Nivalis;
using Nivalis.UI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NivalisNights.FlatNivalisTimes;

internal static class ReaderFadeGuard
{
    private static readonly Dictionary<IntPtr, FadeScreenUI> Screens = new();
    internal static void Initialize()
    {
        // Scene-wide lookup is intentional: FadeScreenUI is a separate root prefab,
        // not a child of the newspaper or the hidden FirstPersonHUD.
        foreach (var screen in Object.FindObjectsOfType<FadeScreenUI>(true)) Add(screen);
    }
    internal static void Add(FadeScreenUI screen) => Screens[screen.Pointer] = screen;
    internal static void Remove(FadeScreenUI screen) => Screens.Remove(screen.Pointer);
    internal static void Clear() => Screens.Clear();
    private static bool Visible(CanvasGroup? group) => PaperStates.Alive(group) && group!.gameObject.activeInHierarchy && group.alpha > .001f;
    internal static bool IsFading
    {
        get
        {
            if (TransitionManager.InstanceNotNull(out var transition) && Visible(transition.fadeCanvas)) return true;
            foreach (var screen in Screens.Values)
                if (PaperStates.Alive(screen) && screen.isActiveAndEnabled && Visible(screen.canvasGroup)) return true;
            return false;
        }
    }
}

[HarmonyPatch(typeof(FadeScreenUI), "Awake")]
internal static class ReaderFadeCreated
{
    private static void Postfix(FadeScreenUI __instance) => ReaderFadeGuard.Add(__instance);
}
[HarmonyPatch(typeof(FadeScreenUI), "OnDestroy")]
internal static class ReaderFadeDestroyed
{
    private static void Prefix(FadeScreenUI __instance) => ReaderFadeGuard.Remove(__instance);
}
