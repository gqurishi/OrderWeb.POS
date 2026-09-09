namespace POS_in_NET.Services;

/// <summary>
/// Rush-hour feel budgets (Phase G). Diagnostics and QA use the same limits.
/// </summary>
public static class PosPerformanceTargets
{
    /// <summary>Any button first visual response.</summary>
    public const double TapFeedbackMilliseconds = 50;

    /// <summary>Sidebar / shell navigation frame visible.</summary>
    public const double NavigationFrameMilliseconds = 200;

    /// <summary>Add menu item feel.</summary>
    public const double OrderPlaceInteractionMilliseconds = 150;

    /// <summary>Open occupied table → order screen.</summary>
    public const double OpenTableToOrderMilliseconds = 300;

    /// <summary>Category chip switch.</summary>
    public const double CategorySwitchMilliseconds = 100;

    /// <summary>Generic page data visible (non-till).</summary>
    public const double NormalPageDataMilliseconds = 600;

    public static double GetLimit(PosPerformanceMetric metric) => metric switch
    {
        PosPerformanceMetric.TapFeedback => TapFeedbackMilliseconds,
        PosPerformanceMetric.NavigationFrame => NavigationFrameMilliseconds,
        PosPerformanceMetric.PageDataVisible => NormalPageDataMilliseconds,
        PosPerformanceMetric.OrderPlaceInteraction => OrderPlaceInteractionMilliseconds,
        PosPerformanceMetric.OpenTableToOrder => OpenTableToOrderMilliseconds,
        PosPerformanceMetric.CategorySwitch => CategorySwitchMilliseconds,
        _ => double.PositiveInfinity
    };
}

public enum PosPerformanceMetric
{
    TapFeedback,
    NavigationFrame,
    PageDataVisible,
    OrderPlaceInteraction,
    OpenTableToOrder,
    CategorySwitch
}

public sealed record PosPerformanceSample(
    DateTimeOffset Timestamp,
    string Operation,
    PosPerformanceMetric Metric,
    double ElapsedMilliseconds,
    double TargetMilliseconds,
    bool Passed);
