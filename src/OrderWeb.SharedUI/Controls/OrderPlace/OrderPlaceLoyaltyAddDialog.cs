using Microsoft.Maui.Controls.Shapes;
using OrderWeb.Contracts.Dtos;

namespace OrderWeb.SharedUI.Controls.OrderPlace;

/// <summary>Preview customer after lookup (host supplies; SharedUI does not call Mother/cloud).</summary>
public sealed record OrderPlaceLoyaltyAddCustomer(
    string Lookup,
    string Name,
    string? Phone,
    int PointsBalance);

/// <summary>Host add outcome for Order Place earn.</summary>
public sealed record OrderPlaceLoyaltyAddOutcome(
    bool Success,
    string Message,
    int? PointsAdded = null,
    int? PointsBalance = null,
    string? ErrorCode = null);

/// <summary>
/// More → Add Loyalty Points: lookup → preview bill/points → confirm add.
/// Host owns search + <c>POST /api/client/orders/loyalty-add</c> (or Mother local ops).
/// </summary>
public sealed class OrderPlaceLoyaltyAddDialog : ContentView
{
    private readonly Label _billLabel = MotherDialogVisuals.Message();
    private readonly Label _statusLabel = new()
    {
        FontSize = 13,
        TextColor = Color.FromArgb("#DC2626"),
        IsVisible = false,
        HorizontalTextAlignment = TextAlignment.Center,
        LineBreakMode = LineBreakMode.WordWrap
    };
    private readonly Entry _lookupEntry = new()
    {
        Placeholder = "Phone or loyalty card",
        FontSize = 16,
        HeightRequest = 44,
        BackgroundColor = Colors.Transparent,
        IsReadOnly = true,
        InputTransparent = true
    };
    private readonly Border _lookupBorder;
    private readonly Label _customerLabel = new()
    {
        FontSize = 15,
        FontAttributes = FontAttributes.Bold,
        HorizontalTextAlignment = TextAlignment.Center,
        IsVisible = false
    };
    private readonly Label _balanceLabel = new()
    {
        FontSize = 14,
        HorizontalTextAlignment = TextAlignment.Center,
        IsVisible = false
    };
    private readonly Button _lookupButton = new()
    {
        Text = "Look Up",
        Style = null,
        BackgroundColor = Color.FromArgb("#2563EB"),
        TextColor = Colors.White,
        FontAttributes = FontAttributes.Bold,
        CornerRadius = 14,
        HeightRequest = 48,
        FontSize = 16
    };
    private readonly Button _addButton = new()
    {
        Text = "Add Points",
        Style = null,
        BackgroundColor = Color.FromArgb("#059669"),
        TextColor = Colors.White,
        FontAttributes = FontAttributes.Bold,
        CornerRadius = 14,
        HeightRequest = 52,
        FontSize = 16,
        IsEnabled = false
    };
    private readonly Button _cancelButton = new()
    {
        Text = "Cancel",
        Style = null,
        BackgroundColor = Color.FromArgb("#E2E8F0"),
        TextColor = Color.FromArgb("#334155"),
        FontAttributes = FontAttributes.Bold,
        CornerRadius = 14,
        HeightRequest = 48,
        FontSize = 16
    };

    private TaskCompletionSource<OrderPlaceLoyaltyAddOutcome?>? _tcs;
    private ContentPage? _page;
    private decimal _billTotal;
    private int _pointsToAdd;
    private string? _stickyIdempotencyKey;
    private OrderPlaceLoyaltyAddCustomer? _customer;
    private bool _busy;
    private Func<string, Task<(bool Ok, string? Error, OrderPlaceLoyaltyAddCustomer? Customer)>>? _lookupAsync;
    private Func<string, int, string, Task<OrderPlaceLoyaltyAddOutcome>>? _addAsync;
    private Func<string, int, string>? _resolveStickyIdempotencyKey;

    public OrderPlaceLoyaltyAddDialog()
    {
        BackgroundColor = Color.FromArgb("#80000000");
        _customerLabel.Use(Label.TextColorProperty, "OwTextStrong");
        _balanceLabel.Use(Label.TextColorProperty, "OwTextMuted");

        _lookupBorder = new Border
        {
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(12, 4),
            Content = _lookupEntry
        };
        _lookupBorder.Use(Border.BackgroundColorProperty, "OwSurface");
        _lookupBorder.Use(Border.StrokeProperty, "OwBorderStrong");

        var lookupHit = new BoxView { Color = Colors.Transparent };
        var lookupTap = new TapGestureRecognizer();
        lookupTap.Tapped += async (_, _) => await OpenLookupKeyboardAsync();
        lookupHit.GestureRecognizers.Add(lookupTap);
        _lookupBorder.GestureRecognizers.Add(lookupTap);
        _lookupBorder.Content = new Grid { Children = { _lookupEntry, lookupHit } };

        _lookupButton.Clicked += async (_, _) => await LookupAsync();
        _addButton.Clicked += async (_, _) => await AddAsync();
        _cancelButton.Clicked += (_, _) => Complete(null);

        var buttons = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 12,
            Children = { _cancelButton }
        };
        buttons.Add(_addButton, 1);

        var body = new VerticalStackLayout
        {
            Spacing = 14,
            Children =
            {
                MotherDialogVisuals.IconCircle("★", size: 64, fontSize: 28, motherBlue: true),
                MotherDialogVisuals.Title("Add Loyalty Points"),
                MotherDialogVisuals.Message("Enter the customer phone or loyalty card to add points for this bill."),
                _billLabel,
                new Label { Text = "Customer", FontSize = 13, TextColor = Color.FromArgb("#64748B") },
                _lookupBorder,
                _lookupButton,
                _customerLabel,
                _balanceLabel,
                _statusLabel,
                buttons
            }
        };

        Content = MotherDialogVisuals.OverlayGrid(MotherDialogVisuals.Panel(460, 480, body, padding: 26));
    }

    public Task<OrderPlaceLoyaltyAddOutcome?> ShowAsync(
        ContentPage page,
        decimal billTotal,
        Func<string, Task<(bool Ok, string? Error, OrderPlaceLoyaltyAddCustomer? Customer)>> lookupAsync,
        Func<string, int, string, Task<OrderPlaceLoyaltyAddOutcome>> addAsync,
        Func<string, int, string>? resolveStickyIdempotencyKey = null)
    {
        _tcs = new TaskCompletionSource<OrderPlaceLoyaltyAddOutcome?>();
        _page = page;
        _billTotal = billTotal;
        _pointsToAdd = OrderPlaceLoyaltyEarnRules.ComputePointsFromBillTotal(billTotal);
        _lookupAsync = lookupAsync;
        _addAsync = addAsync;
        _resolveStickyIdempotencyKey = resolveStickyIdempotencyKey;
        _stickyIdempotencyKey = null;
        _customer = null;
        _busy = false;
        _lookupEntry.Text = string.Empty;
        _statusLabel.IsVisible = false;
        _customerLabel.IsVisible = false;
        _balanceLabel.IsVisible = false;
        _addButton.IsEnabled = false;
        _cancelButton.IsEnabled = true;
        _addButton.Text = _pointsToAdd > 0 ? $"Add {_pointsToAdd:N0} Points" : "Add Points";
        _billLabel.Text =
            _pointsToAdd > 0
                ? $"Bill £{_billTotal:F2} → will add {_pointsToAdd:N0} points (£1 = 1 pt)."
                : $"Bill £{_billTotal:F2} — no points to add (under £1).";
        _lookupButton.IsEnabled = _pointsToAdd > 0;
        return OrderPlaceDialogPresenter.ShowAsync(page, this, _tcs);
    }

    private async Task OpenLookupKeyboardAsync()
    {
        if (_busy || _page is null || _pointsToAdd <= 0)
        {
            return;
        }

        var keyboard = new VirtualKeyboardDialog();
        keyboard.SetPrompt("Customer phone or loyalty card", "DONE");
        keyboard.SetTextMode(VirtualKeyboardTextMode.Phone);
        keyboard.SetPlaceholder("e.g. 07123 456 789");
        keyboard.SetMaximumLength(32);
        keyboard.SetRequired(true);
        keyboard.SetInitialText(_lookupEntry.Text ?? string.Empty);
        var text = await keyboard.ShowAsync(_page);
        if (!string.IsNullOrWhiteSpace(text))
        {
            _lookupEntry.Text = text.Trim();
        }
    }

    private async Task LookupAsync()
    {
        if (_busy || _lookupAsync is null || _pointsToAdd <= 0)
        {
            return;
        }

        var lookup = (_lookupEntry.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(lookup))
        {
            ShowStatus("Enter a phone number or loyalty card.");
            return;
        }

        _busy = true;
        _lookupButton.IsEnabled = false;
        try
        {
            ClearStatus();
            var result = await _lookupAsync(lookup);
            if (!result.Ok || result.Customer is null)
            {
                _customer = null;
                _customerLabel.IsVisible = false;
                _balanceLabel.IsVisible = false;
                _addButton.IsEnabled = false;
                ShowStatus(result.Error ?? "Customer not found.");
                return;
            }

            _customer = result.Customer with { Lookup = lookup };
            _customerLabel.Text = string.IsNullOrWhiteSpace(_customer.Name) ? "Customer found" : _customer.Name;
            _customerLabel.IsVisible = true;
            _balanceLabel.Text = $"Current balance: {_customer.PointsBalance:N0} pts";
            _balanceLabel.IsVisible = true;
            _addButton.IsEnabled = true;
        }
        finally
        {
            _busy = false;
            _lookupButton.IsEnabled = true;
        }
    }

    private async Task AddAsync()
    {
        if (_busy || _addAsync is null || _customer is null || _pointsToAdd <= 0)
        {
            return;
        }

        _busy = true;
        _addButton.IsEnabled = false;
        _lookupButton.IsEnabled = false;
        _cancelButton.IsEnabled = false;
        try
        {
            ClearStatus();
            // Sticky key: host supplies stable key for retries; same add must not mint a new Guid.
            var key = _resolveStickyIdempotencyKey?.Invoke(_customer.Lookup, _pointsToAdd)
                      ?? _stickyIdempotencyKey
                      ?? $"{_customer.Lookup}:loyalty-order-add:{_pointsToAdd}:{Guid.NewGuid():N}";
            _stickyIdempotencyKey = key;
            var outcome = await _addAsync(_customer.Lookup, _pointsToAdd, key);
            if (!outcome.Success)
            {
                ShowStatus(outcome.Message);
                _addButton.IsEnabled = true;
                _lookupButton.IsEnabled = true;
                _cancelButton.IsEnabled = true;
                return;
            }

            Complete(outcome);
        }
        finally
        {
            _busy = false;
        }
    }

    private void ShowStatus(string message)
    {
        _statusLabel.Text = message;
        _statusLabel.IsVisible = !string.IsNullOrWhiteSpace(message);
    }

    private void ClearStatus()
    {
        _statusLabel.Text = string.Empty;
        _statusLabel.IsVisible = false;
    }

    private void Complete(OrderPlaceLoyaltyAddOutcome? result)
    {
        if (_busy && result is null)
        {
            return;
        }

        _tcs?.TrySetResult(result);
    }
}
