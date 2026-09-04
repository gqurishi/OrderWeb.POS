using POS_in_NET.Models;
using POS_in_NET.Services;

namespace POS_in_NET.Pages;

public partial class TerminalSetupPage : ContentPage
{
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
        TerminalNameEntry.Text = config.IsChild ? "Main" : config.TerminalName;

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

    private void UpdateModeUi()
    {
        MotherCard.BackgroundColor = Color.FromArgb("#ECFDF5");
        MotherCard.Stroke = Color.FromArgb("#10B981");
        MotherCard.StrokeThickness = 3;
        MotherDatabaseSection.IsVisible = true;
        StatusLabel.Text = "This terminal will use its own local database and host the Mother API.";
    }

    private async void OnContinueClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TerminalNameEntry.Text))
        {
            ShowError("Please enter a terminal name.");
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

        if (string.IsNullOrWhiteSpace(databasePassword))
        {
            ShowError("Database password is required. Run the installer or enter credentials from orderweb-database.json.");
            return;
        }

        ContinueButton.IsEnabled = false;
        ContinueButton.Text = "Testing...";
        StatusLabel.TextColor = Color.FromArgb("#64748B");
        StatusLabel.Text = "Testing database connection...";

        var config = new TerminalConfiguration
        {
            IsConfigured = true,
            Mode = TerminalMode.Mother,
            TerminalName = TerminalNameEntry.Text.Trim(),
            DatabaseHost = "localhost",
            DatabasePort = databasePort,
            DatabaseName = databaseName,
            DatabaseUser = databaseUser,
            DatabasePassword = databasePassword,
            MotherApiPort = ClientWebSocketBroadcastService.DefaultPort
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

        await EnsureStartupDataAsync();

        StatusLabel.TextColor = Color.FromArgb("#059669");
        StatusLabel.Text = testResult.Message;
        TerminalPowerSafetyService.Apply();
        var services = ServiceHelper.Services;
        if (services != null)
        {
            var motherConnectionStartup = ServiceHelper.GetService<MotherConnectionStartupService>();
            if (motherConnectionStartup != null)
            {
                await motherConnectionStartup.StartAsync();
            }

            await BackgroundSyncJobRegistrar.RegisterDefaultJobsAsync(services);
            ServiceHelper.GetService<BackgroundSyncManager>()?.Start();
        }

        var nextRoute = await StartupNavigationService.GetPostSetupRouteAsync();
        await NavigationCoordinator.Shared.NavigateShellAsync(nextRoute, animated: false, source: sender as VisualElement);
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

    private sealed record ClientBootstrapSetupResponse(
        bool Success,
        string? Message,
        string TerminalId,
        string TerminalToken,
        string TerminalName,
        BootstrapRestaurantSetupResponse? Restaurant);

    private sealed record BootstrapRestaurantSetupResponse(string Name, string? Slug);
}
