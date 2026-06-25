using System;

namespace POS_in_NET.Services
{
    public enum AppDataChangeKind
    {
        Manual,
        Orders,
        TableLayout,
        All
    }

    public sealed class AppDataChangedEventArgs : EventArgs
    {
        public AppDataChangeKind Kind { get; init; } = AppDataChangeKind.Manual;
        public string? SourceTerminalName { get; init; }
        public string? OrderNumber { get; init; }
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

        public string ToastMessage => Kind == AppDataChangeKind.Orders
            ? $"Order updated from {SourceDisplayName}"
            : $"Layout updated from {SourceDisplayName}";
    }

    public static class AppDataRefreshService
    {
        public static event EventHandler? RefreshRequested;
        public static event EventHandler<AppDataChangedEventArgs>? DataChanged;

        public static void RequestRefresh()
        {
            RequestRefresh(new AppDataChangedEventArgs { Kind = AppDataChangeKind.Manual });
        }

        public static void RequestRefresh(AppDataChangeKind kind, string? sourceTerminalName = null, string? orderNumber = null)
        {
            RequestRefresh(new AppDataChangedEventArgs
            {
                Kind = kind,
                SourceTerminalName = sourceTerminalName,
                OrderNumber = orderNumber,
                ChangedAt = DateTime.Now
            });
        }

        public static void RequestRefresh(AppDataChangedEventArgs args)
        {
            try
            {
                if (args.Kind == AppDataChangeKind.Manual || args.Kind == AppDataChangeKind.All)
                {
                    RefreshRequested?.Invoke(null, EventArgs.Empty);
                }

                DataChanged?.Invoke(null, args);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"App data refresh handler failed: {ex.Message}");
            }
        }
    }
}
