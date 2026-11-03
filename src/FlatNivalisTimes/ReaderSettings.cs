using BepInEx.Configuration;

namespace NivalisNights.FlatNivalisTimes;

internal static class ReaderSettings
{
    private static ConfigFile file = null!;
    private static ConfigEntry<PaperBackground> background = null!;
    private static ConfigEntry<bool> contrast = null!;
    internal static PaperBackground Background => background.Value;
    internal static bool Contrast => contrast.Value;
    internal static bool Expanded { get; private set; }
    internal static readonly Dictionary<ReaderCommand, (ConfigEntry<string> Key, ConfigEntry<string> Pad)> Bindings = new();
    private static bool dirty;
    private static long retryAt;

    internal static void Initialize(ConfigFile config)
    {
        file = config;
        file.SaveOnConfigSet = false;
        background = file.Bind("Reading", "Background", PaperBackground.Original, "Paper background: Original, Faded or Plain. Changed live while reading; mod config only.");
        contrast = file.Bind("Reading", "HighContrast", false, "Use dark text and lighter panels while reading; mod config only.");
        if (!Enum.IsDefined(typeof(PaperBackground), background.Value)) background.Value = PaperBackground.Original;
        foreach (var (command, key, pad) in new[] { (ReaderCommand.View, "V", "rightShoulder"), (ReaderCommand.Background, "B", "leftShoulder"), (ReaderCommand.Contrast, "H", "buttonWest") })
            Bindings[command] = (file.Bind("Keyboard", command.ToString(), key, "Single key name, case insensitive; empty disables. No device prefix or combinations."),
                file.Bind("Gamepad", command.ToString(), pad, "Single button name, case insensitive; empty disables. Only available while reading."));
        dirty = true; retryAt = 0;
        Flush();
    }

    internal static void Execute(ReaderCommand command)
    {
        if ((command & ReaderCommand.Background) != 0) { background.Value = (PaperBackground)(((int)Background + 1) % 3); dirty = true; }
        if ((command & ReaderCommand.Contrast) != 0) { contrast.Value = !Contrast; dirty = true; }
        if ((command & ReaderCommand.View) != 0) Expanded = !Expanded;
    }

    internal static void CloseView() => Expanded = false;
    internal static void Flush() => Save(false);
    internal static void FlushOnShutdown() => Save(true);
    private static void Save(bool force)
    {
        if (!dirty || (!force && Environment.TickCount64 < retryAt)) return;
        try { file.Save(); dirty = false; retryAt = 0; }
        catch (Exception error)
        {
            // Retain the latest selection until a save succeeds. Avoid retrying
            // on every frame while a file is temporarily locked/unwritable.
            retryAt = Environment.TickCount64 + 2000;
            Plugin.ReportOnce("reading preferences could not be saved; retry pending", error);
        }
    }
}
