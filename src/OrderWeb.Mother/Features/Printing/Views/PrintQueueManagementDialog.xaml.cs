using POS_in_NET.Models;
using POS_in_NET.Services;

namespace POS_in_NET.Views;

public enum PrintQueueManagementAction
{
    Close,
    CancelPreviousDays,
    CancelAllWaiting,
    RetryFailed
}

public sealed record PrintQueueManagementSelection(
    PrintQueueManagementAction Action,
    int? PrinterId,
    string ScopeName,
    PrintQueueManagementSnapshot Snapshot);

public partial class PrintQueueManagementDialog : ContentView
{
    private readonly NetworkPrintQueueService _queueService;
    private readonly List<QueueScopeOption> _scopeOptions;
    private TaskCompletionSource<PrintQueueManagementSelection>? _completionSource;
    private Grid? _parentGrid;
    private PrintQueueManagementSnapshot _snapshot = new();

    public PrintQueueManagementDialog(
        NetworkPrintQueueService queueService,
        IEnumerable<NetworkPrinter> printers)
    {
        InitializeComponent();
        _queueService = queueService;
        _scopeOptions =
        [
            new QueueScopeOption(null, "All printers"),
            .. printers.OrderBy(printer => printer.Name)
                .Select(printer => new QueueScopeOption(printer.Id, $"{printer.Name} ({printer.IpAddress}:{printer.Port})"))
        ];
        ScopePicker.ItemsSource = _scopeOptions;
        ScopePicker.SelectedIndex = 0;
    }

    public async Task<PrintQueueManagementSelection> ShowAsync()
    {
        using var idleGuard = ServiceHelper.GetService<InactivityService>()?.BeginCriticalActivity();
        _completionSource = new TaskCompletionSource<PrintQueueManagementSelection>();
        if (!DialogOverlayHelper.TryAttachOverlay(this, out _parentGrid))
        {
            return new PrintQueueManagementSelection(
                PrintQueueManagementAction.Close,
                null,
                "All printers",
                new PrintQueueManagementSnapshot());
        }

        await RefreshAsync();
        return await _completionSource.Task;
    }

    private async void OnScopeChanged(object? sender, EventArgs e)
    {
        if (_completionSource != null)
        {
            await RefreshAsync();
        }
    }

    private async Task RefreshAsync()
    {
        SetBusy(true);
        try
        {
            _snapshot = await _queueService.GetManagementSnapshotAsync(SelectedScope.PrinterId);
            WaitingCountLabel.Text = _snapshot.WaitingJobs.ToString();
            FailedCountLabel.Text = _snapshot.FailedJobs.ToString();
            PrintingCountLabel.Text = _snapshot.PrintingJobs.ToString();
            PreviousDayCountLabel.Text = _snapshot.PreviousDayJobs.ToString();
            OldestJobLabel.Text = _snapshot.OldestWaitingJob?.ToString("dd/MM/yyyy HH:mm") ?? "None";
            LastCancellationLabel.Text = _snapshot.LastCancellationAt.HasValue
                ? $"{_snapshot.LastCancellationAt:dd/MM/yyyy HH:mm} ({_snapshot.LastCancellationCount})"
                : "None";
            CancelOldButton.IsEnabled = _snapshot.PreviousDayJobs > 0;
            CancelAllButton.IsEnabled = _snapshot.WaitingJobs > 0;
            RetryFailedButton.IsEnabled = _snapshot.FailedJobs > 0;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private QueueScopeOption SelectedScope =>
        ScopePicker.SelectedItem as QueueScopeOption ?? _scopeOptions[0];

    private void SetBusy(bool busy)
    {
        LoadingLabel.IsVisible = busy;
        ScopePicker.IsEnabled = !busy;
        if (busy)
        {
            CancelOldButton.IsEnabled = false;
            CancelAllButton.IsEnabled = false;
            RetryFailedButton.IsEnabled = false;
        }
    }

    private void OnCancelOldClicked(object? sender, EventArgs e) =>
        Complete(PrintQueueManagementAction.CancelPreviousDays);

    private void OnCancelAllClicked(object? sender, EventArgs e) =>
        Complete(PrintQueueManagementAction.CancelAllWaiting);

    private void OnRetryFailedClicked(object? sender, EventArgs e) =>
        Complete(PrintQueueManagementAction.RetryFailed);

    private void OnCloseClicked(object? sender, EventArgs e) =>
        Complete(PrintQueueManagementAction.Close);

    private void Complete(PrintQueueManagementAction action)
    {
        var scope = SelectedScope;
        _completionSource?.TrySetResult(new PrintQueueManagementSelection(
            action,
            scope.PrinterId,
            scope.DisplayName,
            _snapshot));
        DialogOverlayHelper.DetachOverlay(this, _parentGrid);
        _parentGrid = null;
    }

    private sealed record QueueScopeOption(int? PrinterId, string DisplayName);
}
