using Nivalis;
using Nivalis.Boat;
using UnityEngine;
using static NivalisNights.BoatControlBindings.BoatContext;

namespace NivalisNights.BoatControlBindings;

// Managed, read-only observations. No hooks, subscriptions, input or Unity resources are owned here.
internal static class BoatLightDiagnostics
{
    private static string previousAvailability = "";

    internal static void Availability(BoatContext? context, bool allowed, bool pending, bool originalFrame, bool boundaryFrame)
    {
        try
        {
            if (context == null)
            {
                previousAvailability = "";
                return;
            }
            bool eligible = BoatPolicy.Visible(Operation.Light, context.Mode, context.CanDock, context.Busy);
            string state = $"mode={context.Mode}; allowed={allowed}; lightEligible={eligible}; busy={context.Busy}; pending={pending}; originalFrame={originalFrame}; boundaryFrame={boundaryFrame}";
            string stamp = context.Key + ":" + state;
            if (stamp == previousAvailability) return;
            previousAvailability = stamp;
            Write("availability " + state, context.Cockpit);
        }
        catch { /* Diagnostics must never affect original behavior. */ }
    }

    internal static void Input(string path)
    {
        try { Plugin.Logger.LogInfo($"Boat light input: {path}; frame={Time.frameCount}."); }
        catch { }
    }

    internal static void Write(string stage, BoatCockpit cockpit)
    {
        try
        {
            // The native ToggleLight hook can also run during restoration. Observe only a live gameplay owner.
            if (BoatRunner.Current == null || !Alive(cockpit) || !GameSceneManager.InstanceNotNull(out var scene)
                || !scene.IsGame || GameSceneManager.IsUnloadingGameplay || scene.levelLoading
                || scene.IsLoadingExternal || scene.IsLoading) return;
            var button = cockpit.lightButton;
            bool active = Alive(button) && button!.isActiveAndEnabled && button.IsActive;
            bool sequence = Alive(button) && button!.sequence != null;
            int enabled = 0, total = 0;
            var lights = cockpit.boatLights;
            if (lights != null)
                for (int i = 0; i < lights.Length; i++)
                {
                    var light = lights[i];
                    if (!Alive(light)) continue;
                    total++;
                    if (light.enabled) enabled++;
                }
            Plugin.Logger.LogInfo($"Boat light {stage}: lightsEnabled={cockpit.lightsEnabled}; nativeLights={enabled}/{total}; buttonActive={active}; sequence={sequence}; frame={Time.frameCount}.");
        }
        catch { /* Failed observation is not permission to change or retry a game action. */ }
    }
}
