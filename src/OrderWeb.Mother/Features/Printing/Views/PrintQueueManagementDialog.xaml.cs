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
    private bool _busy;

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
        if (_completionSource != null && !_busy)
        {
            await RefreshAsync();
        }
    }

    private async Task RefreshAsync()
    {
        SetBusy(true);
        try
        {
            var scope = SelectedScope;
            _snapshot = await _queueService.GetManagementSnapshotAsync(scope.PrinterId);
            WaitingCountLabel.Text = _snapshot.WaitingJobs.ToString();
            FailedCountLabel.Text = (_snapshot.FailedJobs + _snapshot.NeedsAttentionJobs).ToString();
            PrintingCountLabel.Text = _snapshot.PrintingJobs.ToString();
            PreviousDayCountLabel.Text = _snapshot.PreviousDayJobs.ToString();

            var jobs = await _queueService.GetStaffAttentionJobsAsync(scope.PrinterId);
            JobsList.ItemsSource = jobs.ToList();

            CancelOldButton.IsEnabled = _snapshot.PreviousDayJobs > 0;
            CancelAllButton.IsEnabled = _snapshot.WaitingJobs > 0;
            RetryFailedButton.IsEnabled = (_snapshot.FailedJobs + _snapshot.NeedsAttentionJobs) > 0;
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
        _busy = busy;
        LoadingLabel.IsVisible = busy;
        ScopePicker.IsEnabled = !busy;
        if (busy)
        {
            CancelOldButton.IsEnabled = false;
            CancelAllButton.IsEnabled = false;
            RetryFailedButton.IsEnabled = false;
        }
    }

    private static bool TryGetJobId(object? sender, out int jobId)
    {
        jobId = 0;
        if (sender is not Button button || button.CommandParameter == null)
        {
            return false;
        }

        try
        {
            jobId = Convert.ToInt32(button.CommandParameter);
            return jobId > 0;
        }
        catch
        {
            return false;
        }
    }

    private async void OnRetryJobClicked(object? sender, EventArgs e)
    {
        if (_busy || !TryGetJobId(sender, out var jobId))
        {
            return;
        }

        SetBusy(true);
        try
        {
            var ok = await _queueService.RetryJobByIdAsync(jobId);
            if (!ok)
            {
                await AppAlertService.ShowAlertAsync("Retry", "Could not retry that job — it may already be gone.");
            }
        }
        finally
        {
            SetBusy(false);
            await RefreshAsync();
        }
    }

    private async void OnReprintJobClicked(object? sender, EventArgs e)
    {
        if (_busy || !TryGetJobId(sender, out var jobId))
        {
            return;
        }

        var confirm = new ModernConfirmDialog();
        confirm.SetConfirm(
            "Reprint ticket",
            "This may print a duplicate ticket. Continue?",
            "Reprint",
            "Cancel",
            "!",
            "#0F766E");
        if (!await confirm.ShowAsync())
        {
            return;
        }

        var user = AuthenticationService.Instance.CurrentUser;
        if (user == null)
        {
            await AppAlertService.ShowAlertAsync("Denied", "Sign in as Manager or Admin to reprint.");
            return;
        }

        SetBusy(true);
        try
        {
            var (ok, _, message) = await _queueService.ReprintJobByIdAsync(
                jobId,
                user.Id,
                string.IsNullOrWhiteSpace(user.Name) ? user.Username : user.Name);
            await AppAlertService.ShowAlertAsync(ok ? "Reprint" : "Reprint failed", message);
        }
        finally
        {
            SetBusy(false);
            await RefreshAsync();
        }
    }

    private async void OnDismissJobClicked(object? sender, EventArgs e)
    {
        if (_busy || !TryGetJobId(sender, out var jobId))
        {
            return;
        }

        var prompt = new StyledPromptDialog();
        prompt.SetDialog(
            "Dismiss print job",
            "Reason required (why this ticket will not print):",
            "e.g. Already printed by hand",
            Keyboard.Text);
        prompt.SetOkText("Dismiss");
        prompt.SetCancelText("Cancel");
        var reason = await prompt.ShowAsync();
        if (string.IsNullOrWhiteSpace(reason))
        {
            return;
        }

        var user = AuthenticationService.Instance.CurrentUser;
        if (user == null)
        {
            await AppAlertService.ShowAlertAsync("Denied", "Sign in as Manager or Admin to dismiss.");
            return;
        }

        SetBusy(true);
        try
        {
            var ok = await _queueService.DismissJobByIdAsync(
                jobId,
                user.Id,
                string.IsNullOrWhiteSpace(user.Name) ? user.Username : user.Name,
                reason.Trim());
            if (!ok)
            {
                await AppAlertService.ShowAlertAsync("Dismiss", "Could not dismiss that job.");
            }
        }
        finally
        {
            SetBusy(false);
            await RefreshAsync();
        }
    }

    private async void OnCancelOldClicked(object? sender, EventArgs e)
    {
        if (_busy)
        {
            return;
        }

        await RunBulkCancelAsync(PrintQueueManagementAction.CancelPreviousDays);
    }

    private async void OnCancelAllClicked(object? sender, EventArgs e)
    {
        if (_busy)
        {
            return;
        }

        await RunBulkCancelAsync(PrintQueueManagementAction.CancelAllWaiting);
    }

    private async void OnRetryFailedClicked(object? sender, EventArgs e)
    {
        if (_busy)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var retried = await _queueService.RetryAllFailedJobsAsync(SelectedScope.PrinterId);
            await AppAlertService.ShowAlertAsync("Retry", $"{retried} failed print job(s) queued for retry.");
        }
        finally
        {
            SetBusy(false);
            await RefreshAsync();
        }
    }

    private async Task RunBulkCancelAsync(PrintQueueManagementAction action)
    {
        var user = AuthenticationService.Instance.CurrentUser;
        if (user == null || user.Role is not (UserRole.Manager or UserRole.Admin))
        {
            await AppAlertService.ShowAlertAsync("Denied", "Manager or admin access is required.");
            return;
        }

        var cutoff = action == PrintQueueManagementAction.CancelPreviousDays
            ? DateTime.Today.AddMilliseconds(-1)
            : DateTime.Now;
        var candidateCount = action == PrintQueueManagementAction.CancelPreviousDays
            ? _snapshot.PreviousDayJobs
            : _snapshot.WaitingJobs;
        var actionName = action == PrintQueueManagementAction.CancelPreviousDays
            ? "previous-day"
            : "waiting";
        var scope = SelectedScope;

        var confirm = new ModernConfirmDialog();
        confirm.SetConfirm(
            "Cancel Print Jobs",
            $"Cancel {candidateCount} {actionName} print job(s) for {scope.DisplayName}? Jobs already printing will not be cancelled.",
            "Cancel Jobs",
            "Keep Jobs",
            "!",
            "#DC2626");
        if (!await confirm.ShowAsync())
        {
            return;
        }

        SetBusy(true);
        try
        {
            var result = await _queueService.CancelWaitingJobsAsync(
                cutoff,
                scope.PrinterId,
                user.Id,
                string.IsNullOrWhiteSpace(user.Name) ? user.Username : user.Name,
                $"Cancelled by {user.Username} from Print problems ({actionName})",
                scope.PrinterId.HasValue ? "printer" : "all_printers");
            await AppAlertService.ShowAlertAsync(
                "Complete",
                $"{result.CancelledJobs} print job(s) cancelled.");
        }
        finally
        {
            SetBusy(false);
            await RefreshAsync();
        }
    }

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
