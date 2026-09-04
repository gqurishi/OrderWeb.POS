using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace POS_in_NET.Services
{
    public enum AppDataChangeKind
    {
        Manual,
        Orders,
        TableLayout,
        Reservations,
        Settings,
        Printers,
        All
    }

    [Flags]
    public enum AppDataRefreshType
    {
        None = 0,
        Orders = 1,
        Tables = 2,
        Reservations = 4,
        Settings = 8,
        Printers = 16,
        All = Orders | Tables | Reservations | Settings | Printers
    }

    public sealed class AppDataChangedEventArgs : EventArgs
    {
        public AppDataChangeKind Kind { get; init; } = AppDataChangeKind.Manual;
        public AppDataRefreshType RefreshTypes { get; init; } = AppDataRefreshType.None;
        public string? SourceTerminalName { get; init; }
        public string? OrderNumber { get; init; }
        public string? EntityType { get; init; }
        public string? EntityId { get; init; }
        public DateTime ChangedAt { get; init; } = DateTime.Now;

        public bool IsFromCurrentTerminal
        {
            get
            {
                try
                {
                    var currentTerminal = TerminalConfigurationService.GetConfiguration().TerminalName;
                    return !string.IsNullOrWhiteSpace(SourceTerminalName)
                        && string.Equals(SourceTerminalName.Trim(), currentTerminal.Trim(), StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return false;
                }
            }
        }

        public string SourceDisplayName => string.IsNullOrWhiteSpace(SourceTerminalName)
            ? "another terminal"
            : SourceTerminalName.Trim();

        public string ToastMessage => HasKind(AppDataChangeKind.Orders)
            ? $"Order updated from {SourceDisplayName}"
            : HasKind(AppDataChangeKind.Reservations)
                ? $"Reservation updated from {SourceDisplayName}"
                : HasKind(AppDataChangeKind.Printers)
                    ? $"Printer settings updated from {SourceDisplayName}"
                    : HasKind(AppDataChangeKind.Settings)
                        ? $"Settings updated from {SourceDisplayName}"
                        : $"Layout updated from {SourceDisplayName}";

        public bool HasKind(AppDataChangeKind kind)
        {
            var types = RefreshTypes == AppDataRefreshType.None
                ? ToRefreshType(Kind)
                : RefreshTypes;

            if (kind == AppDataChangeKind.All)
            {
                return types == AppDataRefreshType.All;
            }

            if (Kind == kind)
            {
                return true;
            }

            return (types & ToRefreshType(kind)) != 0;
        }

        public bool HasAny(params AppDataChangeKind[] kinds)
        {
            return kinds.Any(HasKind);
        }

        internal static AppDataRefreshType ToRefreshType(AppDataChangeKind kind) => kind switch
        {
            AppDataChangeKind.Orders => AppDataRefreshType.Orders,
            AppDataChangeKind.TableLayout => AppDataRefreshType.Tables,
            AppDataChangeKind.Reservations => AppDataRefreshType.Reservations,
            AppDataChangeKind.Settings => AppDataRefreshType.Settings,
            AppDataChangeKind.Printers => AppDataRefreshType.Printers,
            AppDataChangeKind.All or AppDataChangeKind.Manual => AppDataRefreshType.All,
            _ => AppDataRefreshType.None
        };
    }

    public static class AppDataRefreshService
    {
        private static readonly TimeSpan RefreshBatchDelay = TimeSpan.FromMilliseconds(400);
        private static readonly object SyncRoot = new();
        private static readonly List<AppDataChangedEventArgs> PendingChanges = new();
        private static CancellationTokenSource? _batchCts;
        private static bool _pendingLegacyRefreshRequest;

        public static event EventHandler? RefreshRequested;
        public static event EventHandler<AppDataChangedEventArgs>? DataChanged;

        public static void RequestRefresh()
        {
            RequestRefresh(new AppDataChangedEventArgs { Kind = AppDataChangeKind.Manual });
        }

        public static void RequestRefresh(
            AppDataChangeKind kind,
            string? sourceTerminalName = null,
            string? orderNumber = null,
            string? entityType = null,
            string? entityId = null)
        {
            RequestRefresh(new AppDataChangedEventArgs
            {
                Kind = kind,
                RefreshTypes = AppDataChangedEventArgs.ToRefreshType(kind),
                SourceTerminalName = sourceTerminalName,
                OrderNumber = orderNumber,
                EntityType = entityType,
                EntityId = entityId,
                ChangedAt = DateTime.Now
            });
        }

        public static void RequestRefresh(
            AppDataRefreshType refreshTypes,
            string? sourceTerminalName = null,
            string? orderNumber = null,
            string? entityType = null,
            string? entityId = null)
        {
            RequestRefresh(new AppDataChangedEventArgs
            {
                Kind = ToPrimaryKind(refreshTypes, Array.Empty<AppDataChangedEventArgs>()),
                RefreshTypes = refreshTypes == AppDataRefreshType.None ? AppDataRefreshType.All : refreshTypes,
                SourceTerminalName = sourceTerminalName,
                OrderNumber = orderNumber,
                EntityType = entityType,
                EntityId = entityId,
                ChangedAt = DateTime.Now
            });
        }

        public static void RequestRefresh(AppDataChangedEventArgs args)
        {
            lock (SyncRoot)
            {
                var normalizedTypes = args.RefreshTypes == AppDataRefreshType.None
                    ? AppDataChangedEventArgs.ToRefreshType(args.Kind)
                    : args.RefreshTypes;

                PendingChanges.Add(args.RefreshTypes == AppDataRefreshType.None
                    ? new AppDataChangedEventArgs
                    {
                        Kind = args.Kind,
                        RefreshTypes = normalizedTypes,
                        SourceTerminalName = args.SourceTerminalName,
                        OrderNumber = args.OrderNumber,
                        EntityType = args.EntityType,
                        EntityId = args.EntityId,
                        ChangedAt = args.ChangedAt
                    }
                    : args);

                if ((args.Kind == AppDataChangeKind.Manual || args.Kind == AppDataChangeKind.All) &&
                    normalizedTypes == AppDataRefreshType.All)
                {
                    _pendingLegacyRefreshRequest = true;
                }

                if (_batchCts != null)
                {
                    return;
                }

                _batchCts = new CancellationTokenSource();
                _ = FlushAfterDelayAsync(_batchCts.Token);
            }
        }

        private static async Task FlushAfterDelayAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(RefreshBatchDelay, cancellationToken);

                AppDataChangedEventArgs merged;
                bool raiseLegacyRefresh;

                lock (SyncRoot)
                {
                    merged = MergePendingChanges(PendingChanges);
                    PendingChanges.Clear();
                    raiseLegacyRefresh = _pendingLegacyRefreshRequest;
                    _pendingLegacyRefreshRequest = false;
                    _batchCts?.Dispose();
                    _batchCts = null;
                }

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        if (raiseLegacyRefresh)
                        {
                            RefreshRequested?.Invoke(null, EventArgs.Empty);
                        }

                        DataChanged?.Invoke(null, merged);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"App data refresh handler failed: {ex.Message}");
                    }
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"App data refresh batch failed: {ex.Message}");
                lock (SyncRoot)
                {
                    PendingChanges.Clear();
                    _pendingLegacyRefreshRequest = false;
                    _batchCts?.Dispose();
                    _batchCts = null;
                }
            }
        }

        private static AppDataChangedEventArgs MergePendingChanges(IReadOnlyList<AppDataChangedEventArgs> changes)
        {
            if (changes.Count == 0)
            {
                return new AppDataChangedEventArgs
                {
                    Kind = AppDataChangeKind.Manual,
                    RefreshTypes = AppDataRefreshType.All,
                    ChangedAt = DateTime.Now
                };
            }

            var refreshTypes = changes.Aggregate(
                AppDataRefreshType.None,
                (current, change) => current | (change.RefreshTypes == AppDataRefreshType.None
                    ? AppDataChangedEventArgs.ToRefreshType(change.Kind)
                    : change.RefreshTypes));

            if (refreshTypes == AppDataRefreshType.None)
            {
                refreshTypes = AppDataRefreshType.All;
            }

            var kind = ToPrimaryKind(refreshTypes, changes);
            return new AppDataChangedEventArgs
            {
                Kind = kind,
                RefreshTypes = refreshTypes,
                SourceTerminalName = CollapseSameValue(changes.Select(change => change.SourceTerminalName)),
                OrderNumber = CollapseSameValue(changes.Select(change => change.OrderNumber)),
                EntityType = CollapseSameValue(changes.Select(change => change.EntityType)),
                EntityId = CollapseSameValue(changes.Select(change => change.EntityId)),
                ChangedAt = changes.Max(change => change.ChangedAt)
            };
        }

        private static AppDataChangeKind ToPrimaryKind(
            AppDataRefreshType refreshTypes,
            IReadOnlyList<AppDataChangedEventArgs> changes)
        {
            if (refreshTypes == AppDataRefreshType.All ||
                changes.Any(change => change.Kind == AppDataChangeKind.All || change.Kind == AppDataChangeKind.Manual))
            {
                return AppDataChangeKind.All;
            }

            if (refreshTypes == AppDataRefreshType.Orders)
            {
                return AppDataChangeKind.Orders;
            }

            if (refreshTypes == AppDataRefreshType.Tables)
            {
                return AppDataChangeKind.TableLayout;
            }

            if (refreshTypes == AppDataRefreshType.Reservations)
            {
                return AppDataChangeKind.Reservations;
            }

            if (refreshTypes == AppDataRefreshType.Settings)
            {
                return AppDataChangeKind.Settings;
            }

            if (refreshTypes == AppDataRefreshType.Printers)
            {
                return AppDataChangeKind.Printers;
            }

            return AppDataChangeKind.All;
        }

        private static string? CollapseSameValue(IEnumerable<string?> values)
        {
            var distinct = values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .ToList();

            return distinct.Count == 1 ? distinct[0] : null;
        }
    }
}
