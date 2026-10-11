namespace Loupedeck.AudioControlPlugin;

using System.Collections.Concurrent;
using System.Diagnostics;

// Temporary diagnostics: writes to its own file so it does not depend on the service log level.
internal static class Diag
{
    private static readonly object _lock = new object();
    private static readonly ConcurrentDictionary<string, long> _lastByKey = new ConcurrentDictionary<string, long>();
    private static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Logi", "LogiPluginService", "Logs", "plugin_logs", "AudioControl_diag.log");

    public static void Log(string text)
    {
        string line = $"{DateTime.Now:HH:mm:ss.fff} [{Environment.CurrentManagedThreadId,3}] {text}{Environment.NewLine}";
        lock (_lock)
        {
            try
            {
                File.AppendAllText(_path, line);
            }
            catch
            {
            }
        }
    }

    // Logs at most once per interval for the given key, with a count of suppressed calls.
    public static void Throttled(string key, string text, int intervalMs = 1000)
    {
        long now = Stopwatch.GetTimestamp();
        long last = _lastByKey.GetOrAdd(key, 0);
        if (last == 0 || Stopwatch.GetElapsedTime(last, now).TotalMilliseconds >= intervalMs)
        {
            _lastByKey[key] = now;
            Log(text);
        }
    }

    public static void Error(string where, Exception ex)
    {
        Log($"EXCEPTION in {where}: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
    }
}
