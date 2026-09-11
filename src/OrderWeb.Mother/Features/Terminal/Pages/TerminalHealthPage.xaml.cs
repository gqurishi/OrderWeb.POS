using System.Collections.ObjectModel;
using POS_in_NET.Models;
using POS_in_NET.Services;
using POS_in_NET.Views;

namespace POS_in_NET.Pages;

public partial class TerminalHealthPage : ContentPage
{
    private readonly TerminalHealthService _terminalHealthService;
    private readonly ClientWebSocketBroadcastService _clientApiService;
    private readonly ClientTerminalAccessService _clientAccessService;
    private readonly DatabaseBackupService _databaseBackupService;
    private bool _isLoading;
    private string _summaryText = "Loading terminal health...";
    private string _onlineCountText = "0";
    private string _offlineCountText = "0";
    private string _pairedClientsText = "0";
    private string _lastCheckText = "Never";
    private string _backupStatusText = "Full database backups run every 3 days on the mother terminal; the latest 15 are retained.";

    public ObservableCollection<TerminalHealthStatus> Terminals { get; } = new();

    public string SummaryText
    {
        get => _summaryText;
        set
        {
            if (_summaryText != value)
            {
                _summaryText = value;
                OnPropertyChanged();
            }
        }
    }

    public string OnlineCountText
    {
        get => _onlineCountText;
        set
        {
            if (_onlineCountText != value)
            {
                _onlineCountText = value;
                OnPropertyChanged();
            }
        }
    }

    public string OfflineCountText
    {
        get => _offlineCountText;
        set
        {
            if (_offlineCountText != value)
            {
                _offlineCountText = value;
                OnPropertyChanged();
            }
        }
    }

    public string PairedClientsText
    {
        get => _pairedClientsText;
        set
        {
            if (_pairedClientsText != value)
            {
                _pairedClientsText = value;
                OnPropertyChanged();
            }
        }
    }

    public string LastCheckText
    {
        get => _lastCheckText;
        set
        {
            if (_lastCheckText != value)
            {
                _lastCheckText = value;
                OnPropertyChanged();
            }
        }
    }

    public string BackupStatusText
    {
        get => _backupStatusText;
        set
        {
            if (_backupStatusText != value)
            {
                _backupStatusText = value;
                OnPropertyChanged();
            }
        }
    }

    public string CurrentTerminalText
    {
        get
        {
            var config = TerminalConfigurationService.GetConfiguration();
            return $"{config.TerminalName} · {config.ModeDisplay}";
        }
    }

    public bool CanRunBackup => TerminalRoleService.CanRunMotherJobs;
    public bool CanAddChildTerminal => TerminalRoleService.CanRunMotherJobs;

    public string MotherIpText => TerminalNetworkInfoService.GetBestLocalIpAddress();

    public string MotherPortText =>
        (_clientApiService?.Port > 0 ? _clientApiService.Port : ClientWebSocketBroadcastService.DefaultPort).ToString();

    public string ApiStatusText
    {
        get
        {
            if (!TerminalConfigurationService.IsMotherTerminal)
            {
                return "Client terminals do not run the Mother API.";
            }

            if (_clientApiService == null)
            {
                return "API service not initialized.";
            }

            return _clientApiService.IsRunning
                ? $"API running on port {_clientApiService.Port}"
                : $"API stopped: {_clientApiService.StatusMessage}";
        }
    }

    public string WebSocketStatusText
    {
        get
        {
            if (!TerminalConfigurationService.IsMotherTerminal)
            {
                return "/ws is hosted by the Mother terminal.";
            }

            if (_clientApiService == null)
            {
                return "/ws service not initialized.";
            }

            return _clientApiService.IsRunning
                ? $"/ws ready, connected clients: {_clientApiService.ConnectedClientCount}"
                : "/ws stopped";
        }
    }

    public string MotherConnectionText
    {
        get
        {
            return $"Mother IP: {MotherIpText}   Port: {MotherPortText}";
        }
    }

    public string SleepSafetyText => TerminalConfigurationService.IsMotherTerminal
        ? "Mother terminal sleep is disabled while the app is running, keeping reports, backups, printing, and live updates awake."
        : "Sleep safety is controlled on the Mother terminal. Keep this Client terminal awake during service if it is used for active ordering.";

    public TerminalHealthPage()
    {
        InitializeComponent();
        BindingContext = this;
        TopBar.SetPageTitle("Terminal Health");

        _terminalHealthService = ServiceHelper.GetService<TerminalHealthService>()
            ?? new TerminalHealthService(new DatabaseService());
        var databaseService = ServiceHelper.GetService<DatabaseService>() ?? new DatabaseService();
        _clientAccessService = ServiceHelper.GetService<ClientTerminalAccessService>()
            ?? new ClientTerminalAccessService(databaseService);
        _clientApiService = ServiceHelper.GetService<ClientWebSocketBroadcastService>()
            ?? new ClientWebSocketBroadcastService(
                databaseService,
                AuthenticationService.Instance,
                new PermissionService(databaseService, AuthenticationService.Instance),
                new ReservationSyncService(databaseService),
                _clientAccessService);
        _databaseBackupService = ServiceHelper.GetService<DatabaseBackupService>()
            ?? new DatabaseBackupService(databaseService);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        TerminalPowerSafetyService.Apply();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_isLoading)
        {
            return;
        }

        _isLoading = true;
        try
        {
            var healthService = _terminalHealthService;
            var statuses = await Task.Run(async () =>
            {
                await healthService.UpdateCurrentTerminalAsync();
                return await healthService.GetTerminalStatusesAsync();
            }).ConfigureAwait(true);

            var onlineCount = statuses.Count(status => status.IsOnline);
            var pairedClients = statuses.Count(status => !status.IsMother && status.PairedAt.HasValue);
            var offlineCount = statuses.Count - onlineCount;
            var lastCheck = DateTime.Now.ToString("HH:mm:ss");
            var summary = $"Mother API {MotherIpText}:{MotherPortText} · {pairedClients} paired Client POS.";
            var backupStatus = BuildBackupStatus();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Terminals.Clear();
                foreach (var status in statuses)
                {
                    Terminals.Add(status);
                }

                OnlineCountText = onlineCount.ToString();
                OfflineCountText = offlineCount.ToString();
                PairedClientsText = pairedClients.ToString();
                LastCheckText = lastCheck;
                SummaryText = summary;
                BackupStatusText = backupStatus;
                OnPropertyChanged(nameof(MotherIpText));
                OnPropertyChanged(nameof(MotherPortText));
                OnPropertyChanged(nameof(MotherConnectionText));
                OnPropertyChanged(nameof(ApiStatusText));
                OnPropertyChanged(nameof(WebSocketStatusText));
            });
        }
        catch (Exception ex)
        {
            var summary = TerminalConfigurationService.IsChildTerminal
                ? "Mother terminal disconnected. Health data cannot be refreshed."
                : "Could not refresh terminal health.";
            var backupStatus = BuildBackupStatus();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                SummaryText = summary;
                BackupStatusText = backupStatus;
            });

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await AppAlertService.ShowAlertAsync("Terminal Health", ex.Message);
            });
        }
        finally
        {
            _isLoading = false;
        }
    }

    private string BuildBackupStatus()
    {
        if (!TerminalRoleService.CanRunMotherJobs)
        {
            return "Full database backups run every 3 days on the mother terminal only; the latest 15 are retained.";
        }

        if (_databaseBackupService.LastBackupAt <= DateTime.MinValue.AddDays(1))
        {
            return "Three-day backup scheduler is ready. No backup has completed in this app session yet.";
        }

        var location = string.IsNullOrWhiteSpace(_databaseBackupService.LastBackupPath)
            ? "backup folder"
            : _databaseBackupService.LastBackupPath;

        return $"Last backup: {_databaseBackupService.LastBackupAt:dd MMM yyyy HH:mm:ss}\n{location}";
    }

    private async void OnRefreshClicked(object sender, EventArgs e)
    {
        await LoadAsync();
    }

    private async void OnAddChildTerminalClicked(object sender, EventArgs e)
    {
        if (!TerminalRoleService.CanRunMotherJobs)
        {
            await AppAlertService.ShowAlertAsync("Mother Terminal Only", "Client POS terminals are added from the mother terminal.");
            return;
        }

        var terminalPrompt = new StyledPromptDialog();
        terminalPrompt.SetDialog(
            "Add Client POS",
            "Enter a unique Client POS name, e.g. Floor 1, Bar, Counter 2.",
            "Floor 1",
            Keyboard.Text,
            "Floor 1",
            useVirtualKeyboard: true);
        terminalPrompt.SetOkText("Create Code");
        terminalPrompt.SetCancelText("Cancel");

        var terminalName = await terminalPrompt.ShowAsync();

        if (string.IsNullOrWhiteSpace(terminalName))
        {
            return;
        }

        try
        {
            var result = await TerminalPairingService.CreateChildPairingAsync(terminalName);
            if (!result.Success)
            {
                await AppAlertService.ShowAlertAsync("Add Client POS", result.Message);
                return;
            }

            var motherIp = TerminalNetworkInfoService.GetBestLocalIpAddress();
            await LoadAsync();
            await AppAlertService.ShowAlertAsync(
                "Client POS Pairing Code",
                $"Terminal: {terminalName.Trim()}\nCode: {result.PairingCode}\n\nThis code never expires and stays on Terminal Health.\nReuse it anytime to connect or reconnect Client POS.\n\nMother IP: {motherIp}\nAPI Port: {ClientWebSocketBroadcastService.DefaultPort}\nPairing Code: {result.PairingCode}");
        }
        catch (Exception ex)
        {
            await AppAlertService.ShowAlertAsync("Add Client POS", ex.Message);
        }
    }

    private async void OnReconfigureTerminalClicked(object sender, EventArgs e)
    {
        var confirmDialog = new ModernConfirmDialog();
        confirmDialog.SetConfirm(
            "Reconfigure Terminal",
            "Open terminal setup for this device? Use carefully during service.",
            "Open Setup",
            "Cancel",
            "!",
            "#2563EB");

        var confirm = await confirmDialog.ShowAsync();
        if (!confirm)
        {
            return;
        }

        await NavigationCoordinator.Shared.NavigateShellAsync("terminalsetup", animated: false, source: sender as VisualElement);
    }

    private async void OnDeleteTerminalClicked(object sender, EventArgs e)
    {
        if (sender is not Button button || button.BindingContext is not TerminalHealthStatus terminal)
        {
            return;
        }

        if (!TerminalRoleService.CanRunMotherJobs)
        {
            await AppAlertService.ShowAlertAsync("Mother Terminal Only", "Terminals can be deleted from the mother terminal only.");
            return;
        }

        if (!terminal.CanDeleteTerminal)
        {
            await AppAlertService.ShowAlertAsync("Cannot Delete Terminal", "Only offline, pending, disabled, or revoked Client terminals can be deleted.");
            return;
        }

        var confirmDialog = new ModernConfirmDialog();
        confirmDialog.SetConfirm(
            "Delete Terminal",
            $"Remove {terminal.TerminalName} from Terminal Health? This will delete its stale heartbeat and pairing record.",
            "Delete",
            "Cancel",
            "!",
            "#DC2626");

        var confirm = await confirmDialog.ShowAsync();
        if (!confirm)
        {
            return;
        }

        try
        {
            var result = await _terminalHealthService.DeleteTerminalAsync(terminal.TerminalName);
            if (result.Success)
            {
                await LoadAsync();
            }

            await AppAlertService.ShowAlertAsync(
                result.Success ? "Terminal Deleted" : "Delete Terminal",
                result.Message);
        }
        catch (Exception ex)
        {
            await AppAlertService.ShowAlertAsync("Delete Terminal", ex.Message);
        }
    }

    private async void OnDisableTerminalClicked(object sender, EventArgs e)
    {
        if (sender is not Button button || button.BindingContext is not TerminalHealthStatus terminal)
        {
            return;
        }

        var confirm = await ConfirmClientActionAsync(
            "Disable Client POS",
            $"Disable {terminal.TerminalName}? The Client will no longer be allowed to use the Mother API.",
            "Disable",
            "#DC2626");
        if (!confirm)
        {
            return;
        }

        await RunClientActionAsync(() => _terminalHealthService.DisableTerminalAsync(terminal.TerminalName), "Disable Client POS");
    }

    private async void OnRevokeTerminalClicked(object sender, EventArgs e)
    {
        if (sender is not Button button || button.BindingContext is not TerminalHealthStatus terminal)
        {
            return;
        }

        var confirm = await ConfirmClientActionAsync(
            "Revoke Token",
            $"Revoke the saved terminal token for {terminal.TerminalName}? It must be paired again before it can connect.",
            "Revoke",
            "#DC2626");
        if (!confirm)
        {
            return;
        }

        await RunClientActionAsync(() => _terminalHealthService.RevokeTerminalTokenAsync(terminal.TerminalName), "Revoke Token");
    }

    private async void OnForceLogoutTerminalClicked(object sender, EventArgs e)
    {
        if (sender is not Button button || button.BindingContext is not TerminalHealthStatus terminal)
        {
            return;
        }

        await RunClientActionAsync(() => _terminalHealthService.ForceLogoutTerminalAsync(terminal.TerminalName), "Force Logout");
    }

    private async void OnClientAccessClicked(object sender, EventArgs e)
    {
        if (sender is not Button button || button.BindingContext is not TerminalHealthStatus terminal)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(terminal.TerminalId))
        {
            await AppAlertService.ShowAlertAsync("Client Access", "This Client must finish pairing before Mother can set its access list.");
            return;
        }

        try
        {
            var granted = await _clientAccessService.GetGrantedFeaturesAsync(terminal.TerminalId);
            var dialog = new ClientTerminalAccessDialog();
            dialog.SetGrantedFeatures(granted);
            var selected = await dialog.ShowAsync();
            if (selected is null)
            {
                return;
            }

            await _clientAccessService.SaveGrantedFeaturesAsync(terminal.TerminalId, selected);
            await _clientApiService.PublishDataChangedAsync("features.updated", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
            await AppAlertService.ShowAlertAsync(
                "Client Access Saved",
                $"{terminal.TerminalName} will pick this up on Update All, when opening Gift Cards/Loyalty, or at the next PIN login. Reservations stay on by default so Client matches Mother.");
        }
        catch (Exception ex)
        {
            await AppAlertService.ShowAlertAsync("Client Access", ex.Message);
        }
    }

    private async void OnRenameTerminalClicked(object sender, EventArgs e)
    {
        if (sender is not Button button || button.BindingContext is not TerminalHealthStatus terminal)
        {
            return;
        }

        var prompt = new StyledPromptDialog();
        prompt.SetDialog(
            "Rename Client POS",
            "Enter the new Client terminal name.",
            terminal.TerminalName,
            Keyboard.Text,
            terminal.TerminalName,
            useVirtualKeyboard: true);
        prompt.SetOkText("Rename");
        prompt.SetCancelText("Cancel");

        var newName = await prompt.ShowAsync();
        if (string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        await RunClientActionAsync(() => _terminalHealthService.RenameTerminalAsync(terminal.TerminalName, newName), "Rename Client POS");
    }

    private static async Task<bool> ConfirmClientActionAsync(string title, string message, string okText, string color)
    {
        var confirmDialog = new ModernConfirmDialog();
        confirmDialog.SetConfirm(title, message, okText, "Cancel", "!", color);
        return await confirmDialog.ShowAsync();
    }

    private async Task RunClientActionAsync(Func<Task<(bool Success, string Message)>> action, string title)
    {
        if (!TerminalRoleService.CanRunMotherJobs)
        {
            await AppAlertService.ShowAlertAsync("Mother Terminal Only", "Client terminals are managed from the mother terminal.");
            return;
        }

        try
        {
            var result = await action();
            if (result.Success)
            {
                await LoadAsync();
            }

            await AppAlertService.ShowAlertAsync(title, result.Message);
        }
        catch (Exception ex)
        {
            await AppAlertService.ShowAlertAsync(title, ex.Message);
        }
    }

    private async void OnBackupNowClicked(object sender, EventArgs e)
    {
        if (!TerminalRoleService.CanRunMotherJobs)
        {
            await AppAlertService.ShowAlertAsync("Mother Terminal Only", "Database backups run on the mother terminal only.");
            return;
        }

        var result = await _databaseBackupService.ExportDatabaseAsync();
        BackupStatusText = BuildBackupStatus();
        await AppAlertService.ShowAlertAsync(
            result.Success ? "Backup Complete" : "Backup Failed",
            result.Message);
    }
}
