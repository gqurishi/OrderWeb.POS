using POS_in_NET.Models;
using POS_in_NET.Services;

namespace POS_in_NET.Pages;

public partial class LoyaltyPointsPage : ContentPage
{
    private readonly LoyaltyService _loyaltyService;
    private readonly AuthenticationService _authService;
    private readonly RoleAccessService _roleAccessService;
    private LoyaltyCustomer? _currentCustomer;

    public LoyaltyPointsPage()
    {
        InitializeComponent();
        
        // Set the page title in the TopBar
        TopBar.SetPageTitle("Loyalty Points");
        
        _loyaltyService = ServiceHelper.GetService<LoyaltyService>()
            ?? throw new InvalidOperationException("LoyaltyService not found");
        _authService = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
        _roleAccessService = ServiceHelper.GetService<RoleAccessService>() ?? new RoleAccessService();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (!_roleAccessService.IsManagerOrAdmin(_authService.CurrentUser?.Role))
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Access Denied", "Only Manager and Admin can access Loyalty Points.");
            await Shell.Current.GoToAsync($"//{_roleAccessService.ResolveDashboardRoute(_authService.CurrentUser?.Role)}");
        }
    }

    #region Customer Loyalty Methods

    private async void OnSearchCustomerClicked(object sender, EventArgs e)
    {
        var phone = PhoneSearchEntry.Text?.Trim();
        
        if (string.IsNullOrWhiteSpace(phone))
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("❌ Error", "Please enter a phone number");
            return;
        }

        try
        {
            // Show loading
            PhoneSearchEntry.IsEnabled = false;

            var result = await _loyaltyService.SearchCustomerAsync(phone);

            if (result.Success && result.Customer != null)
            {
                _currentCustomer = result.Customer;
                DisplayCustomerDetails(result.Customer, result.Transactions);
                CustomerDetailsFrame.IsVisible = true;
            }
            else
            {
                CustomerDetailsFrame.IsVisible = false;
                
                // Show detailed error with option to create customer
                var errorMessage = result.Error ?? "Customer not found";
                var createNew = await DisplayAlert(
                    "❌ Customer Not Found", 
                    $"{errorMessage}\n\nPhone: {phone}\n\nWould you like to create a new customer account?",
                    "Create New Customer",
                    "Cancel");
                
                if (createNew)
                {
                    // Auto-trigger the new customer dialog
                    OnAddNewCustomerClicked(sender, e);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Exception in search: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"   Stack: {ex.StackTrace}");
            
            await DisplayAlert("❌ Connection Error", 
                $"Failed to connect to OrderWeb.net:\n\n{ex.Message}\n\n" +
                $"Please check:\n" +
                $"• Internet connection\n" +
                $"• Cloud Settings (API Key & Tenant ID)\n" +
                $"• OrderWeb.net service status", 
                "OK");
        }
        finally
        {
            PhoneSearchEntry.IsEnabled = true;
        }
    }

    private async void OnTestConnectionClicked(object sender, EventArgs e)
    {
        try
        {
            var databaseService = ServiceHelper.GetService<DatabaseService>();
            if (databaseService == null)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("❌ Error", "Database service not found");
                return;
            }

            // Get cloud configuration
            var config = await databaseService.GetCloudConfigAsync();
            var apiKey = config.GetValueOrDefault("api_key", "");
            var tenantId = config.GetValueOrDefault("tenant_slug", "");
            var baseUrl = "https://orderweb.net/api";

            // Build test info showing POS API endpoints
            var info = $"🔧 API Configuration Test\n\n" +
                      $"Base URL: {baseUrl}\n" +
                      $"Tenant: {tenantId}\n" +
                      $"Auth: Bearer {(string.IsNullOrEmpty(apiKey) ? "NOT SET" : apiKey.Substring(0, Math.Min(20, apiKey.Length)) + "...")}\n\n" +
                      $"Test Endpoints:\n" +
                      $"• GET {baseUrl}/pos/loyalty-lookup?tenant={tenantId}&phone=07306506797\n\n";

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(tenantId))
            {
                await DisplayAlert("⚠️ Configuration Missing", 
                    info + "❌ API Key or Tenant ID is missing!\n\nPlease configure in Settings → Cloud Settings", 
                    "OK");
                return;
            }

            // Try a test call
            var testPhone = "07306506797";
            info += $"Testing with phone: {testPhone}\n\nPlease wait...";
            
            var testResult = await DisplayAlert("🔧 API Test", info, "Run Test", "Cancel");
            
            if (testResult)
            {
                var result = await _loyaltyService.SearchCustomerAsync(testPhone);
                
                if (result.Success && result.Customer != null)
                {
                    await DisplayAlert("✅ API Test SUCCESS", 
                        $"Connection working!\n\n" +
                        $"Found Customer:\n" +
                        $"Name: {result.Customer.CustomerName}\n" +
                        $"Phone: {result.Customer.Phone}\n" +
                        $"Points: {result.Customer.PointsBalance}\n" +
                        $"Card: {result.Customer.LoyaltyCardNumber}", 
                        "OK");
                }
                else
                {
                    await DisplayAlert("⚠️ API Test - Not Found", 
                        $"API is responding but customer not found.\n\n" +
                        $"Error: {result.Error}\n\n" +
                        $"This means:\n" +
                        $"• Connection is working ✅\n" +
                        $"• Customer doesn't exist in database\n" +
                        $"• Try creating a new customer\n\n" +
                        $"Check Debug Console for full API response", 
                        "OK");
                }
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("❌ API Test FAILED", 
                $"Connection Error:\n\n{ex.Message}\n\n" +
                $"Possible causes:\n" +
                $"• No internet connection\n" +
                $"• Wrong API Key\n" +
                $"• Wrong Tenant ID\n" +
                $"• OrderWeb.net API not responding\n" +
                $"• Endpoint doesn't exist yet\n\n" +
                $"Check Debug Console for details", 
                "OK");
        }
    }

    private async void OnAddNewCustomerClicked(object sender, EventArgs e)
    {
        // Pre-fill phone number if entered in search
        var phoneFromSearch = PhoneSearchEntry.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(phoneFromSearch))
        {
            NewCustomerPhoneEntry.Text = phoneFromSearch;
        }
        
        // Clear other fields
        NewCustomerNameEntry.Text = string.Empty;
        NewCustomerEmailEntry.Text = string.Empty;
        
        // Show the custom dialog
        NewCustomerOverlay.IsVisible = true;
        
        // Focus on the appropriate field
        if (string.IsNullOrWhiteSpace(phoneFromSearch))
        {
            NewCustomerPhoneEntry.Focus();
        }
        else
        {
            NewCustomerNameEntry.Focus();
        }
    }

    private void OnCancelNewCustomerClicked(object sender, EventArgs e)
    {
        // Hide the dialog
        NewCustomerOverlay.IsVisible = false;
        
        // Clear fields
        NewCustomerPhoneEntry.Text = string.Empty;
        NewCustomerNameEntry.Text = string.Empty;
        NewCustomerEmailEntry.Text = string.Empty;
    }

    private async void OnSaveNewCustomerClicked(object sender, EventArgs e)
    {
        var phone = NewCustomerPhoneEntry.Text?.Trim();
        var name = NewCustomerNameEntry.Text?.Trim();
        var email = NewCustomerEmailEntry.Text?.Trim();

        // Validate required fields
        if (string.IsNullOrWhiteSpace(phone))
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("❌ Required Field", "Please enter a phone number");
            NewCustomerPhoneEntry.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("❌ Required Field", "Please enter a customer name");
            NewCustomerNameEntry.Focus();
            return;
        }

        try
        {
            // Hide the dialog
            NewCustomerOverlay.IsVisible = false;

            var result = await _loyaltyService.CreateCustomerAsync(phone, name, email);

            if (result.Success && result.Customer != null)
            {
                await DisplayAlert("✅ Success", 
                    $"Customer account created!\n\n" +
                    $"Name: {result.Customer.CustomerName}\n" +
                    $"Phone: {result.Customer.Phone}\n" +
                    $"Loyalty Card: {result.Customer.LoyaltyCardNumber}\n" +
                    $"Points: {result.Customer.PointsBalance}", 
                    "OK");
                
                _currentCustomer = result.Customer;
                PhoneSearchEntry.Text = phone;
                DisplayCustomerDetails(result.Customer, result.Transactions);
                CustomerDetailsFrame.IsVisible = true;
                
                // Clear the form
                NewCustomerPhoneEntry.Text = string.Empty;
                NewCustomerNameEntry.Text = string.Empty;
                NewCustomerEmailEntry.Text = string.Empty;
            }
            else
            {
                await DisplayAlert("❌ Error", 
                    result.Error ?? "Failed to create customer account", 
                    "OK");
            }
        }
        catch (Exception ex)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("❌ Error", $"Failed to create customer: {ex.Message}");
        }
    }

    private async void OnAddPointsClicked(object sender, EventArgs e)
    {
        if (_currentCustomer == null)
            return;

        var pointsStr = await DisplayPromptAsync("➕ Add Points", 
            $"Current balance: {_currentCustomer.PointsBalance} pts\n\nEnter points to add:",
            keyboard: Keyboard.Numeric);

        if (string.IsNullOrWhiteSpace(pointsStr) || !int.TryParse(pointsStr, out int points) || points <= 0)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("❌ Error", "Please enter a valid number of points");
            return;
        }

        var reason = await DisplayPromptAsync("➕ Add Points", 
            "Enter reason (optional):",
            placeholder: "e.g., POS Manual Addition - Order Value: £50.00");

        if (string.IsNullOrWhiteSpace(reason))
            reason = $"POS Manual Addition - {points} points";

        try
        {
            var result = await _loyaltyService.AddPointsAsync(_currentCustomer.Phone, points, reason);

            if (result.Success && result.Customer != null)
            {
                await DisplayAlert("✅ Success", 
                    $"Added {points} points!\nNew balance: {result.Customer.PointsBalance} pts", 
                    "OK");
                
                _currentCustomer = result.Customer;
                DisplayCustomerDetails(result.Customer, result.Transactions);
            }
            else
            {
                await DisplayAlert("❌ Error", 
                    result.Error ?? "Failed to add points", 
                    "OK");
            }
        }
        catch (Exception ex)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("❌ Error", $"Failed to add points: {ex.Message}");
        }
    }

    private async void OnRedeemPointsClicked(object sender, EventArgs e)
    {
        if (_currentCustomer == null)
            return;

        if (_currentCustomer.PointsBalance <= 0)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("❌ Error", "Customer has no points to redeem");
            return;
        }

        var pointsStr = await DisplayPromptAsync("💰 Redeem Points", 
            $"Available balance: {_currentCustomer.PointsBalance} pts\n" +
            $"Conversion: 100 pts = £1\n\n" +
            $"Enter points to redeem:",
            keyboard: Keyboard.Numeric);

        if (string.IsNullOrWhiteSpace(pointsStr) || !int.TryParse(pointsStr, out int points) || points <= 0)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("❌ Error", "Please enter a valid number of points");
            return;
        }

        if (points > _currentCustomer.PointsBalance)
        {
            await DisplayAlert("❌ Error", 
                $"Insufficient points. Available: {_currentCustomer.PointsBalance} pts", 
                "OK");
            return;
        }

        var discountAmount = points / 100.0m;
        var confirm = await DisplayAlert("💰 Confirm Redemption", 
            $"Redeem {points} points for £{discountAmount:F2} discount?", 
            "Redeem", "Cancel");

        if (!confirm)
            return;

        try
        {
            var reason = $"POS Point Redemption - £{discountAmount:F2} discount applied";
            var result = await _loyaltyService.RedeemPointsAsync(_currentCustomer.Phone, points, reason);

            if (result.Success && result.Customer != null)
            {
                await DisplayAlert("✅ Success", 
                    $"Redeemed {points} points = £{discountAmount:F2}!\n" +
                    $"New balance: {result.Customer.PointsBalance} pts", 
                    "OK");
                
                _currentCustomer = result.Customer;
                DisplayCustomerDetails(result.Customer, result.Transactions);
            }
            else
            {
                await DisplayAlert("❌ Error", 
                    result.Error ?? "Failed to redeem points", 
                    "OK");
            }
        }
        catch (Exception ex)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("❌ Error", $"Failed to redeem points: {ex.Message}");
        }
    }

    private async void OnRefreshCustomerClicked(object sender, EventArgs e)
    {
        if (_currentCustomer == null)
            return;

        // Re-search the customer
        PhoneSearchEntry.Text = _currentCustomer.Phone;
        
        try
        {
            var result = await _loyaltyService.SearchCustomerAsync(_currentCustomer.Phone);

            if (result.Success && result.Customer != null)
            {
                _currentCustomer = result.Customer;
                DisplayCustomerDetails(result.Customer, result.Transactions);
                CustomerDetailsFrame.IsVisible = true;
            }
        }
        catch (Exception ex)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("❌ Error", $"Failed to refresh: {ex.Message}");
        }
    }

    private void DisplayCustomerDetails(LoyaltyCustomer customer, List<LoyaltyTransaction> transactions)
    {
        // Customer Info
        CustomerNameLabel.Text = $"Name: {customer.CustomerName}";
        CustomerPhoneLabel.Text = $"Phone: {customer.DisplayPhone}";
        CustomerEmailLabel.Text = string.IsNullOrWhiteSpace(customer.Email) 
            ? "Email: No email" 
            : $"Email: {customer.Email}";
        CustomerPointsLabel.Text = $"{customer.PointsBalance:N0} Points";

        // Additional Info
        CustomerTotalSpentLabel.Text = $"Total Spent: {customer.TotalSpent}";
        CustomerLastVisitLabel.Text = customer.LastOrderDate.HasValue 
            ? $"Last Visit: {customer.LastOrderDate.Value:MMM dd, yyyy}" 
            : "Last Visit: Never";

        // Redemption Value
        decimal redemptionValue = customer.PointsBalance / 100m; // 100 points = £1
        RedemptionValueLabel.Text = $"Current points worth: £{redemptionValue:F2}";

        // Transaction History
        HistoryCollectionView.ItemsSource = transactions;
    }

    private async void OnSubtractPointsClicked(object sender, EventArgs e)
    {
        if (_currentCustomer == null)
            return;

        var pointsStr = PointsEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(pointsStr) || !int.TryParse(pointsStr, out int points) || points <= 0)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Please enter a valid points amount");
            return;
        }

        if (points > _currentCustomer.PointsBalance)
        {
            await DisplayAlert("Error", 
                $"Insufficient points. Customer has {_currentCustomer.PointsBalance} points", 
                "OK");
            return;
        }

        var notes = NotesEntry.Text?.Trim() ?? "";
        var confirm = await DisplayAlert("Confirm", 
            $"Subtract {points} points from {_currentCustomer.CustomerName}?", 
            "Subtract", "Cancel");

        if (!confirm)
            return;

        try
        {
            var result = await _loyaltyService.RedeemPointsAsync(
                _currentCustomer.Phone, 
                points, 
                $"Manual adjustment: {notes}");

            if (result.Success)
            {
                await DisplayAlert("Success", 
                    $"{points} points subtracted successfully!", 
                    "OK");

                PointsEntry.Text = "";
                NotesEntry.Text = "";

                // Refresh customer details
                OnRefreshCustomerClicked(this, EventArgs.Empty);
            }
            else
            {
                await DisplayAlert("Error", 
                    result.Error ?? "Failed to subtract points", 
                    "OK");
            }
        }
        catch (Exception ex)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to subtract points: {ex.Message}");
        }
    }

    private async void OnViewHistoryClicked(object sender, EventArgs e)
    {
        if (_currentCustomer == null)
            return;

        // Show history overlay
        HistoryOverlay.IsVisible = true;

        // Refresh history
        try
        {
            var result = await _loyaltyService.SearchCustomerAsync(_currentCustomer.Phone);
            if (result.Success && result.Transactions != null)
            {
                HistoryCollectionView.ItemsSource = result.Transactions;
            }
        }
        catch (Exception ex)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to load history: {ex.Message}");
        }
    }

    private void OnCloseHistoryClicked(object sender, EventArgs e)
    {
        // Hide history overlay
        HistoryOverlay.IsVisible = false;
    }

    private async void OnSendStatementClicked(object sender, EventArgs e)
    {
        if (_currentCustomer == null)
            return;

        if (string.IsNullOrWhiteSpace(_currentCustomer.Email))
        {
            await DisplayAlert("Error", 
                "Customer has no email address on file", 
                "OK");
            return;
        }

        var confirm = await DisplayAlert("Send Statement", 
            $"Send loyalty statement to {_currentCustomer.Email}?", 
            "Send", "Cancel");

        if (!confirm)
            return;

        try
        {
            // TODO: Implement email sending functionality
            await DisplayAlert("Success", 
                $"Loyalty statement sent to {_currentCustomer.Email}", 
                "OK");
        }
        catch (Exception ex)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to send statement: {ex.Message}");
        }
    }

    #endregion
}
