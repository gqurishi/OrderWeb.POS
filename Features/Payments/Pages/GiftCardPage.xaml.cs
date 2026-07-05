using POS_in_NET.Models;
using POS_in_NET.Services;

namespace POS_in_NET.Pages;

public partial class GiftCardPage : ContentPage
{
    private readonly LoyaltyService _loyaltyService;
    private readonly DatabaseService _databaseService;
    private readonly AuthenticationService _authService;
    private readonly RoleAccessService _roleAccessService;
    private GiftCard? _currentGiftCard;

    public GiftCardPage()
    {
        InitializeComponent();
        
        // Set the page title in the TopBar
        TopBar.SetPageTitle("Gift Cards");
        
        _loyaltyService = ServiceHelper.GetService<LoyaltyService>()
            ?? throw new InvalidOperationException("LoyaltyService not found");
        _databaseService = ServiceHelper.GetService<DatabaseService>()
            ?? throw new InvalidOperationException("DatabaseService not found");
        _authService = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
        _roleAccessService = ServiceHelper.GetService<RoleAccessService>() ?? new RoleAccessService();
        
        // Subscribe to notification events
        NotificationService.Instance.NotificationRequested += OnNotificationRequested;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (!_roleAccessService.IsManagerOrAdmin(_authService.CurrentUser?.Role))
        {
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Access Denied", "Only Manager and Admin can access Gift Cards.");
            await Shell.Current.GoToAsync($"//{_roleAccessService.ResolveDashboardRoute(_authService.CurrentUser?.Role)}");
            return;
        }
        
        // Reset form when page appears
        GiftCardDetailsFrame.IsVisible = false;
        
        // Ensure LoyaltyService is initialized with latest settings
        try
        {
            await _loyaltyService.ReinitializeAsync();
            System.Diagnostics.Debug.WriteLine(" Gift Card page: LoyaltyService reinitialized");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Failed to reinitialize LoyaltyService: {ex.Message}");
        }
    }
    
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Unsubscribe from notification events
        NotificationService.Instance.NotificationRequested -= OnNotificationRequested;
    }
    
    private void OnNotificationRequested(object? sender, NotificationEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await ToastNotification.ShowAsync(e.Title, e.Message, e.Type, e.DurationMs);
        });
    }

    private async void OnCloseClicked(object sender, EventArgs e)
    {
        try
        {
            var dashboardRoute = _roleAccessService.ResolveDashboardRoute(_authService.CurrentUser?.Role);
            await Shell.Current.GoToAsync($"//{dashboardRoute}", false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Gift card close navigation failed: {ex.Message}");
            await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Navigation Error", "Could not return to the dashboard. Please try again.");
        }
    }
    
    private async void OnCheckGiftCardClicked(object sender, EventArgs e)
    {
        var cardNumber = GiftCardEntry.Text?.Trim();

        if (string.IsNullOrWhiteSpace(cardNumber))
        {
            NotificationService.Instance.ShowWarning("Please enter a gift card number");
            return;
        }

        try
        {
            // Show loading state
            CheckBalanceButton.Text = "Checking...";
            CheckBalanceButton.IsEnabled = false;
            GiftCardEntry.IsEnabled = false;

            System.Diagnostics.Debug.WriteLine($"UI: Checking gift card: {cardNumber}");

            var result = await _loyaltyService.CheckGiftCardBalanceAsync(cardNumber);

            System.Diagnostics.Debug.WriteLine($"UI: Result - Success: {result.Success}, Error: {result.Error}");

            if (result.GiftCard != null)
            {
                _currentGiftCard = result.GiftCard;
                DisplayGiftCardDetails(result.GiftCard);
                GiftCardDetailsFrame.IsVisible = true;

                if (result.Success)
                {
                    NotificationService.Instance.ShowSuccess(
                        $"Balance: {result.GiftCard.BalanceDisplay}", 
                        "Gift Card Found!");
                }
                else
                {
                    NotificationService.Instance.ShowWarning(
                        result.Error ?? "Gift card cannot be used.",
                        "Gift Card Checked");
                }
            }
            else
            {
                GiftCardDetailsFrame.IsVisible = false;
                _currentGiftCard = null;
                
                // Show smart, user-friendly error message
                NotificationService.Instance.ShowError(
                    "Card not found. Please verify the number and try again.",
                    "Not Found");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error checking gift card: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"   Stack: {ex.StackTrace}");
            
            NotificationService.Instance.ShowError(
                "Failed to check gift card. Please check your connection and try again.",
                "Error");
        }
        finally
        {
            CheckBalanceButton.Text = "Check Balance";
            CheckBalanceButton.IsEnabled = true;
            GiftCardEntry.IsEnabled = true;
        }
    }

    private async void OnRedeemAmountClicked(object sender, EventArgs e)
    {
        if (_currentGiftCard == null)
        {
            NotificationService.Instance.ShowWarning("Please check a gift card first");
            return;
        }

        if (_currentGiftCard.Balance <= 0)
        {
            NotificationService.Instance.ShowWarning("This gift card has no balance remaining");
            return;
        }

        // Show custom dialog for amount
        var dialog = new Views.StyledPromptDialog();
        dialog.SetDialog(
            "Redeem Gift Card",
            $"Available balance: {_currentGiftCard.BalanceDisplay}\n\nEnter amount to redeem:",
            "0.00",
            Keyboard.Numeric
        );
        
        var amountStr = await dialog.ShowAsync();

        if (string.IsNullOrWhiteSpace(amountStr))
            return;

        if (!decimal.TryParse(amountStr, out decimal amount) || amount <= 0)
        {
            NotificationService.Instance.ShowWarning("Please enter a valid amount greater than 0");
            return;
        }

        if (amount > _currentGiftCard.Balance)
        {
            NotificationService.Instance.ShowWarning(
                $"Amount exceeds balance (Max: {_currentGiftCard.BalanceDisplay})",
                "Amount Too High");
            return;
        }

        // Confirm redemption
        var confirmDialog = new POS_in_NET.Views.ModernConfirmDialog();
        confirmDialog.SetConfirm(
            "Confirm Redemption",
            $"Redeem £{amount:F2} from gift card?\n\n" +
            $"Card: {_currentGiftCard.CardNumber}\n" +
            $"Current Balance: {_currentGiftCard.BalanceDisplay}\n" +
            $"Remaining after: £{(_currentGiftCard.Balance - amount):F2}",
            "Redeem",
            "Cancel",
            "£",
            "#2563EB");

        var confirm = await confirmDialog.ShowAsync();

        if (!confirm)
            return;

        try
        {
            // Show loading
            RedeemButton.Text = "Redeeming...";
            RedeemButton.IsEnabled = false;

            var description = $"POS Redemption - Order Payment";
            var manualOrderId = $"POS-GIFTCARD-{DateTime.Now:yyyyMMddHHmmss}";
            var result = await _loyaltyService.RedeemGiftCardAsync(
                _currentGiftCard.CardNumber, 
                amount, 
                description,
                manualOrderId,
                manualOrderId);

            if (result.Success)
            {
                var previousBalance = _currentGiftCard.Balance;
                var refreshedCard = await RefreshGiftCardDetails(showNotification: false);
                var redeemedAmount = result.EffectiveAmountRedeemed ?? amount;
                var remainingBalance = result.EffectiveRemainingBalance
                    ?? refreshedCard?.Balance
                    ?? Math.Max(0, previousBalance - redeemedAmount);

                NotificationService.Instance.ShowSuccess(
                    $"Redeemed: £{redeemedAmount:F2} | Remaining: £{remainingBalance:F2}", 
                    "Redemption Successful!");
            }
            else
            {
                NotificationService.Instance.ShowError(
                    result.Error ?? "Failed to redeem gift card. Please try again.",
                    "Redemption Failed");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error redeeming gift card: {ex.Message}");
            NotificationService.Instance.ShowError(
                "Failed to redeem gift card. Please try again.",
                "Error");
        }
        finally
        {
            RedeemButton.Text = "Redeem";
            RedeemButton.IsEnabled = true;
        }
    }

    private async void OnRefreshGiftCardClicked(object sender, EventArgs e)
    {
        if (_currentGiftCard == null)
            return;

        await RefreshGiftCardDetails();
    }

    private async Task<GiftCard?> RefreshGiftCardDetails(bool showNotification = true)
    {
        if (_currentGiftCard == null)
            return null;

        try
        {
            var result = await _loyaltyService.CheckGiftCardBalanceAsync(_currentGiftCard.CardNumber);

            if (result.GiftCard != null)
            {
                _currentGiftCard = result.GiftCard;
                DisplayGiftCardDetails(result.GiftCard);
                if (showNotification)
                {
                    if (result.Success)
                    {
                        NotificationService.Instance.ShowSuccess("Gift card details refreshed", "Refreshed");
                    }
                    else
                    {
                        NotificationService.Instance.ShowWarning(
                            result.Error ?? "Gift card details refreshed, but this card cannot be used.",
                            "Refreshed");
                    }
                }
                return result.GiftCard;
            }
            else
            {
                NotificationService.Instance.ShowError("Failed to refresh gift card details", "Error");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error refreshing gift card: {ex.Message}");
            NotificationService.Instance.ShowError("Failed to refresh gift card", "Error");
        }

        return null;
    }

    private void DisplayGiftCardDetails(GiftCard giftCard)
    {
        CardNumberLabel.Text = string.IsNullOrWhiteSpace(giftCard.CardNumber)
            ? "No card number"
            : giftCard.CardNumber.Trim();
        BalanceLabel.Text = giftCard.BalanceDisplay;
        CardTypeLabel.Text = string.IsNullOrWhiteSpace(giftCard.CardTypeDisplay)
            ? "Card type not saved"
            : giftCard.CardTypeDisplay;
        StatusLabel.Text = giftCard.StatusDisplay;
        ExpiryLabel.Text = string.IsNullOrWhiteSpace(giftCard.ExpiryDisplay)
            ? "No expiry date"
            : giftCard.ExpiryDisplay;

        // Update status color based on card state
        if (giftCard.IsExpired)
        {
            StatusLabel.TextColor = Color.FromArgb("#DC2626");
            StatusLabel.Text = "Expired";
            RedeemButton.IsEnabled = false;
            RedeemButton.BackgroundColor = Color.FromArgb("#94A3B8");
        }
        else if (!giftCard.IsActive)
        {
            StatusLabel.TextColor = Color.FromArgb("#C2410C");
            StatusLabel.Text = "Inactive";
            RedeemButton.IsEnabled = false;
            RedeemButton.BackgroundColor = Color.FromArgb("#94A3B8");
        }
        else if (giftCard.Balance <= 0)
        {
            StatusLabel.TextColor = Color.FromArgb("#64748B");
            StatusLabel.Text = "Zero Balance";
            RedeemButton.IsEnabled = false;
            RedeemButton.BackgroundColor = Color.FromArgb("#94A3B8");
        }
        else
        {
            StatusLabel.TextColor = Color.FromArgb("#059669");
            StatusLabel.Text = "Active";
            RedeemButton.IsEnabled = true;
            RedeemButton.BackgroundColor = Color.FromArgb("#10B981");
        }

        System.Diagnostics.Debug.WriteLine($"Displayed gift card: {giftCard.CardNumber} - Balance: {giftCard.BalanceDisplay}");
    }

    private async void OnLogoutClicked(object sender, EventArgs e)
    {
        // Get authentication service
        var authService = ServiceHelper.GetService<AuthenticationService>();
        if (authService != null)
        {
            await authService.LogoutAsync();
        }

        // Navigate to login page immediately
        await Shell.Current.GoToAsync("//login");
    }
}
