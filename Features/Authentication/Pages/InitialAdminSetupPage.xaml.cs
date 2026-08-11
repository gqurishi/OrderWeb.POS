using POS_in_NET.Services;

namespace POS_in_NET.Pages;

public partial class InitialAdminSetupPage : ContentPage
{
    private readonly AuthenticationService _authService = AuthenticationService.Instance;

    public InitialAdminSetupPage()
    {
        InitializeComponent();
    }

    private void OnMinimizeClicked(object sender, EventArgs e)
    {
        PosWindowService.MinimizeMainWindow();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (!TerminalConfigurationService.IsConfigured || !TerminalConfigurationService.IsMotherTerminal)
        {
            await NavigationCoordinator.Shared.NavigateShellAsync("login", animated: false);
            return;
        }

        if (await _authService.HasAnyUserAsync())
        {
            await NavigationCoordinator.Shared.NavigateShellAsync("login", animated: false);
        }
    }

    private async void OnCreateClicked(object sender, EventArgs e)
    {
        var name = AdminNameEntry.Text?.Trim() ?? string.Empty;
        var pin = PinEntry.Text?.Trim() ?? string.Empty;
        var confirmPin = ConfirmPinEntry.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            ShowError("Please enter an admin name.");
            return;
        }

        if (pin.Length != 4 || !pin.All(char.IsDigit))
        {
            ShowError("PIN must be exactly 4 digits.");
            return;
        }

        if (!string.Equals(pin, confirmPin, StringComparison.Ordinal))
        {
            ShowError("PIN and confirmation do not match.");
            return;
        }

        CreateButton.IsEnabled = false;
        CreateButton.Text = "Creating...";
        StatusLabel.TextColor = Color.FromArgb("#64748B");
        StatusLabel.Text = "Creating administrator account...";

        var result = await _authService.CreateInitialAdminUserAsync(name, pin);
        if (!result.Success)
        {
            CreateButton.IsEnabled = true;
            CreateButton.Text = "Create Admin & Continue";

            if (result.Message.Contains("Access denied for user", StringComparison.OrdinalIgnoreCase))
            {
                TerminalConfigurationService.SetConfigured(false);
                await NavigationCoordinator.Shared.NavigateShellAsync("terminalsetup", animated: false, source: sender as VisualElement);
                return;
            }

            ShowError(result.Message);
            return;
        }

        await _authService.WarmAuthenticationCacheAsync();
        StatusLabel.TextColor = Color.FromArgb("#059669");
        StatusLabel.Text = "Admin account created. Redirecting to login...";
        await NavigationCoordinator.Shared.NavigateShellAsync("login", animated: false, source: sender as VisualElement);
    }

    private void ShowError(string message)
    {
        StatusLabel.TextColor = Color.FromArgb("#DC2626");
        StatusLabel.Text = message;
    }
}
