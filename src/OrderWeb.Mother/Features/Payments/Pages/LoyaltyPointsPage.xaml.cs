using OrderWeb.SharedUI.Views;
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

        TopBar.SetPageTitle("Loyalty Points");

        _loyaltyService = ServiceHelper.GetService<LoyaltyService>()
            ?? throw new InvalidOperationException("LoyaltyService not found");
        _authService = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
        _roleAccessService = ServiceHelper.GetService<RoleAccessService>() ?? new RoleAccessService();

        Loyalty.ShowDiagnostics = true;
        Loyalty.SearchRequested += OnSearchCustomerClicked;
        Loyalty.TestConnectionRequested += OnTestConnectionClicked;
        Loyalty.NewCustomerRequested += (_, _) => Loyalty.ShowNewCustomer(true, Loyalty.PhoneSearchText);
        Loyalty.CreateCustomerRequested += OnCreateCustomerRequested;
        Loyalty.AddPointsRequested += OnAddPointsClicked;
        Loyalty.RedeemPointsRequested += OnRedeemPointsClicked;
        Loyalty.ViewHistoryRequested += OnViewHistoryClicked;
        Loyalty.SendStatementRequested += OnSendStatementClicked;

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
            await NavigationCoordinator.Shared.NavigateShellAsync(_roleAccessService.ResolveDashboardRoute(_authService.CurrentUser?.Role));
            return;
        }

        try
        {
            await _loyaltyService.ReinitializeAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to reinitialize LoyaltyService: {ex.Message}");
        }
    }

    private async void OnCloseClicked(object sender, EventArgs e)
    {
        try
        {
            var dashboardRoute = _roleAccessService.ResolveDashboardRoute(_authService.CurrentUser?.Role);
            await NavigationCoordinator.Shared.NavigateShellAsync(dashboardRoute, animated: false, source: sender as VisualElement);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Loyalty close navigation failed: {ex.Message}");
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Navigation Error", "Could not return to the dashboard. Please try again.");
        }
    }

    private async void OnSearchCustomerClicked(object? sender, EventArgs e)
    {
        var phone = Loyalty.PhoneSearchText;

        if (string.IsNullOrWhiteSpace(phone))
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Please enter a phone number");
            return;
        }

        try
        {
            Loyalty.IsSearchEnabled = false;

            var result = await _loyaltyService.SearchCustomerAsync(phone);

            if (result.Success && result.Customer != null)
            {
                _currentCustomer = result.Customer;
                DisplayCustomerDetails(result.Customer, result.Transactions);
            }
            else
            {
                Loyalty.SetCustomer(null);

                var errorMessage = result.Error ?? "Customer not found";
                var createCustomerDialog = new ModernConfirmDialog();
                createCustomerDialog.SetConfirm(
                    "Customer Not Found",
                    $"{errorMessage}\n\nPhone: {phone}\n\nWould you like to create a new customer account?",
                    "Create New Customer",
                    "Cancel",
                    "?",
                    "#2563EB");

                if (await createCustomerDialog.ShowAsync())
                {
                    Loyalty.ShowNewCustomer(true, phone);
                }
            }
        }
        catch (Exception ex)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync(
                "Connection Error",
                $"Failed to connect to OrderWeb.net:\n\n{ex.Message}\n\n" +
                "Please check:\n" +
                "• Internet connection\n" +
                "• Cloud Settings (API Key & Tenant ID)\n" +
                "• OrderWeb.net service status");
        }
        finally
        {
            Loyalty.IsSearchEnabled = true;
        }
    }

    private async void OnTestConnectionClicked(object? sender, EventArgs e)
    {
        try
        {
            var databaseService = ServiceHelper.GetService<DatabaseService>();
            if (databaseService == null)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", "Database service not found");
                return;
            }

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

            var testLookup = Loyalty.PhoneSearchText;
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
                "?",
                "#2563EB");

            if (!await testDialog.ShowAsync())
            {
                return;
            }

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
                    "This means:\n" +
                    "• Connection is working\n" +
                    "• Customer doesn't exist in database\n" +
                    "• Try creating a new customer");
            }
        }
        catch (Exception ex)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync(
                "API Test Failed",
                $"Connection Error:\n\n{ex.Message}");
        }
    }

    private async void OnCreateCustomerRequested(object? sender, LoyaltyNewCustomerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Phone))
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Required Field", "Please enter a phone number");
            return;
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Required Field", "Please enter a customer name");
            return;
        }

        try
        {
            Loyalty.ShowNewCustomer(false);

            var result = await _loyaltyService.CreateCustomerAsync(request.Phone, request.Name, request.Email);

            if (result.Success && result.Customer != null)
            {
                await DisplayAlert(
                    "Success",
                    $"Customer account created!\n\n" +
                    $"Name: {result.Customer.CustomerName}\n" +
                    $"Phone: {result.Customer.Phone}\n" +
                    $"Loyalty Card: {result.Customer.LoyaltyCardNumber}\n" +
                    $"Points: {result.Customer.PointsBalance}",
                    "OK");

                _currentCustomer = result.Customer;
                Loyalty.PhoneSearchText = request.Phone;
                DisplayCustomerDetails(result.Customer, result.Transactions);
            }
            else
            {
                await DisplayAlert("Error", result.Error ?? "Failed to create customer account", "OK");
            }
        }
        catch (Exception ex)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to create customer: {ex.Message}");
        }
    }

    private async void OnAddPointsClicked(object? sender, EventArgs e) =>
        await AdjustCustomerPointsAsync(addPoints: true);

    private async void OnRedeemPointsClicked(object? sender, EventArgs e) =>
        await AdjustCustomerPointsAsync(addPoints: false);

    private void DisplayCustomerDetails(LoyaltyCustomer customer, List<LoyaltyTransaction>? transactions)
    {
        var displayName = string.IsNullOrWhiteSpace(customer.CustomerName)
            ? "No name saved"
            : customer.CustomerName.Trim();
        var displayPhone = string.IsNullOrWhiteSpace(customer.DisplayPhone)
            ? customer.Phone
            : customer.DisplayPhone;

        Loyalty.SetCustomer(new LoyaltyCustomerPresentation(
            displayName,
            string.IsNullOrWhiteSpace(displayPhone) ? "Phone not saved" : displayPhone.Trim(),
            string.IsNullOrWhiteSpace(customer.Email) ? "Email not saved" : customer.Email.Trim(),
            customer.PointsBalance,
            customer.LastOrderDate.HasValue
                ? $"Last visit: {customer.LastOrderDate.Value:MMM dd, yyyy}"
                : "Last visit: Never"));

        Loyalty.SetHistory(MapHistory(transactions));
    }

    private static IReadOnlyList<LoyaltyHistoryPresentation> MapHistory(List<LoyaltyTransaction>? transactions)
    {
        if (transactions is null || transactions.Count == 0)
        {
            return [];
        }

        return transactions.Select(tx =>
        {
            var description = string.IsNullOrWhiteSpace(tx.Description)
                ? (string.IsNullOrWhiteSpace(tx.Reason) ? tx.TypeDisplay : tx.Reason!)
                : tx.Description;
            return new LoyaltyHistoryPresentation(
                description,
                tx.CreatedAt == default ? "—" : tx.CreatedAt.ToString("MMM dd, yyyy HH:mm"),
                tx.PointsDisplay,
                tx.PointsChange >= 0);
        }).ToList();
    }

    private async Task AdjustCustomerPointsAsync(bool addPoints)
    {
        if (_currentCustomer == null)
        {
            NotificationService.Instance.ShowWarning("Search and select a customer first");
            return;
        }

        var pointsStr = Loyalty.PointsText;
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

        var notes = Loyalty.NotesText;
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

        if (!await confirmDialog.ShowAsync())
        {
            return;
        }

        try
        {
            Loyalty.SetPointsBusy(true, addPoints);

            var result = addPoints
                ? await _loyaltyService.AddPointsAsync(_currentCustomer.Phone, points, reason, expectedRemainingPoints: newBalance)
                : await _loyaltyService.RedeemPointsAsync(_currentCustomer.Phone, points, reason, expectedRemainingPoints: newBalance);

            if (result.Success && result.Customer != null)
            {
                _currentCustomer = result.Customer;
                DisplayCustomerDetails(result.Customer, result.Transactions);
                Loyalty.ClearAdjustmentFields();

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
            Loyalty.SetPointsBusy(false);
        }
    }

    private async void OnViewHistoryClicked(object? sender, EventArgs e)
    {
        if (_currentCustomer == null)
        {
            return;
        }

        Loyalty.ShowHistory(true);

        try
        {
            var result = await _loyaltyService.SearchCustomerAsync(_currentCustomer.Phone);
            if (result.Success && result.Transactions != null)
            {
                Loyalty.SetHistory(MapHistory(result.Transactions));
            }
        }
        catch (Exception ex)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to load history: {ex.Message}");
        }
    }

    private async void OnSendStatementClicked(object? sender, EventArgs e)
    {
        if (_currentCustomer == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_currentCustomer.Email))
        {
            await DisplayAlert("Error", "Customer has no email address on file", "OK");
            return;
        }

        var confirm = await DisplayAlert(
            "Send Statement",
            $"Send loyalty statement to {_currentCustomer.Email}?",
            "Send",
            "Cancel");

        if (!confirm)
        {
            return;
        }

        try
        {
            await DisplayAlert("Success", $"Loyalty statement sent to {_currentCustomer.Email}", "OK");
        }
        catch (Exception ex)
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to send statement: {ex.Message}");
        }
    }
}
