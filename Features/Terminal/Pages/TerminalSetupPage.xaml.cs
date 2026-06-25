using MySqlConnector;
using POS_in_NET.Models;
using POS_in_NET.Services;

namespace POS_in_NET.Pages;

public partial class TerminalSetupPage : ContentPage
{
    private TerminalMode _selectedMode = TerminalMode.Mother;

    public TerminalSetupPage()
    {
        InitializeComponent();
        LoadExistingConfiguration();
        UpdateModeUi();
    }

    private void LoadExistingConfiguration()
    {
        var config = TerminalConfigurationService.GetConfiguration();
        _selectedMode = config.Mode;
        TerminalNameEntry.Text = config.TerminalName;
        MotherIpEntry.Text = config.IsChild ? config.DatabaseHost : string.Empty;
        PairingCodeEntry.Text = string.Empty;
    }

    private void OnMotherTapped(object sender, TappedEventArgs e)
    {
        _selectedMode = TerminalMode.Mother;
        if (string.IsNullOrWhiteSpace(TerminalNameEntry.Text) || TerminalNameEntry.Text.Equals("Terminal", StringComparison.OrdinalIgnoreCase))
        {
            TerminalNameEntry.Text = "Main";
        }
        UpdateModeUi();
    }

    private void OnChildTapped(object sender, TappedEventArgs e)
    {
        _selectedMode = TerminalMode.Child;
        if (string.IsNullOrWhiteSpace(TerminalNameEntry.Text) || TerminalNameEntry.Text.Equals("Main", StringComparison.OrdinalIgnoreCase))
        {
            TerminalNameEntry.Text = "Floor 1";
        }
        UpdateModeUi();
    }

    private void UpdateModeUi()
    {
        var isMother = _selectedMode == TerminalMode.Mother;

        MotherCard.BackgroundColor = Color.FromArgb(isMother ? "#ECFDF5" : "#F8FAFC");
        MotherCard.Stroke = Color.FromArgb(isMother ? "#10B981" : "#CBD5E1");
        MotherCard.StrokeThickness = isMother ? 3 : 2;

        ChildCard.BackgroundColor = Color.FromArgb(isMother ? "#F8FAFC" : "#EFF6FF");
        ChildCard.Stroke = Color.FromArgb(isMother ? "#CBD5E1" : "#2563EB");
        ChildCard.StrokeThickness = isMother ? 2 : 3;

        MotherIpSection.IsVisible = !isMother;
        PairingCodeSection.IsVisible = !isMother;
        StatusLabel.Text = isMother
            ? "This terminal will use its own local database."
            : "This terminal will connect to the mother terminal database with a pairing code.";
    }

    private async void OnContinueClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TerminalNameEntry.Text))
        {
            StatusLabel.TextColor = Color.FromArgb("#DC2626");
            StatusLabel.Text = "Please enter a terminal name.";
            return;
        }

        if (_selectedMode == TerminalMode.Child && string.IsNullOrWhiteSpace(MotherIpEntry.Text))
        {
            StatusLabel.TextColor = Color.FromArgb("#DC2626");
            StatusLabel.Text = "Please enter the mother terminal IP address.";
            return;
        }

        if (_selectedMode == TerminalMode.Child && string.IsNullOrWhiteSpace(PairingCodeEntry.Text))
        {
            StatusLabel.TextColor = Color.FromArgb("#DC2626");
            StatusLabel.Text = "Please enter the pairing code from the mother terminal.";
            return;
        }

        ContinueButton.IsEnabled = false;
        ContinueButton.Text = "Testing...";
        StatusLabel.TextColor = Color.FromArgb("#64748B");
        StatusLabel.Text = "Testing database connection...";

        var config = new TerminalConfiguration
        {
            IsConfigured = true,
            Mode = _selectedMode,
            TerminalName = TerminalNameEntry.Text.Trim(),
            DatabaseHost = _selectedMode == TerminalMode.Mother ? "localhost" : MotherIpEntry.Text.Trim(),
            DatabasePort = 3306,
            DatabaseName = "Pos-net",
            DatabaseUser = "root",
            DatabasePassword = "root"
        };

        TerminalConfigurationService.Save(config);

        if (config.IsMother)
        {
            var initialized = await new DatabaseService().InitializeDatabaseAsync();
            if (!initialized)
            {
                TerminalConfigurationService.SetConfigured(false);
                ContinueButton.IsEnabled = true;
                ContinueButton.Text = "Save & Continue";
                StatusLabel.TextColor = Color.FromArgb("#DC2626");
                StatusLabel.Text = "Could not create or initialize the local database.";
                return;
            }
        }

        var testResult = await TerminalConnectionTestService.TestAsync();
        if (!testResult.Success)
        {
            TerminalConfigurationService.SetConfigured(false);
            ContinueButton.IsEnabled = true;
            ContinueButton.Text = "Save & Continue";
            StatusLabel.TextColor = Color.FromArgb("#DC2626");
            StatusLabel.Text = testResult.Message;
            return;
        }

        if (config.IsChild)
        {
            StatusLabel.Text = "Validating pairing code...";
            var pairingResult = await TerminalPairingService.ValidateAndActivateChildAsync(
                config.TerminalName,
                PairingCodeEntry.Text);
            if (!pairingResult.Success)
            {
                TerminalConfigurationService.SetConfigured(false);
                ContinueButton.IsEnabled = true;
                ContinueButton.Text = "Save & Continue";
                StatusLabel.TextColor = Color.FromArgb("#DC2626");
                StatusLabel.Text = pairingResult.Message;
                return;
            }
        }

        await EnsureStartupDataAsync();

        StatusLabel.TextColor = Color.FromArgb("#059669");
        StatusLabel.Text = testResult.Message;
        TerminalPowerSafetyService.Apply();
        ServiceHelper.GetService<TerminalHealthService>()?.Start();
        DatabaseChangeMonitorService.Start();
        await Shell.Current.GoToAsync("//login", false);
    }

    private static async Task EnsureStartupDataAsync()
    {
        try
        {
            var authService = AuthenticationService.Instance;
            var connectionTest = await authService.TestDatabaseConnectionAsync();
            if (!connectionTest.Success)
            {
                return;
            }

            await authService.EnsureDefaultAdminUserAsync();
            await authService.EnsureUserExistsAsync("Admin", "0000", "0000", UserRole.Admin);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Terminal setup startup data warning: {ex.Message}");
        }
    }
}
