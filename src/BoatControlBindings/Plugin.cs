using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace NivalisNights.BoatControlBindings;

[BepInPlugin(Id, "Nivalis Nights Boat Control Bindings", "1.1.1")]
public sealed class Plugin : BasePlugin
{
    public const string Id = BoatSettings.Id;
    internal static ManualLogSource Logger = null!;
    internal static ConfigFile Settings = null!;
    internal static ConfigEntry<bool> Enabled = null!;
    internal static readonly Dictionary<Operation, (ConfigEntry<string> Pad, ConfigEntry<string> Key)> Bindings = new();
    private Harmony? harmony;
    public override void Load()
    {
        Logger = Log;
        Settings = new ConfigFile(BoatSettings.Resolve(Paths.ConfigPath), true);
        Enabled = Settings.Bind("General", "Enabled", true, "Enable boat control bindings and hints. Reloaded when inactive.");
        foreach (var (op, pad, key) in BoatBindings.Defaults)
            Bindings[op] = (Settings.Bind("Gamepad", op.ToString(), pad, "Button name (for example buttonNorth); device prefix optional; empty disables."),
                Settings.Bind("Keyboard", op.ToString(), key, "Key name (for example v); device prefix optional; empty disables."));
        harmony = new Harmony(Id);
        try { harmony.PatchAll(typeof(Plugin).Assembly); AddComponent<BoatRunner>(); }
        catch { harmony.UnpatchSelf(); throw; }
        Log.LogInfo("Boat Control Bindings 1.1.1 loaded: Y engine, X dock, LB lights; localized bottom hints only. RuntimeAccepted=false.");
    }
    public override bool Unload() { BoatRunner.Current?.Shutdown(); harmony?.UnpatchSelf(); return true; }
}
