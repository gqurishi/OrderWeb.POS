using POS_in_NET.Services;
using POS_in_NET.Models;

namespace POS_in_NET.Pages;

public partial class PostcodeLookupPage : ContentPage
{
    private readonly PostcodeLookupService _postcodeLookupService;

    public PostcodeLookupPage(PostcodeLookupService postcodeLookupService)
    {
        InitializeComponent();
        TopBar.SetPageTitle("Address Lookup");
        _postcodeLookupService = postcodeLookupService;
        UpdateTabHighlight("postcode");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadSettingsAsync();
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            var settings = await _postcodeLookupService.GetSettingsAsync();
            OrderWebAddressApiKeyEntry.Text = settings.OrderWebAddressApiKey;
            OrderWebBaseUrlEntry.Text = settings.OrderWebBaseUrl;

            if (settings.TotalLookups > 0)
            {
                OrderWebStatsFrame.IsVisible = true;
                OrderWebUsageLabel.Text = $"Total lookups: {settings.TotalLookups:N0}";
                OrderWebLastUsedLabel.Text = settings.LastUsed.HasValue
                    ? $"Last used: {settings.LastUsed.Value:g}"
                    : "Last used: Never";
            }
        }
        catch (Exception ex)
        {
            await AppAlertService.ShowAlertAsync("Setup Required", $"Could not load address lookup settings: {ex.Message}");
            await Navigation.PopAsync();
        }
    }

    private void OnToggleOrderWebApiKey(object sender, EventArgs e)
    {
        OrderWebAddressApiKeyEntry.IsPassword = !OrderWebAddressApiKeyEntry.IsPassword;
        ToggleOrderWebApiKeyButton.Text = OrderWebAddressApiKeyEntry.IsPassword ? "Show" : "Hide";
    }

    private async void OnTestOrderWebClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(OrderWebAddressApiKeyEntry.Text))
        {
            await AppAlertService.ShowAlertAsync("Required", "Enter the OrderWeb address API key (owp_...).");
            return;
        }

        try
        {
            TestOrderWebButton.IsEnabled = false;
            TestOrderWebButton.Text = "Testing...";

            await _postcodeLookupService.TestConnectionAsync(
                OrderWebAddressApiKeyEntry.Text?.Trim(),
                OrderWebBaseUrlEntry.Text?.Trim());

            ShowStatus(true, "Connected successfully. Address lookup is ready.");
            await AppAlertService.ShowAlertAsync("Success", "OrderWeb address lookup is working.");
        }
        catch (Exception ex)
        {
            ShowStatus(false, ex.Message);
            await AppAlertService.ShowAlertAsync("Test Failed", ex.Message);
        }
        finally
        {
            TestOrderWebButton.IsEnabled = true;
            TestOrderWebButton.Text = "Test";
        }
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(OrderWebAddressApiKeyEntry.Text))
        {
            await AppAlertService.ShowAlertAsync("Required", "Enter the OrderWeb address API key (owp_...).");
            return;
        }

        try
        {
            SaveButton.IsEnabled = false;
            SaveButton.Text = "Saving...";

            var settings = new PostcodeLookupSettings
            {
                OrderWebAddressApiKey = OrderWebAddressApiKeyEntry.Text.Trim(),
                OrderWebBaseUrl = string.IsNullOrWhiteSpace(OrderWebBaseUrlEntry.Text)
                    ? OrderWebAddressLookupService.DefaultBaseUrl
                    : OrderWebBaseUrlEntry.Text.Trim(),
                OrderWebAddressEnabled = true
            };

            if (await _postcodeLookupService.SaveSettingsAsync(settings))
            {
                await AppAlertService.ShowAlertAsync("Saved", "OrderWeb address lookup settings saved.");
                await Navigation.PopAsync();
            }
            else
            {
                await AppAlertService.ShowAlertAsync("Error", "Could not save settings.");
            }
        }
        catch (Exception ex)
        {
            await AppAlertService.ShowAlertAsync("Error", ex.Message);
        }
        finally
        {
            SaveButton.IsEnabled = true;
            SaveButton.Text = "Save Settings";
        }
    }

    private void ShowStatus(bool success, string message)
    {
        OrderWebStatusFrame.IsVisible = true;
        OrderWebStatusFrame.BackgroundColor = success ? Color.FromArgb("#D1FAE5") : Color.FromArgb("#FEE2E2");
        OrderWebStatusFrame.BorderColor = success ? Color.FromArgb("#10B981") : Color.FromArgb("#EF4444");
        OrderWebStatusText.Text = message;
        OrderWebStatusText.TextColor = success ? Color.FromArgb("#065F46") : Color.FromArgb("#991B1B");
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    private async void OnBusinessTabClicked(object? sender, EventArgs e)
    {
        try { await Shell.Current.GoToAsync("//settings"); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Navigation error: {ex.Message}"); }
    }

    private async void OnUserTabClicked(object? sender, EventArgs e)
    {
        try { await Shell.Current.GoToAsync("//usermanagement"); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Navigation error: {ex.Message}"); }
    }

    private async void OnCloudTabClicked(object? sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    private void OnPostcodeTabClicked(object? sender, EventArgs e)
    {
        UpdateTabHighlight("postcode");
    }

    private void UpdateTabHighlight(string activeTab)
    {
        // Visual tab styling when opened from legacy settings flows.
    }
}
