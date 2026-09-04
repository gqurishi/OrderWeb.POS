using System.Collections.Concurrent;

namespace POS_in_NET.Services;

[Flags]
public enum BackgroundSyncResources
{
    None = 0,
    Database = 1,
    Cloud = 2,
    Api = 4,
    Network = 8,
    Printer = 16
}

public enum BackgroundSyncPriority
{
    Critical = 0,
    Important = 1,
    Normal = 2,
    Low = 3
}

public enum BackgroundSyncTerminalScope
{
    AnyConfiguredTerminal,
    MotherOnly,
    OnlineOrderMasterOnly,
    ChildSafe
}

public enum BackgroundSyncJobStatus
{
    Registered,
    Waiting,
    Running,
    Succeeded,
    Failed,
    BackingOff,
    Paused,
    Skipped,
    Disabled
}

public sealed record BackgroundSyncJobDefinition
{
    public required string Name { get; init; }
    public BackgroundSyncPriority Priority { get; init; } = BackgroundSyncPriority.Normal;
    public TimeSpan NormalInterval { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan IdleInterval { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan FailureBackoff { get; init; } = TimeSpan.FromMinutes(1);
    public TimeSpan InitialDelay { get; init; } = TimeSpan.Zero;
    public BackgroundSyncResources RequiredResources { get; init; } = BackgroundSyncResources.None;
    public BackgroundSyncTerminalScope TerminalScope { get; init; } = BackgroundSyncTerminalScope.AnyConfiguredTerminal;
    public bool CanRunDuringPaymentOrOrderEntry { get; init; } = true;
    public bool CanRunWhileUserActive { get; init; } = true;
    public bool IsEnabled { get; init; } = true;
}

public sealed record BackgroundSyncRunResult(bool Success, string? Message = null, bool Skipped = false)
{
    public static BackgroundSyncRunResult Completed(string? message = null) => new(true, message);
    public static BackgroundSyncRunResult Failed(string message) => new(false, message);
    public static BackgroundSyncRunResult Skip(string message) => new(true, message, true);
}

public sealed record BackgroundSyncJobSnapshot
{
    public required string Name { get; init; }
    public BackgroundSyncPriority Priority { get; init; }
    public TimeSpan NormalInterval { get; init; }
    public TimeSpan IdleInterval { get; init; }
    public TimeSpan FailureBackoff { get; init; }
    public BackgroundSyncResources RequiredResources { get; init; }
    public BackgroundSyncTerminalScope TerminalScope { get; init; }
    public bool CanRunDuringPaymentOrOrderEntry { get; init; }
    public bool CanRunWhileUserActive { get; init; }
    public BackgroundSyncJobStatus Status { get; init; }
    public DateTime? LastRunUtc { get; init; }
    public DateTime? NextRunUtc { get; init; }
    public string? LastError { get; init; }
    public int ConsecutiveFailures { get; init; }
    public bool IsEnabled { get; init; }
}

public sealed class BackgroundSyncManager : IDisposable
{
    private sealed class ManagedJob
    {
        public required BackgroundSyncJobDefinition Definition { get; init; }
        public required Func<CancellationToken, Task<BackgroundSyncRunResult>> ExecuteAsync { get; init; }
        public readonly SemaphoreSlim Gate = new(1, 1);
        public BackgroundSyncJobStatus Status = BackgroundSyncJobStatus.Registered;
        public DateTime? LastRunUtc;
        public DateTime NextRunUtc;
        public string? LastError;
        public int ConsecutiveFailures;
    }

    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan UserActiveWindow = TimeSpan.FromSeconds(20);

    private readonly ConcurrentDictionary<string, ManagedJob> _jobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lifecycleLock = new();
    private CancellationTokenSource? _cts;
    private Task? _runLoopTask;
    private DateTime _lastUserActivityUtc = DateTime.UtcNow;
    private int _criticalActivityDepth;

    public event Action<IReadOnlyList<BackgroundSyncJobSnapshot>>? StatusChanged;

    public bool IsRunning => _runLoopTask is { IsCompleted: false };
    public bool IsUserActive => DateTime.UtcNow - _lastUserActivityUtc < UserActiveWindow;
    public bool IsInCriticalActivity => Volatile.Read(ref _criticalActivityDepth) > 0;

    public void RegisterJob(
        BackgroundSyncJobDefinition definition,
        Func<CancellationToken, Task<BackgroundSyncRunResult>> executeAsync)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(executeAsync);

        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            throw new ArgumentException("Background sync job name is required.", nameof(definition));
        }

        var now = DateTime.UtcNow;
        var job = new ManagedJob
        {
            Definition = definition,
            ExecuteAsync = executeAsync,
            NextRunUtc = definition.IsEnabled ? now.Add(definition.InitialDelay) : DateTime.MaxValue,
            Status = definition.IsEnabled ? BackgroundSyncJobStatus.Waiting : BackgroundSyncJobStatus.Disabled
        };

        _jobs.AddOrUpdate(definition.Name, job, (_, _) => job);
        RaiseStatusChanged();
    }

    public bool UnregisterJob(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var removed = _jobs.TryRemove(name, out var job);
        job?.Gate.Dispose();
        if (removed)
        {
            RaiseStatusChanged();
        }

        return removed;
    }

    public void Start()
    {
        lock (_lifecycleLock)
        {
            if (IsRunning)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _runLoopTask = Task.Run(() => RunLoopAsync(_cts.Token));
        }
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        Task? runLoopTask;

        lock (_lifecycleLock)
        {
            cts = _cts;
            runLoopTask = _runLoopTask;
            _cts = null;
            _runLoopTask = null;
        }

        if (cts == null)
        {
            return;
        }

        cts.Cancel();
        try
        {
            if (runLoopTask != null)
            {
                await runLoopTask;
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cts.Dispose();
        }
    }

    public void NotifyUserActivity()
    {
        _lastUserActivityUtc = DateTime.UtcNow;
    }

    public IDisposable BeginCriticalActivity()
    {
        Interlocked.Increment(ref _criticalActivityDepth);
        NotifyUserActivity();
        return new CriticalActivityScope(this);
    }

    public bool RequestRunSoon(string name, TimeSpan? delay = null)
    {
        if (string.IsNullOrWhiteSpace(name) || !_jobs.TryGetValue(name, out var job))
        {
            return false;
        }

        if (!job.Definition.IsEnabled)
        {
            return false;
        }

        var requestedRunUtc = DateTime.UtcNow.Add(delay ?? TimeSpan.Zero);
        if (requestedRunUtc < job.NextRunUtc)
        {
            job.NextRunUtc = requestedRunUtc;
            job.Status = BackgroundSyncJobStatus.Waiting;
            RaiseStatusChanged();
        }

        return true;
    }

    public IReadOnlyList<BackgroundSyncJobSnapshot> GetSnapshots()
    {
        return _jobs.Values
            .OrderBy(job => job.Definition.Priority)
            .ThenBy(job => job.Definition.Name, StringComparer.OrdinalIgnoreCase)
            .Select(CreateSnapshot)
            .ToList();
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TickInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            await RunDueJobsAsync(cancellationToken);
        }
    }

    private async Task RunDueJobsAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var dueJobs = _jobs.Values
            .Where(job => job.Definition.IsEnabled && job.NextRunUtc <= now)
            .OrderBy(job => job.Definition.Priority)
            .ThenBy(job => job.NextRunUtc)
            .ToList();

        foreach (var job in dueJobs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await job.Gate.WaitAsync(0, cancellationToken))
            {
                continue;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await RunJobAsync(job, cancellationToken);
                }
                finally
                {
                    job.Gate.Release();
                }
            });
        }
    }

    private async Task RunJobAsync(ManagedJob job, CancellationToken cancellationToken)
    {
        if (!CanRunOnThisTerminal(job.Definition, out var terminalReason))
        {
            MarkSkipped(job, terminalReason);
            return;
        }

        if (IsInCriticalActivity && !job.Definition.CanRunDuringPaymentOrOrderEntry)
        {
            PauseJob(job, "Paused during payment/order entry.", TimeSpan.FromSeconds(5));
            return;
        }

        if (ShouldPauseForUserActivity(job.Definition, out var activityReason))
        {
            PauseJob(job, activityReason, TimeSpan.FromSeconds(5));
            return;
        }

        job.Status = BackgroundSyncJobStatus.Running;
        RaiseStatusChanged();

        try
        {
            var result = await job.ExecuteAsync(cancellationToken);
            job.LastRunUtc = DateTime.UtcNow;

            if (result.Skipped)
            {
                MarkSkipped(job, result.Message ?? "Skipped.");
                return;
            }

            if (result.Success)
            {
                job.Status = BackgroundSyncJobStatus.Succeeded;
                job.LastError = null;
                job.ConsecutiveFailures = 0;
                job.NextRunUtc = DateTime.UtcNow.Add(GetCurrentInterval(job.Definition));
            }
            else
            {
                job.Status = BackgroundSyncJobStatus.Failed;
                job.LastError = result.Message ?? "Job failed.";
                job.ConsecutiveFailures++;
                job.NextRunUtc = DateTime.UtcNow.Add(job.Definition.FailureBackoff);
            }
        }
        catch (OperationCanceledException)
        {
            job.Status = BackgroundSyncJobStatus.Paused;
            job.NextRunUtc = DateTime.UtcNow.Add(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            job.LastRunUtc = DateTime.UtcNow;
            job.Status = BackgroundSyncJobStatus.BackingOff;
            job.LastError = ex.Message;
            job.ConsecutiveFailures++;
            job.NextRunUtc = DateTime.UtcNow.Add(job.Definition.FailureBackoff);
            AppDiagnostics.Log($"Background sync job '{job.Definition.Name}' failed: {ex.Message}");
        }
        finally
        {
            RaiseStatusChanged();
        }
    }

    private void MarkSkipped(ManagedJob job, string reason)
    {
        job.LastRunUtc = DateTime.UtcNow;
        job.Status = BackgroundSyncJobStatus.Skipped;
        job.LastError = reason;
        job.NextRunUtc = DateTime.UtcNow.Add(GetCurrentInterval(job.Definition));
        RaiseStatusChanged();
    }

    private void PauseJob(ManagedJob job, string reason, TimeSpan delay)
    {
        job.Status = BackgroundSyncJobStatus.Paused;
        job.LastError = reason;
        job.NextRunUtc = DateTime.UtcNow.Add(delay);
        RaiseStatusChanged();
    }

    private bool ShouldPauseForUserActivity(BackgroundSyncJobDefinition definition, out string reason)
    {
        reason = string.Empty;
        if (!IsUserActive)
        {
            return false;
        }

        if (definition.Priority == BackgroundSyncPriority.Low)
        {
            reason = "Low-priority job waiting for idle till.";
            return true;
        }

        if (!definition.CanRunWhileUserActive)
        {
            reason = "Background job waiting for idle till.";
            return true;
        }

        return false;
    }

    private TimeSpan GetCurrentInterval(BackgroundSyncJobDefinition definition)
    {
        return IsUserActive ? definition.NormalInterval : definition.IdleInterval;
    }

    private static bool CanRunOnThisTerminal(BackgroundSyncJobDefinition definition, out string reason)
    {
        reason = string.Empty;

        if (!TerminalConfigurationService.IsConfigured &&
            definition.TerminalScope != BackgroundSyncTerminalScope.AnyConfiguredTerminal)
        {
            reason = "Terminal setup is not complete.";
            return false;
        }

        switch (definition.TerminalScope)
        {
            case BackgroundSyncTerminalScope.MotherOnly:
                if (!TerminalRoleService.CanRunMotherJobs)
                {
                    reason = "Mother terminal only.";
                    return false;
                }
                break;
            case BackgroundSyncTerminalScope.ChildSafe:
            case BackgroundSyncTerminalScope.AnyConfiguredTerminal:
                if (!TerminalConfigurationService.IsConfigured)
                {
                    reason = "Terminal setup is not complete.";
                    return false;
                }
                break;
            case BackgroundSyncTerminalScope.OnlineOrderMasterOnly:
                if (!TerminalConfigurationService.IsConfigured || TerminalConfigurationService.IsChildTerminal)
                {
                    reason = "Online master jobs do not run on child/unconfigured terminals.";
                    return false;
                }
                break;
        }

        return true;
    }

    private static BackgroundSyncJobSnapshot CreateSnapshot(ManagedJob job)
    {
        var definition = job.Definition;
        return new BackgroundSyncJobSnapshot
        {
            Name = definition.Name,
            Priority = definition.Priority,
            NormalInterval = definition.NormalInterval,
            IdleInterval = definition.IdleInterval,
            FailureBackoff = definition.FailureBackoff,
            RequiredResources = definition.RequiredResources,
            TerminalScope = definition.TerminalScope,
            CanRunDuringPaymentOrOrderEntry = definition.CanRunDuringPaymentOrOrderEntry,
            CanRunWhileUserActive = definition.CanRunWhileUserActive,
            Status = job.Status,
            LastRunUtc = job.LastRunUtc,
            NextRunUtc = job.NextRunUtc == DateTime.MaxValue ? null : job.NextRunUtc,
            LastError = job.LastError,
            ConsecutiveFailures = job.ConsecutiveFailures,
            IsEnabled = definition.IsEnabled
        };
    }

    private void RaiseStatusChanged()
    {
        try
        {
            StatusChanged?.Invoke(GetSnapshots());
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log($"Background sync status handler failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch
        {
        }

        foreach (var job in _jobs.Values)
        {
            job.Gate.Dispose();
        }

        _jobs.Clear();
    }

    private sealed class CriticalActivityScope : IDisposable
    {
        private readonly BackgroundSyncManager _manager;
        private bool _disposed;

        public CriticalActivityScope(BackgroundSyncManager manager)
        {
            _manager = manager;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Interlocked.Decrement(ref _manager._criticalActivityDepth);
        }
    }
}
