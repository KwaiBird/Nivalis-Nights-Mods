using Nivalis;

namespace NivalisNights.FlatNivalisTimes;

internal static class ReaderWakeup
{
    internal static bool Allows(PlayerInputManager input, Newspaper? paper, PlayerHandsAnimator? hands)
    {
        try { return Read(input, paper, hands); }
        catch (Exception error)
        {
            Plugin.ReportOnce("wake-up ownership rejected", error);
            return false;
        }
    }

    private static bool Read(PlayerInputManager input, Newspaper? paper, PlayerHandsAnimator? hands)
    {
        // SleepManager deliberately keeps its menu lock while NewspaperSequence reads.
        // Its camera-look lease exists only for that reading phase and is released
        // when EquipNewspaper finishes. Inspect both leases; never release either.
        if (!PaperStates.Alive(paper) || paper!.isPoster || !paper.Equipped || !paper.FullyVisible
            || !PaperStates.Alive(hands) || hands!.IsPlayingMealAnimation || hands.IsHoldingTray
            || !PaperStates.Alive(hands.newspaper) || hands.newspaper.Pointer != paper.Pointer
            || (PaperStates.Alive(hands.poster) && hands.poster.Equipped)) return false;
        if (!SleepManager.InstanceNotNull(out var sleep) || !sleep.isActiveAndEnabled
            || !PlayerManager.InstanceNotNull(out var players)) return false;
        var player = players.LocalPlayer?.Character;
        var controller = PaperStates.Alive(player) ? player!.Controller : null;
        if (!PaperStates.Alive(controller) || !PaperStates.Alive(controller!.HandsAnimator)
            || controller.HandsAnimator.Pointer != hands.Pointer) return false;

        var shortcuts = input.PreventAllMenuShortcuts;
        var menu = sleep._preventOpeningMenuLock;
        var reading = sleep._playerLookLock;
        var look = controller._cameraLookEnabled;
        if (shortcuts == null || menu == null || reading == null || look == null) return false;
        var owners = shortcuts._overrideOwners;
        if (owners == null) return false;
        return WakeupLockPolicy.Allows(sleep.Pointer, shortcuts.Pointer, shortcuts._defaultValue,
            owners.Count, owners.Contains(sleep), Capture(menu), look.Pointer, Capture(reading));
    }

    private static WakeupLockPolicy.Lease Capture(OverrideableBool.OverrideLock value)
        => new(value._owner?.Pointer ?? IntPtr.Zero, value._target?.Pointer ?? IntPtr.Zero, value.IsReleased);
}
