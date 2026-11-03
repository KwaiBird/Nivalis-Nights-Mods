namespace NivalisNights.FlatNivalisTimes;

// Attempt independent releases even when another owner must retain a resource.
internal sealed class CleanupBatch
{
    private readonly List<Exception> errors = new();
    internal void Attempt(Action action)
    {
        try { action(); } catch (Exception error) { errors.Add(error); }
    }
    internal void ThrowIfFailed()
    {
        if (errors.Count != 0) throw new AggregateException("Cleanup incomplete; ownership retained for retry.", errors);
    }
}

internal sealed class SubscriptionPair : IDisposable
{
    private readonly Action addFirst, removeFirst, addSecond, removeSecond;
    private bool first, second;
    internal bool Stopped { get; private set; }
    internal bool HasOwnedSubscriptions => first || second;
    internal SubscriptionPair(Action addFirst, Action removeFirst, Action addSecond, Action removeSecond)
        => (this.addFirst, this.removeFirst, this.addSecond, this.removeSecond) = (addFirst, removeFirst, addSecond, removeSecond);

    internal void Subscribe()
    {
        if (Stopped || first || second) throw new InvalidOperationException("Subscription owner cannot be reused.");
        try
        {
            addFirst(); first = true;
            addSecond(); second = true;
        }
        catch (Exception error)
        {
            try { Dispose(); }
            catch (Exception rollback) { throw new AggregateException(error, rollback); }
            throw;
        }
    }
    public void Dispose()
    {
        Stopped = true;
        var cleanup = new CleanupBatch();
        cleanup.Attempt(() => { if (first) { removeFirst(); first = false; } });
        cleanup.Attempt(() => { if (second) { removeSecond(); second = false; } });
        cleanup.ThrowIfFailed();
    }
}
