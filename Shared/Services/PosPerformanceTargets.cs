namespace POS_in_NET.Services;

/// <summary>
/// Phase 4 user-experience budgets. These values are kept in one place so
/// diagnostics, verification tooling and regression tests use identical limits.
/// </summary>
public static class PosPerformanceTargets
{
    public const double TapFeedbackMilliseconds = 50;
    public const double NavigationFrameMilliseconds = 150;
    public const double NormalPageDataMilliseconds = 600;

    public static double GetLimit(PosPerformanceMetric metric) => metric switch
    {
        PosPerformanceMetric.TapFeedback => TapFeedbackMilliseconds,
        PosPerformanceMetric.NavigationFrame => NavigationFrameMilliseconds,
        PosPerformanceMetric.PageDataVisible => NormalPageDataMilliseconds,
        _ => double.PositiveInfinity
    };
}

public enum PosPerformanceMetric
{
    TapFeedback,
    NavigationFrame,
    PageDataVisible
}

public sealed record PosPerformanceSample(
    DateTimeOffset Timestamp,
    string Operation,
    PosPerformanceMetric Metric,
    double ElapsedMilliseconds,
    double TargetMilliseconds,
    bool Passed);
