namespace WordStrip.Core.Suggestions;

/// <summary>
/// Collapses a burst of updates into one delivery of the newest.
///
/// <para>Prediction runs on every keystroke, and should — it is fast, and a stale model is worse than a busy
/// one. Drawing need not. Posting here instead of rendering directly means any number of updates that land
/// before the scheduled delivery runs are drawn once, as the last of them, and a delivery can never apply a
/// value older than one already applied: values carry a sequence number and the delivery reads the newest at
/// the moment it runs, not the one that happened to schedule it.</para>
///
/// <para>This is coalescing, not throttling. Nothing waits beyond the scheduler's next idle moment, so
/// sustained fast typing still sees every settled state — it just stops paying for the intermediate ones.</para>
/// </summary>
public sealed class LatestValueCoalescer<T>
{
    private readonly Action<Action> _schedule;
    private readonly Action<T> _deliver;

    private T? _pending;
    private long _pendingSequence;
    private long _deliveredSequence;
    private bool _scheduled;

    /// <param name="schedule">Runs the delivery later, e.g. at background priority on the UI dispatcher.</param>
    /// <param name="deliver">Applies one value, on whatever thread <paramref name="schedule"/> uses.</param>
    public LatestValueCoalescer(Action<Action> schedule, Action<T> deliver)
    {
        _schedule = schedule;
        _deliver = deliver;
    }

    /// <summary>How many values were superseded before being delivered. Diagnostics only.</summary>
    public long Coalesced { get; private set; }

    public void Post(T value)
    {
        if (_pendingSequence > _deliveredSequence) Coalesced++;

        _pending = value;
        _pendingSequence++;

        if (_scheduled) return;
        _scheduled = true;
        _schedule(Flush);
    }

    /// <summary>Delivers the newest value now, if it has not been delivered already. Safe to call at any time.</summary>
    public void Flush()
    {
        _scheduled = false;
        if (_pendingSequence <= _deliveredSequence) return;

        _deliveredSequence = _pendingSequence;
        _deliver(_pending!);
    }
}
