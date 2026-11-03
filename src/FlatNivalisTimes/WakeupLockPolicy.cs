namespace NivalisNights.FlatNivalisTimes;

// Pointer identities are captured afresh from current, live owners on the game thread.
// No native objects, leases or permission are retained between samples.
internal static class WakeupLockPolicy
{
    internal readonly record struct Lease(IntPtr Owner, IntPtr Target, bool Released);

    internal static bool Allows(IntPtr sleeper, IntPtr shortcuts, bool defaultBlocked, int ownerCount,
        bool containsSleeper, Lease menu, IntPtr cameraLook, Lease reading)
        => sleeper != IntPtr.Zero && shortcuts != IntPtr.Zero && cameraLook != IntPtr.Zero
            && shortcuts != cameraLook && !defaultBlocked && ownerCount == 1 && containsSleeper
            && !menu.Released && menu.Owner == sleeper && menu.Target == shortcuts
            && !reading.Released && reading.Owner == sleeper && reading.Target == cameraLook;

    internal static bool AllowsPlacement(bool enabled, bool holding, bool wakeupReading)
        => !enabled || (!holding && wakeupReading);
}
