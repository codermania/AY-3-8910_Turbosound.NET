namespace Yamaha.Psg.Core.Timing;

/// <summary>
/// A queue of scheduled register writes within a single render buffer, sorted by cycle offset.
/// Lets the render loop tick the chip one cycle at a time and apply each write exactly at the
/// right cycle (not just at buffer boundaries) — this is what makes accurate digi-drum playback
/// possible: fast volume-register changes mid-buffer.
/// </summary>
public sealed class SubFrameEventQueue
{
    private readonly List<TimedRegisterWrite> _pending = [];
    private int _nextIndex;

    /// <summary>Loads the writes for the next render buffer (replaces the previous contents).</summary>
    public void Load(IReadOnlyList<TimedRegisterWrite>? writes)
    {
        _pending.Clear();
        _nextIndex = 0;

        if (writes is null)
        {
            return;
        }

        _pending.AddRange(writes);
        _pending.Sort((a, b) => a.CycleOffset.CompareTo(b.CycleOffset));
    }

    /// <summary>
    /// Returns (and consumes) all not-yet-emitted writes whose offset is &lt;= <paramref name="currentCycle"/>,
    /// in ascending offset order. Each write is returned exactly once over the queue's lifetime.
    /// </summary>
    public IEnumerable<TimedRegisterWrite> DrainDue(int currentCycle)
    {
        while (_nextIndex < _pending.Count && _pending[_nextIndex].CycleOffset <= currentCycle)
        {
            yield return _pending[_nextIndex];
            _nextIndex++;
        }
    }

    /// <summary>Whether any writes are still pending (useful for test assertions).</summary>
    public bool HasPending => _nextIndex < _pending.Count;
}
