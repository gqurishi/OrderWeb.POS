using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace POS_in_NET.Services;

/// <summary>
/// Lightweight, privacy-safe Phase 4 timing monitor. It records operation names
/// and durations only; order, customer and payment data are never captured.
/// </summary>
public static class PosPerformanceMonitor
{
    private const int MaximumSamples = 500;
    private static readonly ConcurrentQueue<PosPerformanceSample> Samples = new();
    private static readonly object NavigationSync = new();
    private static NavigationTiming? _activeNavigation;
    private static long _nextNavigationId;
    private static long _duplicateNavigationAttempts;

    public static event EventHandler<PosPerformanceSample>? SampleRecorded;

    public static long DuplicateNavigationAttempts => Interlocked.Read(ref _duplicateNavigationAttempts);

    public static NavigationTiming BeginNavigation(string target)
    {
        var timing = new NavigationTiming(
            Interlocked.Increment(ref _nextNavigationId),
            NormalizeOperation(target),
            Stopwatch.GetTimestamp());

        lock (NavigationSync)
        {
            _activeNavigation = timing;
        }

        return timing;
    }

    public static void RecordTapFeedback(NavigationTiming timing) =>
        Record(timing.Operation, PosPerformanceMetric.TapFeedback, timing.ElapsedMilliseconds);

    public static void MarkNavigationFrameVisible(NavigationTiming timing, string? visiblePage = null)
    {
        if (Interlocked.Exchange(ref timing.FrameRecorded, 1) != 0)
            return;

        var operation = string.IsNullOrWhiteSpace(visiblePage)
            ? timing.Operation
            : $"{timing.Operation} -> {NormalizeOperation(visiblePage)}";
        Record(operation, PosPerformanceMetric.NavigationFrame, timing.ElapsedMilliseconds);

        lock (NavigationSync)
        {
            if (_activeNavigation?.Id == timing.Id)
                _activeNavigation = null;
        }
    }

    public static void MarkCurrentNavigationFrameVisible(string visiblePage)
    {
        NavigationTiming? timing;
        lock (NavigationSync)
        {
            timing = _activeNavigation;
        }

        if (timing != null)
            MarkNavigationFrameVisible(timing, visiblePage);
    }

    public static void CancelNavigation(NavigationTiming timing)
    {
        lock (NavigationSync)
        {
            if (_activeNavigation?.Id == timing.Id)
                _activeNavigation = null;
        }
    }

    public static DataLoadTiming BeginDataLoad(string operation) =>
        new(NormalizeOperation(operation), Stopwatch.GetTimestamp());

    public static void MarkDataVisible(DataLoadTiming timing) =>
        Record(timing.Operation, PosPerformanceMetric.PageDataVisible, timing.ElapsedMilliseconds);

    public static void RecordDuplicateNavigationAttempt() =>
        Interlocked.Increment(ref _duplicateNavigationAttempts);

    public static IReadOnlyList<PosPerformanceSample> GetSamples() => Samples.ToArray();

    public static void ResetSession()
    {
        while (Samples.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _duplicateNavigationAttempts, 0);
        lock (NavigationSync)
        {
            _activeNavigation = null;
        }
    }

    public static async Task<string> ExportMarkdownAsync(
        string directory,
        int windowsScalingPercent,
        CancellationToken cancellationToken = default)
    {
        if (windowsScalingPercent is not (100 or 125 or 150 or 175))
            throw new ArgumentOutOfRangeException(nameof(windowsScalingPercent));

        Directory.CreateDirectory(directory);
        var path = Path.Combine(
            directory,
            $"phase4-{DateTime.Now:yyyyMMdd-HHmmss}-{windowsScalingPercent}pct.md");
        var snapshot = GetSamples();
        var builder = new StringBuilder();
        builder.AppendLine("# Phase 4 performance capture");
        builder.AppendLine();
        builder.AppendLine($"- Captured: {DateTimeOffset.Now:O}");
        builder.AppendLine($"- Windows scaling: {windowsScalingPercent}%");
        builder.AppendLine($"- Duplicate navigation attempts blocked: {DuplicateNavigationAttempts}");
        builder.AppendLine();
        builder.AppendLine("| Metric | Operation | Elapsed | Target | Result |");
        builder.AppendLine("|---|---|---:|---:|---|");
        foreach (var sample in snapshot)
        {
            builder.Append("| ").Append(sample.Metric)
                .Append(" | ").Append(sample.Operation.Replace("|", "/", StringComparison.Ordinal))
                .Append(" | ").Append(sample.ElapsedMilliseconds.ToString("0.0 ms", CultureInfo.InvariantCulture))
                .Append(" | ").Append(sample.TargetMilliseconds.ToString("0 ms", CultureInfo.InvariantCulture))
                .Append(" | ").Append(sample.Passed ? "PASS" : "FAIL").AppendLine(" |");
        }

        await File.WriteAllTextAsync(path, builder.ToString(), cancellationToken);
        return path;
    }

    private static void Record(string operation, PosPerformanceMetric metric, double elapsedMilliseconds)
    {
        var target = PosPerformanceTargets.GetLimit(metric);
        var sample = new PosPerformanceSample(
            DateTimeOffset.Now,
            operation,
            metric,
            elapsedMilliseconds,
            target,
            elapsedMilliseconds <= target);

        Samples.Enqueue(sample);
        while (Samples.Count > MaximumSamples && Samples.TryDequeue(out _)) { }

        AppDiagnostics.Log(
            $"[PERF] {metric} {operation}: {elapsedMilliseconds:0.0}ms / {target:0}ms " +
            (sample.Passed ? "PASS" : "FAIL"));
        try
        {
            SampleRecorded?.Invoke(null, sample);
        }
        catch (Exception ex)
        {
            // Diagnostics must never interrupt an order, payment or navigation flow.
            AppDiagnostics.Log($"[PERF] Sample observer failed: {ex.Message}");
        }
    }

    private static string NormalizeOperation(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unknown";
        var normalized = value.Trim().Replace('\r', ' ').Replace('\n', ' ');
        var queryIndex = normalized.IndexOf('?');
        return queryIndex >= 0 ? normalized[..queryIndex] : normalized;
    }

    public sealed class NavigationTiming
    {
        internal NavigationTiming(long id, string operation, long startedTimestamp)
        {
            Id = id;
            Operation = operation;
            StartedTimestamp = startedTimestamp;
        }

        internal long Id { get; }
        internal string Operation { get; }
        internal long StartedTimestamp { get; }
        internal int FrameRecorded;
        internal double ElapsedMilliseconds => Stopwatch.GetElapsedTime(StartedTimestamp).TotalMilliseconds;
    }

    public sealed class DataLoadTiming
    {
        internal DataLoadTiming(string operation, long startedTimestamp)
        {
            Operation = operation;
            StartedTimestamp = startedTimestamp;
        }

        internal string Operation { get; }
        internal long StartedTimestamp { get; }
        internal double ElapsedMilliseconds => Stopwatch.GetElapsedTime(StartedTimestamp).TotalMilliseconds;
    }
}
