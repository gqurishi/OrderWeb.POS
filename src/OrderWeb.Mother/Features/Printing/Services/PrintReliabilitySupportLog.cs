using System.Diagnostics;

namespace POS_in_NET.Services;

/// <summary>
/// Phase 5 support log (Release + Debug): job id, printer, attempts, last error, status probe.
/// File: AppData → <c>print-reliability.log</c>
/// </summary>
public static class PrintReliabilitySupportLog
{
    private static readonly object Sync = new();
    private static string? _path;

    public static void Write(
        int jobId,
        string? printerName,
        int printerId,
        int attempt,
        string outcome,
        string? lastError = null,
        string? orderId = null,
        string? jobType = null,
        string? statusDetail = null)
    {
        var line =
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} " +
            $"[Print support] job=#{jobId} printerId={printerId} printer={Sanitize(printerName) ?? "?"} " +
            $"type={Sanitize(jobType) ?? "-"} order={Sanitize(orderId) ?? "-"} " +
            $"attempts={attempt} outcome={outcome}" +
            (string.IsNullOrWhiteSpace(statusDetail) ? "" : $" status={Sanitize(statusDetail)}") +
            (string.IsNullOrWhiteSpace(lastError) ? "" : $" error={Sanitize(lastError)}");

        Debug.WriteLine(line);
        try
        {
            var path = GetPath();
            lock (Sync)
            {
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch
        {
            // Never block the till on logging.
        }
    }

    private static string GetPath()
    {
        if (_path != null)
        {
            return _path;
        }

        string directory;
        try
        {
            directory = FileSystem.AppDataDirectory;
        }
        catch
        {
            directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OrderWebPOS",
                "logs");
        }

        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "print-reliability.log");
        return _path;
    }

    private static string? Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return value.Replace('\r', ' ').Replace('\n', ' ').Trim();
    }
}
