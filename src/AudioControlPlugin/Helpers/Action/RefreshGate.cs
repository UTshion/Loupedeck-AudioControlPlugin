namespace Loupedeck.AudioControlPlugin;

// The shared refresh timer is a System.Timers.Timer: its Elapsed handlers run on thread-pool threads and
// overlap when a refresh takes longer than the interval (20 FPS by default). Overlapping refreshes pile up
// threads and redraw the same buttons concurrently, so a refresh that is still running makes the next tick skip.
internal sealed class RefreshGate
{
    private int _running = 0;

    public bool TryEnter() => Interlocked.CompareExchange(ref this._running, 1, 0) == 0;

    public void Exit() => Interlocked.Exchange(ref this._running, 0);
}
