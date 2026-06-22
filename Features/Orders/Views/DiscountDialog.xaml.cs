using Microsoft.Maui.Controls;
using MySqlConnector;
using POS_in_NET.Models;
using POS_in_NET.Services;

namespace POS_in_NET.Views;

public partial class DiscountDialog : ContentView
{
    private TaskCompletionSource<DiscountDialogResult?>? _taskCompletionSource;
    private decimal _orderSubtotal;
    private bool _isFixedAmount = true;
    private decimal _discountValue = 0;
    private string? _selectedReason;
    private Button? _selectedReasonButton;
    
    // Reason button references
    private readonly Dictionary<string, Button> _reasonButtons = new();
    
    public DiscountDialog()
    {
        InitializeComponent();
    }
    
    public void SetOrderSubtotal(decimal subtotal)
    {
        _orderSubtotal = subtotal;
        OrderSubtotalLabel.Text = $"£{subtotal:F2}";
    }
    
    public Task<DiscountDialogResult?> ShowAsync()
    {
        _taskCompletionSource = new TaskCompletionSource<DiscountDialogResult?>();
        
        // Store reason buttons for easy selection management
        _reasonButtons["Staff"] = StaffButton;
        _reasonButtons["Family"] = FamilyButton;
        _reasonButtons["Good Will"] = GoodWillButton;
        _reasonButtons["Voucher"] = VoucherButton;
        _reasonButtons["Custom"] = CustomButton;
        
        // Add to page
        if (Application.Current?.MainPage is Page page)
        {
            if (page is Shell shell && shell.CurrentPage is ContentPage contentPage)
            {
                AddToPage(contentPage);
            }
            else if (page is ContentPage cp)
            {
                AddToPage(cp);
            }
        }
        
        return _taskCompletionSource.Task;
    }
    
    private void AddToPage(ContentPage page)
    {
        if (page.Content is Grid grid)
        {
            // Span all rows and columns to cover full page
            if (grid.RowDefinitions.Count > 0)
                Grid.SetRowSpan(this, grid.RowDefinitions.Count);
            if (grid.ColumnDefinitions.Count > 0)
                Grid.SetColumnSpan(this, grid.ColumnDefinitions.Count);
            
            grid.Children.Add(this);
        }
        else if (page.Content is Layout layout)
        {
            var newGrid = new Grid();
            var existingContent = page.Content;
            page.Content = null;
            newGrid.Children.Add(existingContent);
            newGrid.Children.Add(this);
            page.Content = newGrid;
        }
    }
    
    private void CloseDialog()
    {
        if (Parent is Grid grid)
        {
            grid.Children.Remove(this);
        }
    }
    
    private void OnFixedAmountClicked(object sender, EventArgs e)
    {
        _isFixedAmount = true;
        FixedAmountButton.BackgroundColor = Color.FromArgb("#4A90D9");
        FixedAmountButton.TextColor = Colors.White;
        PercentageButton.BackgroundColor = Color.FromArgb("#E8E8E8");
        PercentageButton.TextColor = Color.FromArgb("#555555");
        DiscountSymbol.Text = "£";
        UpdateDiscountDisplay();
    }
    
    private void OnPercentageClicked(object sender, EventArgs e)
    {
        _isFixedAmount = false;
        PercentageButton.BackgroundColor = Color.FromArgb("#4A90D9");
        PercentageButton.TextColor = Colors.White;
        FixedAmountButton.BackgroundColor = Color.FromArgb("#E8E8E8");
        FixedAmountButton.TextColor = Color.FromArgb("#555555");
        DiscountSymbol.Text = "%";
        UpdateDiscountDisplay();
    }
    
    private void OnDiscountAmountChanged(object sender, TextChangedEventArgs e)
    {
        if (decimal.TryParse(e.NewTextValue, out decimal value))
        {
            _discountValue = value;
        }
        else
        {
            _discountValue = 0;
        }
        UpdateDiscountDisplay();
    }
    
    private void UpdateDiscountDisplay()
    {
        decimal actualDiscount;
        if (_isFixedAmount)
        {
            actualDiscount = _discountValue;
        }
        else
        {
            // Calculate percentage
            actualDiscount = Math.Round(_orderSubtotal * (_discountValue / 100), 2);
        }
        
        // Cap at subtotal
        if (actualDiscount > _orderSubtotal)
        {
            actualDiscount = _orderSubtotal;
        }
        
        DiscountAmountLabel.Text = $"£{actualDiscount:F2}";
    }
    
    private void SelectReasonButton(Button button, string reason)
    {
        // Deselect previous
        if (_selectedReasonButton != null)
        {
            _selectedReasonButton.BackgroundColor = Color.FromArgb("#F0F0F0");
            _selectedReasonButton.TextColor = Color.FromArgb("#555555");
        }
        
        // Select new
        _selectedReasonButton = button;
        button.BackgroundColor = Color.FromArgb("#4A90D9");
        button.TextColor = Colors.White;
        
        _selectedReason = reason;
        
        // Hide custom entry if not custom
        if (reason != "Custom")
        {
            CustomReasonFrame.IsVisible = false;
        }
    }
    
    private void OnReasonClicked(object sender, EventArgs e)
    {
        if (sender is Button button)
        {
            SelectReasonButton(button, button.Text);
        }
    }
    
    private void OnCustomReasonClicked(object sender, EventArgs e)
    {
        if (sender is Button button)
        {
            SelectReasonButton(button, "Custom");
            CustomReasonFrame.IsVisible = true;
            CustomReasonEntry.Focus();
        }
    }
    
    private void OnRemoveDiscountClicked(object sender, EventArgs e)
    {
        // Return zero discount
        _taskCompletionSource?.TrySetResult(new DiscountDialogResult
        {
            DiscountAmount = 0,
            DiscountPercent = 0,
            Reason = null,
            DiscountType = "removed"
        });
        CloseDialog();
    }
    
    private async void OnApplyClicked(object sender, EventArgs e)
    {
        // Validate discount amount
        if (_discountValue <= 0)
        {
            var alert = new ModernAlertDialog();
            alert.SetAlert("Invalid Amount", "Please enter a discount amount.", "", "#DC2626", "White");
            await alert.ShowAsync();
            return;
        }
        
        // Validate reason
        if (string.IsNullOrEmpty(_selectedReason))
        {
            var alert = new ModernAlertDialog();
            alert.SetAlert("Select Reason", "Please select a discount reason.", "", "#DC2626", "White");
            await alert.ShowAsync();
            return;
        }
        
        // Get custom reason text if selected
        string finalReason = _selectedReason;
        if (_selectedReason == "Custom")
        {
            var customText = CustomReasonEntry.Text?.Trim();
            if (string.IsNullOrEmpty(customText))
            {
                var alert = new ModernAlertDialog();
                alert.SetAlert("Custom Reason", "Please enter a custom reason.", "", "#DC2626", "White");
                await alert.ShowAsync();
                return;
            }
            finalReason = customText;
        }
        
        // Calculate actual discount
        decimal actualDiscount;
        decimal discountPercent = 0;
        
        if (_isFixedAmount)
        {
            actualDiscount = _discountValue;
        }
        else
        {
            discountPercent = _discountValue;
            actualDiscount = Math.Round(_orderSubtotal * (_discountValue / 100), 2);
        }
        
        // Cap at subtotal
        if (actualDiscount > _orderSubtotal)
        {
            actualDiscount = _orderSubtotal;
        }
        
        // Check if Manager PIN is required (discount over £30)
        var approvalRequired = actualDiscount > 30;
        DiscountApprovalInfo? approvedBy = null;

        if (approvalRequired)
        {
            var authService = Application.Current?.MainPage?.Handler?.MauiContext?.Services.GetService<AuthenticationService>();
            var currentUser = authService?.CurrentUser;
            
            // Check if current user is Manager or Admin
            bool isManagerOrAdmin = currentUser != null && 
                                    (currentUser.Role == UserRole.Manager || currentUser.Role == UserRole.Admin);
            
            if (isManagerOrAdmin)
            {
                approvedBy = new DiscountApprovalInfo
                {
                    UserId = currentUser?.Id,
                    Name = !string.IsNullOrWhiteSpace(currentUser?.Name) ? currentUser.Name : currentUser?.Username ?? "Manager",
                    Role = currentUser?.Role.ToString() ?? string.Empty
                };
            }
            else
            {
                // Request Manager PIN
                var pinDialog = new StyledPromptDialog();
                pinDialog.SetDialog(
                    "Manager PIN Required",
                    $"Discount of £{actualDiscount:F2} requires manager approval:",
                    "Enter PIN",
                    Keyboard.Numeric,
                    ""
                );
                var pin = await pinDialog.ShowAsync();
                
                if (string.IsNullOrEmpty(pin))
                {
                    return; // Cancelled
                }
                
                approvedBy = await ValidateManagerPinAsync(pin);
                if (approvedBy == null)
                {
                    var alert = new ModernAlertDialog();
                    alert.SetAlert("Invalid PIN", "Manager approval could not be verified.", "", "#DC2626", "White");
                    await alert.ShowAsync();
                    return;
                }
            }
        }
        
        _taskCompletionSource?.TrySetResult(new DiscountDialogResult
        {
            DiscountAmount = actualDiscount,
            DiscountPercent = discountPercent,
            Reason = finalReason,
            DiscountType = _isFixedAmount ? "fixed" : "percent",
            ApprovalRequired = approvalRequired,
            ApprovedBy = approvedBy
        });
        CloseDialog();
    }
    
    private void OnCancelClicked(object sender, EventArgs e)
    {
        _taskCompletionSource?.TrySetResult(null);
        CloseDialog();
    }

    private static async Task<DiscountApprovalInfo?> ValidateManagerPinAsync(string pin)
    {
        if (string.IsNullOrWhiteSpace(pin))
        {
            return null;
        }

        try
        {
            var databaseService = Application.Current?.MainPage?.Handler?.MauiContext?.Services.GetService<DatabaseService>()
                ?? new DatabaseService();

            await using var connection = await databaseService.GetConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT id, name, username, password_hash, role
                FROM users
                WHERE role IN ('admin', 'manager')";

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var hash = reader.IsDBNull(reader.GetOrdinal("password_hash"))
                    ? string.Empty
                    : reader.GetString("password_hash");

                if (!string.IsNullOrWhiteSpace(hash) && BCrypt.Net.BCrypt.Verify(pin, hash))
                {
                    var name = reader.IsDBNull(reader.GetOrdinal("name"))
                        ? string.Empty
                        : reader.GetString("name");
                    var username = reader.IsDBNull(reader.GetOrdinal("username"))
                        ? string.Empty
                        : reader.GetString("username");

                    return new DiscountApprovalInfo
                    {
                        UserId = reader.GetInt32(reader.GetOrdinal("id")),
                        Name = !string.IsNullOrWhiteSpace(name) ? name : username,
                        Role = reader.IsDBNull(reader.GetOrdinal("role"))
                            ? string.Empty
                            : reader.GetString("role")
                    };
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Manager PIN validation failed: {ex.Message}");
        }

        return null;
    }
}
