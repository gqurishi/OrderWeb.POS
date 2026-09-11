using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using OrderWeb.SharedUI.Controls;

namespace OrderWeb.SharedUI.Payments;

public sealed class PaymentGiftCardLookupResult
{
    public bool Success { get; init; }
    public bool CanUse { get; init; }
    public string? CardNumber { get; init; }
    public decimal Balance { get; init; }
    public string? Message { get; init; }
}

public sealed class PaymentGiftCardResult
{
    public bool Success { get; init; }
    public string? CardNumber { get; init; }
    public decimal AmountApplied { get; init; }
    public decimal PreviousCardBalance { get; init; }
    public decimal NewCardBalance { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Mother-chrome GIFT CARD PAYMENT dialog.
/// SharedUI owns chrome only — host provides lookup; APPLY returns card+amount
/// (Client posts redeem via Mother payments API; do not redeem in SharedUI).
/// </summary>
public sealed class PaymentGiftCardDialog : ContentView
{
    private const double PreferredWidth = 650;
    private const double PreferredHeight = 760;

    private readonly Border _dialogCard;
    private readonly VerticalStackLayout _dialogContent;
    private readonly Button _closeButton;
    private readonly Label _amountDueLabel;
    private readonly Entry _giftCardNumberEntry;
    private readonly Button _checkBalanceButton;
    private readonly Label _statusMessageLabel;
    private readonly Border _balanceFrame;
    private readonly Label _balanceLabel;
    private readonly VerticalStackLayout _applyAmountSection;
    private readonly Entry _applyAmountEntry;
    private readonly Button _useFullButton;
    private readonly Border _remainingFrame;
    private readonly Label _remainingLabel;
    private readonly Button _applyButton;

    private Func<string, CancellationToken, Task<PaymentGiftCardLookupResult>>? _lookupAsync;
    private TaskCompletionSource<PaymentGiftCardResult>? _tcs;
    private Grid? _parent;
    private ContentPage? _hostPage;
    private decimal _amountDue;
    private decimal _cardBalance;
    private string? _cardNumber;
    private bool _cardReady;
    private bool _isUpdatingApplyAmount;
    private bool _keyboardOpen;
    private bool _busy;

    public PaymentGiftCardDialog()
    {
        BackgroundColor = Color.FromArgb("#80000000");
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;

        var titleLabel = new Label
        {
            Text = "GIFT CARD PAYMENT",
            FontSize = 28,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#111827"),
            VerticalOptions = LayoutOptions.Center
        };

        _closeButton = new Button
        {
            Text = "CLOSE",
            BackgroundColor = Color.FromArgb("#DC2626"),
            TextColor = Colors.White,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            WidthRequest = 90,
            HeightRequest = 45,
            CornerRadius = 14
        };
        _closeButton.Clicked += (_, _) => Complete(new PaymentGiftCardResult { Success = false, Message = "Cancelled." });

        var header = new Grid
        {
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) },
            Children = { titleLabel }
        };
        header.Add(_closeButton, 1);

        var giftBadge = new Border
        {
            BackgroundColor = Color.FromArgb("#F3E8FF"),
            Stroke = Color.FromArgb("#A855F7"),
            StrokeThickness = 2,
            WidthRequest = 150,
            HeightRequest = 80,
            HorizontalOptions = LayoutOptions.Center,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Content = new Label
            {
                Text = "GIFT CARD",
                FontSize = 18,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#7C3AED"),
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };

        _amountDueLabel = new Label
        {
            Text = "£0.00",
            FontSize = 28,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#7C3AED"),
            VerticalOptions = LayoutOptions.Center
        };

        var amountDueCard = new Border
        {
            BackgroundColor = Color.FromArgb("#FAF5FF"),
            Stroke = Color.FromArgb("#A855F7"),
            StrokeThickness = 2,
            Padding = 18,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Content = new Grid
            {
                ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) },
                Children =
                {
                    new Label
                    {
                        Text = "AMOUNT DUE:",
                        FontSize = 20,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#6B21A8"),
                        VerticalOptions = LayoutOptions.Center
                    }
                }
            }
        };
        ((Grid)amountDueCard.Content!).Add(_amountDueLabel, 1);

        _giftCardNumberEntry = new Entry
        {
            Placeholder = "Enter or scan gift card number",
            FontSize = 16,
            BackgroundColor = Colors.Transparent,
            IsReadOnly = true,
            VerticalOptions = LayoutOptions.Center
        };
        _giftCardNumberEntry.Focused += OnGiftCardNumberFocused;
        _giftCardNumberEntry.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () => await OpenGiftCardNumberKeyboardAsync())
        });

        _checkBalanceButton = new Button
        {
            Text = "CHECK",
            BackgroundColor = Color.FromArgb("#7C3AED"),
            TextColor = Colors.White,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 14,
            WidthRequest = 100,
            HeightRequest = 55
        };
        _checkBalanceButton.Clicked += async (_, _) => await OnCheckBalanceClickedAsync();

        var numberRow = new Grid
        {
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) },
            ColumnSpacing = 10
        };
        numberRow.Add(new Border
        {
            BackgroundColor = Color.FromArgb("#F3F4F6"),
            Stroke = Color.FromArgb("#CBD5E1"),
            StrokeThickness = 1,
            Padding = new Thickness(14, 0),
            HeightRequest = 55,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Content = _giftCardNumberEntry
        }, 0);
        numberRow.Add(_checkBalanceButton, 1);

        _statusMessageLabel = new Label
        {
            FontSize = 13,
            TextColor = Color.FromArgb("#6B7280"),
            IsVisible = false
        };

        var numberSection = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new Label
                {
                    Text = "GIFT CARD NUMBER",
                    FontSize = 14,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#6B7280")
                },
                numberRow,
                _statusMessageLabel
            }
        };

        _balanceLabel = new Label
        {
            Text = "£0.00",
            FontSize = 26,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#047857"),
            VerticalOptions = LayoutOptions.Center
        };
        _balanceFrame = new Border
        {
            BackgroundColor = Color.FromArgb("#ECFDF5"),
            Stroke = Color.FromArgb("#10B981"),
            StrokeThickness = 2,
            Padding = 16,
            IsVisible = false,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Content = new Grid
            {
                ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) },
                Children =
                {
                    new Label
                    {
                        Text = "CARD BALANCE:",
                        FontSize = 18,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#047857"),
                        VerticalOptions = LayoutOptions.Center
                    }
                }
            }
        };
        ((Grid)_balanceFrame.Content!).Add(_balanceLabel, 1);

        _applyAmountEntry = new Entry
        {
            Placeholder = "Amount to use",
            FontSize = 16,
            BackgroundColor = Colors.Transparent,
            IsReadOnly = true,
            VerticalOptions = LayoutOptions.Center
        };
        _applyAmountEntry.Focused += OnApplyAmountFocused;
        _applyAmountEntry.TextChanged += OnApplyAmountChanged;
        _applyAmountEntry.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () => await OpenApplyAmountKeyboardAsync())
        });

        _useFullButton = new Button
        {
            Text = "USE FULL",
            BackgroundColor = Color.FromArgb("#D1FAE5"),
            TextColor = Color.FromArgb("#065F46"),
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 14,
            WidthRequest = 100,
            HeightRequest = 55
        };
        _useFullButton.Clicked += (_, _) => OnUseFullClicked();

        var applyRow = new Grid
        {
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) },
            ColumnSpacing = 10
        };
        applyRow.Add(new Border
        {
            BackgroundColor = Color.FromArgb("#F3F4F6"),
            Stroke = Color.FromArgb("#CBD5E1"),
            StrokeThickness = 1,
            Padding = new Thickness(14, 0),
            HeightRequest = 55,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Content = _applyAmountEntry
        }, 0);
        applyRow.Add(_useFullButton, 1);

        _applyAmountSection = new VerticalStackLayout
        {
            Spacing = 8,
            IsVisible = false,
            Children =
            {
                new Label
                {
                    Text = "AMOUNT TO APPLY",
                    FontSize = 14,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#6B7280")
                },
                applyRow
            }
        };

        _remainingLabel = new Label
        {
            Text = "£0.00",
            FontSize = 20,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#DC2626"),
            VerticalOptions = LayoutOptions.Center
        };
        _remainingFrame = new Border
        {
            BackgroundColor = Color.FromArgb("#FEF2F2"),
            Stroke = Color.FromArgb("#EF4444"),
            StrokeThickness = 2,
            Padding = 12,
            IsVisible = false,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Content = new Grid
            {
                ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) },
                Children =
                {
                    new Label
                    {
                        Text = "REMAINING TO PAY:",
                        FontSize = 16,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#DC2626"),
                        VerticalOptions = LayoutOptions.Center
                    }
                }
            }
        };
        ((Grid)_remainingFrame.Content!).Add(_remainingLabel, 1);

        _applyButton = new Button
        {
            Text = "CHECK GIFT CARD FIRST",
            BackgroundColor = Color.FromArgb("#9CA3AF"),
            TextColor = Colors.White,
            FontSize = 20,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 14,
            HeightRequest = 60,
            IsEnabled = false
        };
        _applyButton.Clicked += (_, _) => OnApplyClicked();

        _dialogContent = new VerticalStackLayout
        {
            Spacing = 18,
            Children =
            {
                header,
                giftBadge,
                new BoxView { HeightRequest = 2, Color = Color.FromArgb("#E5E7EB") },
                amountDueCard,
                numberSection,
                _balanceFrame,
                _applyAmountSection,
                _remainingFrame,
                _applyButton
            }
        };

        _dialogCard = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#E2E8F0"),
            StrokeThickness = 1,
            Padding = 30,
            WidthRequest = PreferredWidth,
            MaximumWidthRequest = PreferredWidth,
            MaximumHeightRequest = PreferredHeight,
            Margin = 16,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            StrokeShape = new RoundRectangle { CornerRadius = 22 },
            Shadow = new Shadow
            {
                Brush = Color.FromArgb("#0F172A"),
                Opacity = 0.16f,
                Radius = 20,
                Offset = new Point(0, 8)
            },
            Content = new ScrollView
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Default,
                Content = _dialogContent
            }
        };

        Content = new Grid
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            Children = { _dialogCard }
        };

        SizeChanged += (_, _) => ApplyResponsiveLayout();
    }

    public void SetAmountDue(decimal amount)
    {
        _amountDue = Math.Max(0m, amount);
        _amountDueLabel.Text = $"£{_amountDue:F2}";
        UpdateApplyButtonState();
    }

    public async Task<PaymentGiftCardResult> ShowAsync(
        Func<string, CancellationToken, Task<PaymentGiftCardLookupResult>> lookupAsync,
        ContentPage? hostPage = null)
    {
        _lookupAsync = lookupAsync ?? throw new ArgumentNullException(nameof(lookupAsync));
        _tcs = new TaskCompletionSource<PaymentGiftCardResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _hostPage = hostPage;
        _parent = PaymentOverlayHost.Attach(this, hostPage);
        if (_parent is null)
        {
            return new PaymentGiftCardResult { Success = false, Message = "Could not open gift card dialog." };
        }

        ApplyResponsiveLayout();
        return await _tcs.Task;
    }

    private void OnGiftCardNumberFocused(object? sender, FocusEventArgs e)
    {
        if (e.IsFocused)
        {
            _giftCardNumberEntry.Unfocus();
            _ = OpenGiftCardNumberKeyboardAsync();
        }
    }

    private async Task OpenGiftCardNumberKeyboardAsync()
    {
        if (_keyboardOpen || _busy)
        {
            return;
        }

        _keyboardOpen = true;
        try
        {
            _giftCardNumberEntry.Unfocus();
            var overlayHost = _parent ?? Content as Grid;
            if (overlayHost is null)
            {
                return;
            }

            var hostPage = _hostPage
                ?? PaymentOverlayHost.FindPage()
                ?? overlayHost.Window?.Page as ContentPage;

            var keyboard = new NumericKeyboardDialog();
            var value = await keyboard.ShowDigitsAsync(
                _giftCardNumberEntry.Text,
                "Gift card number",
                maxDigits: 24,
                hostPage: hostPage,
                overlayHost: overlayHost);
            if (value != null)
            {
                _giftCardNumberEntry.Text = value;
            }
        }
        finally
        {
            _keyboardOpen = false;
        }
    }

    private void OnApplyAmountFocused(object? sender, FocusEventArgs e)
    {
        if (e.IsFocused)
        {
            _applyAmountEntry.Unfocus();
            _ = OpenApplyAmountKeyboardAsync();
        }
    }

    private async Task OpenApplyAmountKeyboardAsync()
    {
        if (_keyboardOpen || _busy || !_applyAmountSection.IsVisible)
        {
            return;
        }

        _keyboardOpen = true;
        try
        {
            _applyAmountEntry.Unfocus();
            var overlayHost = _parent ?? Content as Grid;
            if (overlayHost is null)
            {
                return;
            }

            var hostPage = _hostPage
                ?? PaymentOverlayHost.FindPage()
                ?? overlayHost.Window?.Page as ContentPage;

            decimal? initial = TryParseAmount(_applyAmountEntry.Text, out var parsed) ? parsed : null;
            var keyboard = new NumericKeyboardDialog();
            var value = await keyboard.ShowCurrencyAsync(
                initial,
                "Amount to apply",
                hostPage,
                minimum: 0m,
                maximum: 999999.99m,
                overlayHost: overlayHost);
            if (value.HasValue)
            {
                _isUpdatingApplyAmount = true;
                _applyAmountEntry.Text = value.Value.ToString("F2", CultureInfo.InvariantCulture);
                _isUpdatingApplyAmount = false;
                UpdateRemainingDisplay();
                UpdateApplyButtonState();
            }
        }
        finally
        {
            _keyboardOpen = false;
        }
    }

    private async Task OnCheckBalanceClickedAsync()
    {
        if (_busy || _lookupAsync is null)
        {
            return;
        }

        var cardNumber = _giftCardNumberEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(cardNumber))
        {
            SetStatus("Enter or scan a gift card number.", true);
            return;
        }

        await RunBusyAsync("CHECKING...", async () =>
        {
            SetStatus("Checking gift card…", false);
            var result = await _lookupAsync(cardNumber, CancellationToken.None);
            if (!result.Success)
            {
                ResetCardState();
                SetStatus(result.Message ?? "Gift card not found.", true);
                return;
            }

            _cardNumber = string.IsNullOrWhiteSpace(result.CardNumber) ? cardNumber : result.CardNumber.Trim();
            _cardBalance = Math.Max(0m, result.Balance);
            _cardReady = result.CanUse;
            _balanceFrame.IsVisible = true;
            _balanceLabel.Text = $"£{_cardBalance:F2}";
            _applyAmountSection.IsVisible = result.CanUse;

            if (result.CanUse)
            {
                _isUpdatingApplyAmount = true;
                _applyAmountEntry.Text = Math.Min(_cardBalance, _amountDue).ToString("F2", CultureInfo.InvariantCulture);
                _isUpdatingApplyAmount = false;
                SetStatus(result.Message ?? "Gift card verified.", false);
            }
            else
            {
                SetStatus(result.Message ?? "Gift card cannot be used.", true);
            }

            UpdateRemainingDisplay();
            UpdateApplyButtonState();
        });
    }

    private void OnUseFullClicked()
    {
        var maxApply = Math.Min(_cardBalance, _amountDue);
        _isUpdatingApplyAmount = true;
        _applyAmountEntry.Text = maxApply.ToString("F2", CultureInfo.InvariantCulture);
        _isUpdatingApplyAmount = false;
        UpdateRemainingDisplay();
        UpdateApplyButtonState();
    }

    private void OnApplyClicked()
    {
        if (!_cardReady || string.IsNullOrWhiteSpace(_cardNumber))
        {
            SetStatus("Check the gift card first.", true);
            return;
        }

        if (!TryParseAmount(_applyAmountEntry.Text, out var applyAmount) || applyAmount <= 0)
        {
            SetStatus("Enter a valid amount to apply.", true);
            return;
        }

        if (applyAmount > _cardBalance + 0.009m)
        {
            SetStatus($"Amount exceeds card balance. Max: £{_cardBalance:F2}.", true);
            return;
        }

        if (applyAmount > _amountDue + 0.009m)
        {
            applyAmount = _amountDue;
        }

        applyAmount = Math.Round(applyAmount, 2, MidpointRounding.AwayFromZero);
        Complete(new PaymentGiftCardResult
        {
            Success = true,
            CardNumber = _cardNumber,
            AmountApplied = applyAmount,
            PreviousCardBalance = _cardBalance,
            NewCardBalance = Math.Max(0m, _cardBalance - applyAmount),
            Message = "Gift card ready to apply."
        });
    }

    private void OnApplyAmountChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingApplyAmount)
        {
            return;
        }

        UpdateRemainingDisplay();
        UpdateApplyButtonState();
    }

    private void UpdateRemainingDisplay()
    {
        if (TryParseAmount(_applyAmountEntry.Text, out var applyAmount))
        {
            var remaining = _amountDue - applyAmount;
            if (remaining > 0.009m)
            {
                _remainingFrame.IsVisible = true;
                _remainingLabel.Text = $"£{remaining:F2}";
                return;
            }
        }

        _remainingFrame.IsVisible = false;
    }

    private void UpdateApplyButtonState()
    {
        var hasAmount = TryParseAmount(_applyAmountEntry.Text, out var applyAmount);
        var canApply = _cardReady
            && hasAmount
            && applyAmount > 0
            && applyAmount <= _cardBalance + 0.009m;

        _applyButton.IsEnabled = canApply && !_busy;
        _applyButton.BackgroundColor = canApply
            ? Color.FromArgb("#7C3AED")
            : Color.FromArgb("#9CA3AF");
        _applyButton.Text = canApply
            ? "APPLY GIFT CARD"
            : !_cardReady
                ? "CHECK GIFT CARD FIRST"
                : !hasAmount || applyAmount <= 0
                    ? "ENTER AMOUNT"
                    : "AMOUNT TOO HIGH";
    }

    private void ResetCardState()
    {
        _cardReady = false;
        _cardBalance = 0;
        _cardNumber = null;
        _balanceFrame.IsVisible = false;
        _applyAmountSection.IsVisible = false;
        _remainingFrame.IsVisible = false;
        UpdateApplyButtonState();
    }

    private void SetStatus(string message, bool isError)
    {
        _statusMessageLabel.Text = message;
        _statusMessageLabel.TextColor = isError
            ? Color.FromArgb("#DC2626")
            : Color.FromArgb("#047857");
        _statusMessageLabel.IsVisible = !string.IsNullOrWhiteSpace(message);
    }

    private async Task RunBusyAsync(string busyText, Func<Task> action)
    {
        _busy = true;
        var previousCheck = _checkBalanceButton.Text;
        var previousApply = _applyButton.Text;
        _checkBalanceButton.IsEnabled = false;
        _applyButton.IsEnabled = false;
        _giftCardNumberEntry.IsEnabled = false;
        _applyAmountEntry.IsEnabled = false;
        _checkBalanceButton.Text = busyText;
        _applyButton.Text = busyText;

        try
        {
            await action();
        }
        finally
        {
            _busy = false;
            _checkBalanceButton.Text = previousCheck;
            _applyButton.Text = previousApply;
            _checkBalanceButton.IsEnabled = true;
            _giftCardNumberEntry.IsEnabled = true;
            _applyAmountEntry.IsEnabled = true;
            UpdateApplyButtonState();
        }
    }

    private void ApplyResponsiveLayout()
    {
        var width = Width;
        var height = Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var tablet = width < 1280;
        var shortWindow = height <= 720;
        var margin = tablet ? 12d : 16d;
        var availableWidth = Math.Max(320, width - (margin * 2));
        var cardWidth = Math.Min(PreferredWidth, availableWidth);

        _dialogCard.WidthRequest = cardWidth;
        _dialogCard.MaximumWidthRequest = cardWidth;
        _dialogCard.Margin = new Thickness(margin);
        _dialogCard.Padding = new Thickness(tablet ? 24 : 30);
        _dialogCard.MaximumHeightRequest = Math.Max(
            320,
            Math.Min(PreferredHeight, height - (margin * 2) - 24));

        _dialogContent.Spacing = shortWindow ? 9 : tablet ? 12 : 18;
        _closeButton.WidthRequest = tablet ? 82 : 90;
        _applyButton.HeightRequest = tablet ? 54 : 60;
    }

    private static bool TryParseAmount(string? input, out decimal amount)
    {
        var normalized = (input ?? string.Empty)
            .Replace("£", string.Empty, StringComparison.Ordinal)
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Trim();
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
    }

    private void Complete(PaymentGiftCardResult result)
    {
        PaymentOverlayHost.Detach(this, _parent);
        _parent = null;
        _tcs?.TrySetResult(result);
    }
}
