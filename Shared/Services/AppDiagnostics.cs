using System.Diagnostics;

namespace POS_in_NET.Services;

/// <summary>
/// Central diagnostics: verbose file logging in DEBUG only; minimal crash log in all builds.
/// </summary>
public static class AppDiagnostics
{
    private static readonly object Sync = new();

#if DEBUG
    private static string? _debugLogPath;
#endif

    [Conditional("DEBUG")]
    public static void Log(string message)
    {
#if DEBUG
        Debug.WriteLine(message);
        try
        {
            var path = GetDebugLogPath();
            lock (Sync)
            {
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}\n");
            }
        }
        catch
        {
            // Ignore logging failures.
        }
#endif
    }

    public static void LogFatal(string context, Exception ex)
    {
        try
        {
            var directory = GetLogDirectory();
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "pos-error.log");
            lock (Sync)
            {
                File.AppendAllText(path, $"\n=== {context} {DateTime.Now:u} ===\n{ex.GetType().Name}: {ex.Message}\n");
            }
        }
        catch
        {
            // Ignore logging failures during crash handling.
        }

#if DEBUG
        Debug.WriteLine($"{context}: {ex}");
#endif
    }

#if DEBUG
    private static string GetDebugLogPath()
    {
        if (_debugLogPath != null)
        {
            return _debugLogPath;
        }

        _debugLogPath = Path.Combine(GetLogDirectory(), "pos-debug.log");
        return _debugLogPath;
    }
#endif

    private static string GetLogDirectory()
    {
        try
        {
            return FileSystem.AppDataDirectory;
        }
        catch
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OrderWebPOS",
                "logs");
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
