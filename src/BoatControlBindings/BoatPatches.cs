using HarmonyLib;
using Nivalis;
using Nivalis.Boat;
using Nivalis.UI;
using UnityEngine.InputSystem;

namespace NivalisNights.BoatControlBindings;

[HarmonyPatch(typeof(PlayerObjectHolder), "TryPickupObjectInFront")]
internal static class PickupPatch
{
    static bool Prefix(PlayerObjectHolder __instance) => BoatRunner.Current?.SuppressPickup(__instance) != true;
}
[HarmonyPatch(typeof(PlacementModeUi), "OnPlacementMode_Context")]
internal static class PlacementPatch
{
    static bool Prefix(InputAction.CallbackContext ctx) => BoatRunner.Current?.SuppressPlacement(ctx) != true;
}
[HarmonyPatch(typeof(DiegeticButton), "DoInteraction")]
internal static class ButtonPatch
{
    static void Prefix(DiegeticButton __instance) => BoatRunner.Current?.OriginalInteraction(__instance);
}
[HarmonyPatch(typeof(BoatCockpit), "Enter")]
internal static class EnterPatch
{
    static void Prefix(BoatCockpit __instance) => BoatRunner.Current?.CockpitChanged(__instance);
}
[HarmonyPatch(typeof(BoatCockpit), "Exit")]
internal static class ExitPatch
{
    static void Prefix(BoatCockpit __instance) => BoatRunner.Current?.CockpitChanged(__instance);
}
[HarmonyPatch(typeof(BoatCockpit), "ExitOrDock")]
internal static class StopPatch
{
    static bool Prefix(BoatCockpit __instance) => BoatRunner.Current?.GuardCallback(__instance, Operation.Engine) != false;
}
[HarmonyPatch(typeof(BoatCockpit), "Dock")]
internal static class DockPatch
{
    static bool Prefix(BoatCockpit __instance) => BoatRunner.Current?.GuardCallback(__instance, Operation.Dock) != false;
}
[HarmonyPatch(typeof(BoatCockpit), "ToggleLight")]
internal static class LightPatch
{
    static bool Prefix(BoatCockpit __instance) => BoatRunner.Current?.GuardCallback(__instance, Operation.Light) != false;
    // Harmony postfix also runs after a rejected prefix; the preceding allowed flag is authoritative.
    static void Postfix(BoatCockpit __instance) => BoatLightDiagnostics.Write("callback exit", __instance);
}
[HarmonyPatch(typeof(GameSceneManager), "LoadArea")]
internal static class ScenePatch
{
    static void Prefix() => BoatRunner.Current?.Boundary();
}
