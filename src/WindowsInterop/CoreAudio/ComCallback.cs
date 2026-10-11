namespace WindowsInterop.CoreAudio;

// Core Audio notification callbacks must return quickly and must not register/unregister
// notifications or release objects (documented deadlock risk), so that work is moved off the callback thread.
public static class ComCallback
{
    public static event EventHandler<Exception> UnhandledException;

    public static void Defer(Action action)
    {
        ThreadPool.QueueUserWorkItem(_ => Run(action));
    }

    // Exceptions must never escape back into COM or onto a thread-pool thread (that would take down the host).
    public static void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Report(ex);
        }
    }

    public static void Report(Exception ex)
    {
        try
        {
            UnhandledException?.Invoke(null, ex);
        }
        catch
        {
        }
    }
}
