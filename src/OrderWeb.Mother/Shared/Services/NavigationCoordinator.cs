using System.Diagnostics;

namespace POS_in_NET.Services;

public interface INavigationCommitParticipant
{
    /// <summary>
    /// Returns false when navigation must be cancelled (for example, an
    /// uncommitted table basket still needs an explicit staff decision).
    /// </summary>
    Task<bool> CommitBeforeNavigationAsync();
}

/// <summary>
/// Owns application navigation so rapid taps cannot push duplicate pages and
/// temporary order flows do not remain hidden in Shell navigation stacks.
/// </summary>
public sealed class NavigationCoordinator
{
    private static readonly HashSet<string> TemporaryRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "collection",
        "delivery",
        "reportdetails"
    };

    private readonly SemaphoreSlim _navigationGate = new(1, 1);
    private string? _lastTarget;
    private DateTime _lastNavigationCompletedAt = DateTime.MinValue;

    public static NavigationCoordinator Shared { get; } = new();

    public bool IsNavigating => _navigationGate.CurrentCount == 0;

    public static bool IsTemporaryRoute(string route)
    {
        var routeName = route.Trim().TrimStart('/').Split('?', StringSplitOptions.RemoveEmptyEntries)[0];
        return TemporaryRoutes.Contains(routeName);
    }

    public Task<bool> NavigateShellAsync(
        string route,
        bool animated = false,
        VisualElement? source = null,
        bool clearTemporaryPages = true)
    {
        var normalized = route.StartsWith("//", StringComparison.Ordinal) ? route : $"//{route.TrimStart('/')}";
        return ExecuteAsync(
            normalized,
            async () =>
            {
                await Shell.Current.GoToAsync(normalized, animated);
                if (clearTemporaryPages)
                {
                    PruneTemporaryPages();
                }
            },
            source);
    }

    public Task<bool> NavigateTemporaryRouteAsync(string route, bool animated = true, VisualElement? source = null) =>
        ExecuteAsync(route, () => Shell.Current.GoToAsync(route, animated), source);

    public Task<bool> PushTemporaryPageAsync(Page page, bool animated = true, VisualElement? source = null) =>
        ExecuteAsync(
            $"page:{page.GetType().FullName}",
            () => (Shell.Current?.Navigation ?? page.Navigation).PushAsync(page, animated),
            source);

    public Task<bool> PopTemporaryPageAsync(INavigation navigation, bool animated = true, VisualElement? source = null) =>
        ExecuteAsync("pop:temporary", () => navigation.PopAsync(animated), source);

    public Task<bool> GoBackAsync(bool animated = true, VisualElement? source = null) =>
        ExecuteAsync("..", () => Shell.Current.GoToAsync("..", animated), source);

    public async Task<bool> ExecuteAsync(
        string target,
        Func<Task> navigation,
        VisualElement? source = null)
    {
        if (!await _navigationGate.WaitAsync(0))
        {
            PosPerformanceMonitor.RecordDuplicateNavigationAttempt();
            return false;
        }

        var timing = PosPerformanceMonitor.BeginNavigation(target);
        var originalOpacity = source?.Opacity ?? 1d;
        var originalEnabled = source?.IsEnabled ?? true;

        try
        {
            if (string.Equals(_lastTarget, target, StringComparison.OrdinalIgnoreCase)
                && DateTime.UtcNow - _lastNavigationCompletedAt < TimeSpan.FromMilliseconds(500))
            {
                PosPerformanceMonitor.RecordDuplicateNavigationAttempt();
                PosPerformanceMonitor.CancelNavigation(timing);
                return false;
            }

            if (source != null)
            {
                source.IsEnabled = false;
                source.Opacity = 0.72;
            }

            // Yield once so the pressed/disabled state is painted before navigation work starts.
            await Task.Yield();
            if (source != null)
            {
                PosPerformanceMonitor.RecordTapFeedback(timing);
            }
            if (ResolveVisiblePage() is INavigationCommitParticipant commitParticipant)
            {
                if (!await commitParticipant.CommitBeforeNavigationAsync())
                {
                    PosPerformanceMonitor.CancelNavigation(timing);
                    return false;
                }
            }
            await navigation();
            PosPerformanceMonitor.MarkNavigationFrameVisible(timing);
            _lastTarget = target;
            _lastNavigationCompletedAt = DateTime.UtcNow;
            return true;
        }
        catch (Exception ex)
        {
            PosPerformanceMonitor.CancelNavigation(timing);
            Debug.WriteLine($"[Navigation] Failed to navigate to '{target}': {ex}");
            throw;
        }
        finally
        {
            if (source != null)
            {
                source.Opacity = originalOpacity;
                source.IsEnabled = originalEnabled;
            }

            _navigationGate.Release();
        }
    }

    private static Page? ResolveVisiblePage()
    {
        var shell = Shell.Current;
        var navigationTop = shell?.Navigation?.NavigationStack?.LastOrDefault();
        return navigationTop ?? shell?.CurrentPage;
    }

    public static void PruneTemporaryPages()
    {
        if (Shell.Current is not Shell shell)
        {
            return;
        }

        foreach (var shellItem in shell.Items)
        {
            foreach (var shellSection in shellItem.Items)
            {
                var navigation = shellSection.Navigation;
                if (navigation?.NavigationStack == null || navigation.NavigationStack.Count <= 1)
                {
                    continue;
                }

                foreach (var page in navigation.NavigationStack.Skip(1).ToList())
                {
                    navigation.RemovePage(page);
                }
            }
        }
    }
}
