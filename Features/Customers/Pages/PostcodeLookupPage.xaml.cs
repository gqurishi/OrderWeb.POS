using POS_in_NET.Services;
using POS_in_NET.Models;

namespace POS_in_NET.Pages;

public partial class PostcodeLookupPage : ContentPage
{
    private readonly PostcodeLookupService _postcodeLookupService;
    private PostcodeLookupSettings _currentSettings;

    public PostcodeLookupPage(PostcodeLookupService postcodeLookupService)
    {
        InitializeComponent();
        TopBar.SetPageTitle("Settings");
        _postcodeLookupService = postcodeLookupService;
        _currentSettings = new PostcodeLookupSettings();
        
        // Set active tab
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
            _currentSettings = await _postcodeLookupService.GetSettingsAsync();

            // Always use Mapbox provider
            _currentSettings.Provider = "Mapbox";

            // Load Mapbox settings
            MapboxTokenEntry.Text = _currentSettings.MapboxApiToken;

            // Load Custom settings
            CustomUrlEntry.Text = _currentSettings.CustomApiUrl;
            CustomTokenEntry.Text = _currentSettings.CustomAuthToken;

            // Show usage statistics if available
            if (_currentSettings.TotalLookups > 0)
            {
                MapboxStatsFrame.IsVisible = true;
                MapboxUsageLabel.Text = $"Total Lookups: {_currentSettings.TotalLookups:N0}";
                
                if (_currentSettings.LastUsed.HasValue)
                {
                    MapboxLastUsedLabel.Text = $"Last Used: {_currentSettings.LastUsed.Value:g}";
                }
            }

            UpdateProviderUI();
        }
        catch (Exception)
        {
            // If table doesn't exist, show a friendly message
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Setup Required", "Please run the database migration script first.\n\nSee POSTCODE_LOOKUP_GUIDE.md for instructions.");
            await Navigation.PopAsync();
        }
    }

    private void UpdateProviderUI()
    {
        // Always show Mapbox, hide Custom
        MapboxFrame.IsVisible = true;
        CustomFrame.IsVisible = false;
    }

    private void OnProviderChanged(object sender, EventArgs e)
    {
        // No longer needed but kept for compatibility
    }

    private void UpdateProviderUILegacy()
    {
        // Legacy method - no longer used
        if (false)
        {
            MapboxFrame.IsVisible = false;
            CustomFrame.IsVisible = true;
        }
    }

    // Tab Navigation
    private async void OnBusinessTabClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//businesssettings");
    }

    private async void OnUserTabClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//usermanagement");
    }

    private async void OnCloudTabClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//cloudsettings");
    }

    private void OnPostcodeTabClicked(object? sender, EventArgs e)
    {
        // Already on this page
        UpdateTabHighlight("postcode");
    }

    private void UpdateTabHighlight(string activeTab)
    {
        // Reset all tabs to default styling
        ResetTabToDefault(BusinessTabBorder);
        ResetTabToDefault(UserTabBorder);
        ResetTabToDefault(CloudTabBorder);
        ResetTabToDefault(PostcodeTabBorder);

        // Set all label colors to default
        SetTabLabelColor(BusinessTabBorder, "#6B7280");
        SetTabLabelColor(UserTabBorder, "#6B7280");
        SetTabLabelColor(CloudTabBorder, "#6B7280");
        SetTabLabelColor(PostcodeTabBorder, "#6B7280");

        // Highlight active tab with elegant styling
        var primaryColor = Application.Current?.Resources["Primary"] as Color ?? Color.FromArgb("#6366F1");
        switch (activeTab.ToLower())
        {
            case "business":
                HighlightActiveTab(BusinessTabBorder, "#E0F2FE", "#0EA5E9");
                break;
            case "user":
                HighlightActiveTab(UserTabBorder, "#DCFCE7", "#10B981");
                break;
            case "cloud":
                HighlightActiveTab(CloudTabBorder, "#F3E8FF", "#8B5CF6");
                break;
            case "postcode":
                HighlightActiveTab(PostcodeTabBorder, "#FEF2F2", "#EF4444");
                break;
        }
    }
    
    private void ResetTabToDefault(Border tabBorder)
    {
        tabBorder.BackgroundColor = Colors.White;
        tabBorder.Stroke = Color.FromArgb("#E5E7EB");
        tabBorder.StrokeThickness = 1;
    }
    
    private void HighlightActiveTab(Border tabBorder, string backgroundColor, string borderColor)
    {
        tabBorder.BackgroundColor = Color.FromArgb(backgroundColor);
        tabBorder.Stroke = Color.FromArgb(borderColor);
        tabBorder.StrokeThickness = 2;
        SetTabLabelColor(tabBorder, borderColor);
    }
    
    private void SetTabLabelColor(Border tabBorder, string color)
    {
        if (tabBorder.Content is Label label)
        {
            label.TextColor = Color.FromArgb(color);
        }
    }

    private void OnToggleMapboxToken(object sender, EventArgs e)
    {
        MapboxTokenEntry.IsPassword = !MapboxTokenEntry.IsPassword;
        ToggleMapboxTokenButton.Text = MapboxTokenEntry.IsPassword ? "Show" : "Hide";
    }

    private async void OnTestMapboxClicked(object sender, EventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("===============================================");
        System.Diagnostics.Debug.WriteLine("[TEST CONNECTION] Button clicked!");
        System.Diagnostics.Debug.WriteLine("===============================================");
        
        if (string.IsNullOrWhiteSpace(MapboxTokenEntry.Text))
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Please enter your Mapbox API token");
            return;
        }

        try
        {
            TestMapboxButton.IsEnabled = false;
            TestMapboxButton.Text = "Testing...";

            var token = MapboxTokenEntry.Text?.Trim();
            System.Diagnostics.Debug.WriteLine($"[PostcodeLookup] Token entered: {token?.Substring(0, Math.Min(30, token.Length))}...");
            System.Diagnostics.Debug.WriteLine($"[PostcodeLookup] Token length: {token?.Length}");

            // Create temporary settings for testing
            var testSettings = new PostcodeLookupSettings
            {
                Provider = "Mapbox",
                MapboxApiToken = token,
                MapboxEnabled = true
            };

            System.Diagnostics.Debug.WriteLine("[PostcodeLookup] Saving test settings...");
            await _postcodeLookupService.SaveSettingsAsync(testSettings);
            System.Diagnostics.Debug.WriteLine("[PostcodeLookup] Settings saved successfully");
            
            System.Diagnostics.Debug.WriteLine("[PostcodeLookup] Calling TestConnectionAsync()...");
            await _postcodeLookupService.TestConnectionAsync();
            System.Diagnostics.Debug.WriteLine("[PostcodeLookup] TestConnectionAsync() SUCCESS - no exception thrown");

            // Show result
            MapboxStatusFrame.IsVisible = true;
            MapboxStatusFrame.BackgroundColor = Color.FromArgb("#D5F4E6");
            MapboxStatusFrame.BorderColor = Color.FromArgb("#27AE60");
            MapboxStatusIcon.Text = "✓";
            MapboxStatusText.Text = "Connected successfully!";
            MapboxStatusText.TextColor = Color.FromArgb("#27AE60");
            
            System.Diagnostics.Debug.WriteLine("[PostcodeLookup] ✅ Connection test PASSED!");
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", "Mapbox connection successful!\n\nYou can now use postcode lookup in your delivery orders.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("===============================================");
            System.Diagnostics.Debug.WriteLine("[PostcodeLookup] ❌ Connection test FAILED");
            System.Diagnostics.Debug.WriteLine($"[PostcodeLookup] Exception type: {ex.GetType().Name}");
            System.Diagnostics.Debug.WriteLine($"[PostcodeLookup] Error message: {ex.Message}");
            if (ex.InnerException != null)
            {
                System.Diagnostics.Debug.WriteLine($"[PostcodeLookup] Inner exception: {ex.InnerException.Message}");
            }
            System.Diagnostics.Debug.WriteLine($"[PostcodeLookup] Stack trace: {ex.StackTrace}");
            System.Diagnostics.Debug.WriteLine("===============================================");
            
            MapboxStatusFrame.IsVisible = true;
            MapboxStatusFrame.BackgroundColor = Color.FromArgb("#FADBD8");
            MapboxStatusFrame.BorderColor = Color.FromArgb("#E74C3C");
            MapboxStatusIcon.Text = "✗";
            MapboxStatusText.Text = $"Error: {ex.Message}";
            MapboxStatusText.TextColor = Color.FromArgb("#E74C3C");
            
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Test failed: {ex.Message}");
        }
        finally
        {
            TestMapboxButton.IsEnabled = true;
            TestMapboxButton.Text = "Test Connection";
        }
    }

    private async void OnTestCustomClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CustomUrlEntry.Text))
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Please enter your API endpoint URL");
            return;
        }

        try
        {
            TestCustomButton.IsEnabled = false;
            TestCustomButton.Text = "Testing...";

            // Create temporary settings for testing
            var testSettings = new PostcodeLookupSettings
            {
                Provider = "Custom",
                CustomApiUrl = CustomUrlEntry.Text,
                CustomAuthToken = CustomTokenEntry.Text,
                CustomEnabled = true
            };

            await _postcodeLookupService.SaveSettingsAsync(testSettings);
            bool isConnected = await _postcodeLookupService.TestConnectionAsync();

            // Show result
            CustomStatusFrame.IsVisible = true;
            if (isConnected)
            {
                CustomStatusFrame.BackgroundColor = Color.FromArgb("#D5F4E6");
                CustomStatusFrame.BorderColor = Color.FromArgb("#27AE60");
                CustomStatusIcon.Text = "✓";
                CustomStatusText.Text = "Connected successfully!";
                CustomStatusText.TextColor = Color.FromArgb("#27AE60");
                
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", "Custom API connection successful!");
            }
            else
            {
                CustomStatusFrame.BackgroundColor = Color.FromArgb("#FADBD8");
                CustomStatusFrame.BorderColor = Color.FromArgb("#E74C3C");
                CustomStatusIcon.Text = "✗";
                CustomStatusText.Text = "Connection failed";
                CustomStatusText.TextColor = Color.FromArgb("#E74C3C");
                
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Failed to connect to custom API.\n\nPlease check your endpoint URL and authentication token.");
            }
        }
        catch (Exception ex)
        {
            CustomStatusFrame.IsVisible = true;
            CustomStatusFrame.BackgroundColor = Color.FromArgb("#FADBD8");
            CustomStatusFrame.BorderColor = Color.FromArgb("#E74C3C");
            CustomStatusIcon.Text = "✗";
            CustomStatusText.Text = $"Error: {ex.Message}";
            CustomStatusText.TextColor = Color.FromArgb("#E74C3C");
            
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Test failed: {ex.Message}");
        }
        finally
        {
            TestCustomButton.IsEnabled = true;
            TestCustomButton.Text = "Test Connection";
        }
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        // Always use Mapbox provider
        var selectedProvider = "Mapbox";

        // Validate Mapbox token
        if (string.IsNullOrWhiteSpace(MapboxTokenEntry.Text))
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Please enter your Mapbox API token");
            return;
        }

        if (selectedProvider == "Custom" && string.IsNullOrWhiteSpace(CustomUrlEntry.Text))
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Please enter your custom API URL");
            return;
        }

        try
        {
            SaveButton.IsEnabled = false;
            SaveButton.Text = "Saving...";

            var settings = new PostcodeLookupSettings
            {
                Provider = selectedProvider ?? "Mapbox",
                MapboxApiToken = MapboxTokenEntry.Text ?? "",
                MapboxEnabled = selectedProvider == "Mapbox",
                CustomApiUrl = CustomUrlEntry.Text ?? "",
                CustomAuthToken = CustomTokenEntry.Text ?? "",
                CustomEnabled = selectedProvider == "Custom"
            };

            bool success = await _postcodeLookupService.SaveSettingsAsync(settings);

            if (success)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Success", "Settings saved successfully!");
                await Navigation.PopAsync();
            }
            else
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Failed to save settings");
            }
        }
        catch (Exception ex)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to save: {ex.Message}");
        }
        finally
        {
            SaveButton.IsEnabled = true;
            SaveButton.Text = "Save Settings";
        }
    }

    private async void OnCancelClicked(object sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }
}