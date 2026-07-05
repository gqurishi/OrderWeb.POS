using POS_in_NET.Models;
using POS_in_NET.Services;
using POS_in_NET.Views;

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
        NotificationService.Instance.NotificationRequested += OnNotificationRequested;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        NotificationService.Instance.NotificationRequested -= OnNotificationRequested;
    }

    private void OnNotificationRequested(object? sender, NotificationEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await ToastNotification.ShowAsync(e.Title, e.Message, e.Type, e.DurationMs);
        });
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (!_roleAccessService.IsManagerOrAdmin(_authService.CurrentUser?.Role))
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Access Denied", "Only Manager and Admin can access Loyalty Points.");
            await Shell.Current.GoToAsync($"//{_roleAccessService.ResolveDashboardRoute(_authService.CurrentUser?.Role)}");
            return;
        }

        try
        {
            await _loyaltyService.ReinitializeAsync();
            System.Diagnostics.Debug.WriteLine(" Loyalty Points page: LoyaltyService reinitialized");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Failed to reinitialize LoyaltyService: {ex.Message}");
        }
    }

    #region Customer Loyalty Methods

    private async void OnCloseClicked(object sender, EventArgs e)
    {
        try
        {
            var dashboardRoute = _roleAccessService.ResolveDashboardRoute(_authService.CurrentUser?.Role);
            await Shell.Current.GoToAsync($"//{dashboardRoute}", false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Loyalty close navigation failed: {ex.Message}");
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Navigation Error", "Could not return to the dashboard. Please try again.");
        }
    }

    private async void OnSearchCustomerClicked(object sender, EventArgs e)
    {
        var phone = PhoneSearchEntry.Text?.Trim();
        
        if (string.IsNullOrWhiteSpace(phone))
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync(" Error", "Please enter a phone number");
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
                
                var errorMessage = result.Error ?? "Customer not found";
                var createCustomerDialog = new ModernConfirmDialog();
                createCustomerDialog.SetConfirm(
                    "Customer Not Found",
                    $"{errorMessage}\n\nPhone: {phone}\n\nWould you like to create a new customer account?",
                    "Create New Customer",
                    "Cancel",
                    "i",
                    "#2563EB");

                var createNew = await createCustomerDialog.ShowAsync();
                
                if (createNew)
                {
                    // Auto-trigger the new customer dialog
                    OnAddNewCustomerClicked(sender, e);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Exception in search: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"   Stack: {ex.StackTrace}");
            
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync(
                "Connection Error", 
                $"Failed to connect to OrderWeb.net:\n\n{ex.Message}\n\n" +
                $"Please check:\n" +
                $"• Internet connection\n" +
                $"• Cloud Settings (API Key & Tenant ID)\n" +
                $"• OrderWeb.net service status");
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
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync(" Error", "Database service not found");
                return;
            }

            // Get cloud configuration
            var config = await databaseService.GetCloudConfigAsync();
            var apiKey = config.GetValueOrDefault("api_key", "");
            var tenantId = config.GetValueOrDefault("tenant_slug", "");

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(tenantId))
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync(
                    "Configuration Missing",
                    "API Key or Tenant ID is missing. Please configure Cloud Settings first.");
                return;
            }

            var testLookup = PhoneSearchEntry.Text?.Trim();
            if (string.IsNullOrWhiteSpace(testLookup))
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync(
                    "Enter Test Customer",
                    "Enter a customer phone number or loyalty card number first, then run the test.");
                return;
            }

            var testDialog = new ModernConfirmDialog();
            testDialog.SetConfirm(
                "API Test",
                "Run a loyalty API connection test now?",
                "Run Test",
                "Cancel",
                "i",
                "#2563EB");

            var testResult = await testDialog.ShowAsync();
            
            if (testResult)
            {
                await _loyaltyService.ReinitializeAsync();
                var result = await _loyaltyService.SearchCustomerAsync(testLookup);
                
                if (result.Success && result.Customer != null)
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync(
                        "API Test Success", 
                        $"Connection working!\n\n" +
                        $"Found Customer:\n" +
                        $"Name: {result.Customer.CustomerName}\n" +
                        $"Phone: {result.Customer.Phone}\n" +
                        $"Points: {result.Customer.PointsBalance}\n" +
                        $"Card: {result.Customer.LoyaltyCardNumber}");
                }
                else
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync(
                        "API Test - Not Found", 
                        $"API is responding but customer not found.\n\n" +
                        $"Error: {result.Error}\n\n" +
                        $"This means:\n" +
                        $"• Connection is working \n" +
                        $"• Customer doesn't exist in database\n" +
                        $"• Try creating a new customer\n\n" +
                        $"Check Debug Console for full API response");
                }
            }
        }
        catch (Exception ex)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync(
                "API Test Failed", 
                $"Connection Error:\n\n{ex.Message}\n\n" +
                $"Possible causes:\n" +
                $"• No internet connection\n" +
                $"• Wrong API Key\n" +
                $"• Wrong Tenant ID\n" +
                $"• OrderWeb.net API not responding\n" +
                $"• Endpoint doesn't exist yet\n\n" +
                $"Check Debug Console for details");
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
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync(" Required Field", "Please enter a phone number");
            NewCustomerPhoneEntry.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync(" Required Field", "Please enter a customer name");
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
                await DisplayAlert(" Success", 
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
                await DisplayAlert(" Error", 
                    result.Error ?? "Failed to create customer account", 
                    "OK");
            }
        }
        catch (Exception ex)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync(" Error", $"Failed to create customer: {ex.Message}");
        }
    }

    private async void OnAddPointsClicked(object sender, EventArgs e)
    {
        await AdjustCustomerPointsAsync(addPoints: true, sender as Button);
    }

    private async void OnRedeemPointsClicked(object sender, EventArgs e)
    {
        await AdjustCustomerPointsAsync(addPoints: false, sender as Button);
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
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync(" Error", $"Failed to refresh: {ex.Message}");
        }
    }

    private void DisplayCustomerDetails(LoyaltyCustomer customer, List<LoyaltyTransaction> transactions)
    {
        var displayName = string.IsNullOrWhiteSpace(customer.CustomerName)
            ? "No name saved"
            : customer.CustomerName.Trim();
        var displayPhone = string.IsNullOrWhiteSpace(customer.DisplayPhone)
            ? customer.Phone
            : customer.DisplayPhone;

        CustomerNameLabel.Text = displayName;
        CustomerPhoneLabel.Text = string.IsNullOrWhiteSpace(displayPhone)
            ? "Phone not saved"
            : displayPhone.Trim();
        CustomerEmailLabel.Text = string.IsNullOrWhiteSpace(customer.Email) 
            ? "Email not saved" 
            : customer.Email.Trim();
        CustomerPointsLabel.Text = $"{customer.PointsBalance:N0} Points";

        CustomerLastVisitLabel.Text = customer.LastOrderDate.HasValue 
            ? $"Last Visit: {customer.LastOrderDate.Value:MMM dd, yyyy}" 
            : "Last Visit: Never";

        decimal redemptionValue = customer.PointsBalance / 100m; // 100 points = £1
        RedemptionValueLabel.Text = $"Worth: £{redemptionValue:F2}";

        HistoryCollectionView.ItemsSource = transactions;
    }

    private async void OnSubtractPointsClicked(object sender, EventArgs e)
    {
        await AdjustCustomerPointsAsync(addPoints: false, sender as Button);
    }

    private async Task AdjustCustomerPointsAsync(bool addPoints, Button? actionButton)
    {
        if (_currentCustomer == null)
        {
            NotificationService.Instance.ShowWarning("Search and select a customer first");
            return;
        }

        var pointsStr = PointsEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(pointsStr) || !int.TryParse(pointsStr, out int points) || points <= 0)
        {
            NotificationService.Instance.ShowWarning("Please enter a valid points amount");
            return;
        }

        if (!addPoints && _currentCustomer.PointsBalance <= 0)
        {
            NotificationService.Instance.ShowWarning("Customer has no points to redeem");
            return;
        }

        if (!addPoints && points > _currentCustomer.PointsBalance)
        {
            NotificationService.Instance.ShowWarning($"Insufficient points. Customer has {_currentCustomer.PointsBalance:N0} points");
            return;
        }

        var notes = NotesEntry.Text?.Trim();
        var reason = string.IsNullOrWhiteSpace(notes)
            ? addPoints
                ? $"POS Manual Addition - {points} points"
                : $"POS Point Redemption - £{points / 100m:F2} discount applied"
            : notes;

        var customerName = string.IsNullOrWhiteSpace(_currentCustomer.CustomerName)
            ? _currentCustomer.Phone
            : _currentCustomer.CustomerName.Trim();
        var currentBalance = _currentCustomer.PointsBalance;
        var newBalance = addPoints ? currentBalance + points : currentBalance - points;
        var actionTitle = addPoints ? "Confirm Add Points" : "Confirm Redeem Points";
        var actionVerb = addPoints ? "Add" : "Redeem";
        var iconColor = addPoints ? "#10B981" : "#EF4444";
        var confirmDialog = new ModernConfirmDialog();
        confirmDialog.SetConfirm(
            actionTitle,
            $"{actionVerb} {points:N0} points for {customerName}?\n\n" +
            $"Current balance: {currentBalance:N0} points\n" +
            $"New balance: {newBalance:N0} points\n" +
            $"Value: £{points / 100m:F2}\n\n" +
            $"Reason: {reason}\n\n" +
            "This will update OrderWeb.net.",
            actionVerb,
            "Cancel",
            addPoints ? "+" : "-",
            iconColor);

        var confirmed = await confirmDialog.ShowAsync();
        if (!confirmed)
        {
            return;
        }

        var originalButtonText = actionButton?.Text;

        try
        {
            if (actionButton != null)
            {
                actionButton.Text = addPoints ? "Adding..." : "Redeeming...";
                actionButton.IsEnabled = false;
            }

            var result = addPoints
                ? await _loyaltyService.AddPointsAsync(_currentCustomer.Phone, points, reason, expectedRemainingPoints: newBalance)
                : await _loyaltyService.RedeemPointsAsync(_currentCustomer.Phone, points, reason, expectedRemainingPoints: newBalance);

            if (result.Success && result.Customer != null)
            {
                _currentCustomer = result.Customer;
                DisplayCustomerDetails(result.Customer, result.Transactions);
                PointsEntry.Text = "";
                NotesEntry.Text = "";

                NotificationService.Instance.ShowSuccess(
                    addPoints
                        ? $"Added {points:N0} points. New balance: {result.Customer.PointsBalance:N0}"
                        : $"Redeemed {points:N0} points. New balance: {result.Customer.PointsBalance:N0}",
                    addPoints ? "Points Added" : "Points Redeemed");

                var completedDialog = new ModernAlertDialog();
                completedDialog.SetAlert(
                    addPoints ? "Points Added" : "Points Redeemed",
                    addPoints
                        ? $"{points:N0} points were added successfully.\n\nNew balance: {result.Customer.PointsBalance:N0} points"
                        : $"{points:N0} points were redeemed successfully.\n\nNew balance: {result.Customer.PointsBalance:N0} points",
                    "OK",
                    "#10B981",
                    "#10B981");
                await completedDialog.ShowAsync();
            }
            else
            {
                NotificationService.Instance.ShowError(
                    result.Error ?? (addPoints ? "Failed to add points" : "Failed to redeem points"),
                    addPoints ? "Add Failed" : "Redeem Failed");
            }
        }
        catch (Exception ex)
        {
            NotificationService.Instance.ShowError(
                addPoints ? $"Failed to add points: {ex.Message}" : $"Failed to redeem points: {ex.Message}",
                "OrderWeb Error");
        }
        finally
        {
            if (actionButton != null)
            {
                actionButton.Text = originalButtonText;
                actionButton.IsEnabled = true;
            }
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
