using System.Collections.ObjectModel;
using POS_in_NET.Models;
using POS_in_NET.Services;
using POS_in_NET.Views;

namespace POS_in_NET.Pages;

public partial class TerminalHealthPage : ContentPage
{
    private readonly TerminalHealthService _terminalHealthService;
    private readonly DatabaseBackupService _databaseBackupService;
    private bool _isLoading;
    private string _summaryText = "Loading terminal health...";
    private string _onlineCountText = "0";
    private string _offlineCountText = "0";
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

    public string MotherConnectionText
    {
        get
        {
            var config = TerminalConfigurationService.GetConfiguration();
            var ipAddress = TerminalNetworkInfoService.GetBestLocalIpAddress();
            return $"Use this on each child terminal:\nMother IP: {ipAddress}\nPort: {config.DatabasePort}\nDatabase: {config.DatabaseName}\nEach child needs its own pairing code.";
        }
    }

    public string SleepSafetyText => TerminalConfigurationService.IsMotherTerminal
        ? "Mother terminal sleep is disabled while the app is running, keeping reports, backups, printing, and live updates awake."
        : "Sleep safety is controlled on the mother terminal. Keep this child terminal awake during service if it is used for active ordering.";

    public TerminalHealthPage()
    {
        InitializeComponent();
        BindingContext = this;
        TopBar.SetPageTitle("Terminal Health");

        _terminalHealthService = ServiceHelper.GetService<TerminalHealthService>()
            ?? new TerminalHealthService(new DatabaseService());
        _databaseBackupService = ServiceHelper.GetService<DatabaseBackupService>()
            ?? new DatabaseBackupService(new DatabaseService());
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
            await _terminalHealthService.UpdateCurrentTerminalAsync();
            var statuses = await _terminalHealthService.GetTerminalStatusesAsync();

            var onlineCount = statuses.Count(status => status.IsOnline);
            var offlineCount = statuses.Count - onlineCount;
            var lastCheck = DateTime.Now.ToString("HH:mm:ss");
            var summary = $"{statuses.Count} terminal(s) registered in the shared database.";
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
                LastCheckText = lastCheck;
                SummaryText = summary;
                BackupStatusText = backupStatus;
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
            await AppAlertService.ShowAlertAsync("Mother Terminal Only", "Child terminals are added from the mother terminal.");
            return;
        }

        var terminalPrompt = new StyledPromptDialog();
        terminalPrompt.SetDialog(
            "Add Child Terminal",
            "Enter a unique child terminal name, e.g. Floor 1, Bar, Counter 2.",
            "Floor 1",
            Keyboard.Text,
            "Floor 1");
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
                await AppAlertService.ShowAlertAsync("Add Child Terminal", result.Message);
                return;
            }

            var config = TerminalConfigurationService.GetConfiguration();
            var motherIp = TerminalNetworkInfoService.GetBestLocalIpAddress();
            await LoadAsync();
            await AppAlertService.ShowAlertAsync(
                "Child Pairing Code",
                $"Terminal: {terminalName.Trim()}\nCode: {result.PairingCode}\nExpires: {result.ExpiresAt:HH:mm}\n\nOn the child terminal choose Child Terminal and enter:\nMother IP: {motherIp}\nPort: {config.DatabasePort}\nDatabase: {config.DatabaseName}\nPairing Code: {result.PairingCode}");
        }
        catch (Exception ex)
        {
            await AppAlertService.ShowAlertAsync("Add Child Terminal", ex.Message);
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
            await AppAlertService.ShowAlertAsync("Cannot Delete Terminal", "Only offline or pending child terminals can be deleted.");
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
