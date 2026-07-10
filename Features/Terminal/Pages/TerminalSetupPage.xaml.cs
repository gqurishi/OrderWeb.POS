using POS_in_NET.Models;
using POS_in_NET.Services;

namespace POS_in_NET.Pages;

public partial class TerminalSetupPage : ContentPage
{
    private TerminalMode _selectedMode = TerminalMode.Mother;
    private bool _installerConfigApplied;

    public TerminalSetupPage()
    {
        InitializeComponent();
#if !DEBUG
        GenerateDatabaseCredentialsButton.IsVisible = false;
#endif
        TerminalConfigurationService.TryApplyInstallerDatabaseConfig();
        LoadExistingConfiguration();
        UpdateModeUi();
    }

    private void OnMinimizeClicked(object sender, EventArgs e)
    {
        PosWindowService.MinimizeMainWindow();
    }

    private void LoadExistingConfiguration()
    {
        var config = TerminalConfigurationService.GetConfiguration();
        _selectedMode = config.Mode;
        TerminalNameEntry.Text = config.TerminalName;
        MotherIpEntry.Text = config.IsChild ? config.DatabaseHost : string.Empty;
        PairingCodeEntry.Text = string.Empty;

        DatabaseNameEntry.Text = config.DatabaseName;
        DatabasePortEntry.Text = config.DatabasePort <= 0 ? "3306" : config.DatabasePort.ToString();
        DatabaseUserEntry.Text = config.DatabaseUser;
        DatabasePasswordEntry.Text = config.DatabasePassword;

        _installerConfigApplied = !string.IsNullOrWhiteSpace(config.DatabasePassword);
        InstallerConfigLabel.IsVisible = _installerConfigApplied;
        InstallerConfigLabel.Text = _installerConfigApplied
            ? "Database credentials loaded from orderweb-database.json. Edit them if this computer uses different MariaDB credentials."
            : string.Empty;
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

        MotherDatabaseSection.IsVisible = true;
        MotherIpSection.IsVisible = !isMother;
        PairingCodeSection.IsVisible = !isMother;
        StatusLabel.Text = isMother
            ? "This terminal will use its own local database."
            : "Enter the mother terminal IP, database connection, and pairing code.";
    }

    private async void OnContinueClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TerminalNameEntry.Text))
        {
            ShowError("Please enter a terminal name.");
            return;
        }

        if (_selectedMode == TerminalMode.Child && string.IsNullOrWhiteSpace(MotherIpEntry.Text))
        {
            ShowError("Please enter the mother terminal IP address.");
            return;
        }

        if (_selectedMode == TerminalMode.Child && string.IsNullOrWhiteSpace(PairingCodeEntry.Text))
        {
            ShowError("Please enter the pairing code from the mother terminal.");
            return;
        }

        var databaseName = string.IsNullOrWhiteSpace(DatabaseNameEntry.Text)
            ? PosDatabaseDefaults.ProductionDatabaseName
            : DatabaseNameEntry.Text.Trim();
        var databasePortText = string.IsNullOrWhiteSpace(DatabasePortEntry.Text)
            ? "3306"
            : DatabasePortEntry.Text.Trim();
        if (!int.TryParse(databasePortText, out var databasePort) || databasePort <= 0 || databasePort > 65535)
        {
            ShowError("Database port must be between 1 and 65535.");
            return;
        }

        var databaseUser = string.IsNullOrWhiteSpace(DatabaseUserEntry.Text)
            ? PosDatabaseDefaults.ProductionDatabaseUser
            : DatabaseUserEntry.Text.Trim();
        var databasePassword = DatabasePasswordEntry.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(databasePassword))
        {
            databasePassword = TerminalConfigurationService.GetConfiguration().DatabasePassword;
        }

        if (_selectedMode == TerminalMode.Mother)
        {
            if (string.IsNullOrWhiteSpace(databaseName) || string.IsNullOrWhiteSpace(databaseUser))
            {
                ShowError("Database name and user are required.");
                return;
            }

            var credentialValidation = ProductionDatabaseCredentialPolicy.Validate(databaseUser, databasePassword);
            if (!credentialValidation.IsValid)
            {
                ShowError(credentialValidation.Message);
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(databasePassword))
        {
            ShowError("Database password is required. Run the installer or enter credentials from orderweb-database.json.");
            return;
        }

        ContinueButton.IsEnabled = false;
        ContinueButton.Text = "Testing...";
        StatusLabel.TextColor = Color.FromArgb("#64748B");
        StatusLabel.Text = "Testing database connection...";

        var existing = TerminalConfigurationService.GetConfiguration();
        var config = new TerminalConfiguration
        {
            IsConfigured = true,
            Mode = _selectedMode,
            TerminalName = TerminalNameEntry.Text.Trim(),
            DatabaseHost = _selectedMode == TerminalMode.Mother ? "localhost" : MotherIpEntry.Text.Trim(),
            DatabasePort = databasePort,
            DatabaseName = databaseName,
            DatabaseUser = databaseUser,
            DatabasePassword = databasePassword
        };

        try
        {
            TerminalConfigurationService.Save(config);
        }
        catch (InvalidOperationException ex)
        {
            ResetContinueButton();
            ShowError(ex.Message);
            return;
        }

        var schemaResult = await new DatabaseService().EnsureProductionSchemaAsync();
        if (!schemaResult.Success)
        {
            TerminalConfigurationService.SetConfigured(false);
            ResetContinueButton();
            ShowError(schemaResult.Message);
            return;
        }

        var testResult = await TerminalConnectionTestService.TestAsync();
        if (!testResult.Success)
        {
            TerminalConfigurationService.SetConfigured(false);
            ResetContinueButton();
            ShowError(testResult.Message);
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
                ResetContinueButton();
                ShowError(pairingResult.Message);
                return;
            }
        }

        await EnsureStartupDataAsync();

        StatusLabel.TextColor = Color.FromArgb("#059669");
        StatusLabel.Text = testResult.Message;
        TerminalPowerSafetyService.Apply();
        ServiceHelper.GetService<TerminalHealthService>()?.Start();
        DatabaseChangeMonitorService.Start();

        var nextRoute = await StartupNavigationService.GetPostSetupRouteAsync();
        await Shell.Current.GoToAsync(nextRoute, false);
    }

    private void OnGenerateDatabaseCredentialsClicked(object sender, EventArgs e)
    {
        try
        {
            var databaseName = string.IsNullOrWhiteSpace(DatabaseNameEntry.Text)
                ? PosDatabaseDefaults.ProductionDatabaseName
                : DatabaseNameEntry.Text.Trim();
            var databaseUser = string.IsNullOrWhiteSpace(DatabaseUserEntry.Text)
                ? PosDatabaseDefaults.ProductionDatabaseUser
                : DatabaseUserEntry.Text.Trim();

            var result = ProductionDatabaseCredentialService.GenerateAndSave(
                databaseName: databaseName,
                databaseUser: databaseUser);

            DatabaseNameEntry.Text = result.Config.DatabaseName;
            DatabaseUserEntry.Text = result.Config.DatabaseUser;
            DatabasePasswordEntry.Text = result.Config.DatabasePassword;

            _installerConfigApplied = true;
            InstallerConfigLabel.IsVisible = true;
            InstallerConfigLabel.Text = "Production credentials generated and saved.";
            StatusLabel.TextColor = Color.FromArgb("#059669");
            StatusLabel.Text = $"Generated database config and SQL setup script: {result.SqlSetupScriptPath}";
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogFatal("GenerateDatabaseCredentials", ex);
            ShowError("Could not generate database credentials.");
        }
    }

    private void OnToggleDatabasePasswordClicked(object sender, EventArgs e)
    {
        DatabasePasswordEntry.IsPassword = !DatabasePasswordEntry.IsPassword;
        ToggleDatabasePasswordButton.Text = DatabasePasswordEntry.IsPassword ? "⌾ Show" : "⌾ Hide";
    }

    private void ResetContinueButton()
    {
        ContinueButton.IsEnabled = true;
        ContinueButton.Text = "Save & Continue";
    }

    private void ShowError(string message)
    {
        StatusLabel.TextColor = Color.FromArgb("#DC2626");
        StatusLabel.Text = message;
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

            await authService.EnsureAuthenticationSchemaAsync();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogFatal("TerminalSetupStartupData", ex);
        }
    }
}
