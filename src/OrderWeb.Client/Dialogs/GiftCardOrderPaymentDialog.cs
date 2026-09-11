using System.Globalization;
using OrderWeb.Client.Services;
using OrderWeb.Contracts.Dtos;

namespace OrderWeb.Client.Dialogs;

public sealed class GiftCardOrderPaymentResult
{
    public bool Success { get; init; }
    public string? CardNumber { get; init; }
    public decimal AmountApplied { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Client gift-card pay dialog: lookup balance via Mother, choose apply amount.
/// Redeem + order payment line happen on Mother when Client posts /api/client/payments.
/// </summary>
public sealed class GiftCardOrderPaymentDialog : ContentPage
{
    private readonly decimal _amountDue;
    private readonly MotherGiftCardClient _giftCards = new();
    private readonly Entry _cardEntry = new() { AutomationId = "GiftCardNumber", Placeholder = "Enter or scan gift card number", FontSize = 18, Keyboard = Keyboard.Numeric, MaxLength = 24 };
    private readonly Entry _applyEntry = new() { AutomationId = "GiftCardAmount", Keyboard = Keyboard.Numeric, FontSize = 18, Placeholder = "0.00", MaxLength = 9 };
    private readonly Label _statusLabel = new() { FontSize = 14, TextColor = Color.FromArgb("#64748B") };
    private readonly Label _balanceLabel = new() { FontSize = 16, FontAttributes = FontAttributes.Bold, Text = "Balance: —" };
    private readonly Button _checkButton = new() { Text = "Check balance", BackgroundColor = Color.FromArgb("#7C3AED"), TextColor = Colors.White, HeightRequest = 48 };
    private readonly Button _applyButton = new() { Text = "Apply to order", BackgroundColor = Color.FromArgb("#16A34A"), TextColor = Colors.White, HeightRequest = 52, IsEnabled = false };
    private readonly Button _useFullButton = new() { Text = "Use max", BackgroundColor = Color.FromArgb("#F1F5F9"), TextColor = Color.FromArgb("#334155"), HeightRequest = 44 };
    private decimal _cardBalance;
    private string? _cardNumber;
    private TaskCompletionSource<GiftCardOrderPaymentResult>? _tcs;

    public GiftCardOrderPaymentDialog(decimal amountDue)
    {
        _amountDue = Math.Max(0, amountDue);
        Title = "Gift Card Payment";
        BackgroundColor = Color.FromArgb("#F8FAFC");

        _checkButton.Clicked += async (_, _) => await CheckBalanceAsync();
        _useFullButton.Clicked += (_, _) => UseMax();
        _applyButton.Clicked += OnApplyClicked;
        _applyEntry.TextChanged += (_, _) => UpdateApplyEnabled();

        var cancelButton = new Button
        {
            Text = "Cancel",
            BackgroundColor = Colors.Transparent,
            TextColor = Color.FromArgb("#DC2626")
        };
        cancelButton.Clicked += async (_, _) => await CompleteAsync(new GiftCardOrderPaymentResult { Success = false, Message = "Cancelled." });

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 24,
                Spacing = 16,
                Children =
                {
                    new Label
                    {
                        Text = "GIFT CARD PAYMENT",
                        FontSize = 24,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#111827")
                    },
                    new Border
                    {
                        BackgroundColor = Color.FromArgb("#FAF5FF"),
                        Stroke = Color.FromArgb("#A855F7"),
                        StrokeThickness = 2,
                        Padding = 16,
                        Content = new Grid
                        {
                            ColumnDefinitions = new ColumnDefinitionCollection
                            {
                                new(GridLength.Star),
                                new(GridLength.Auto)
                            },
                            Children =
                            {
                                new Label { Text = "AMOUNT DUE", FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#6B21A8"), VerticalOptions = LayoutOptions.Center },
                                CreateAmountLabel()
                            }
                        }
                    },
                    new Label { Text = "Gift card number", FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#6B7280") },
                    _cardEntry,
                    _checkButton,
                    _balanceLabel,
                    new Label { Text = "Amount to apply", FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#6B7280") },
                    _applyEntry,
                    _useFullButton,
                    _statusLabel,
                    _applyButton,
                    cancelButton
                }
            }
        };
    }

    private Label CreateAmountLabel()
    {
        var label = new Label
        {
            Text = FormatMoney(_amountDue),
            FontSize = 26,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#7C3AED"),
            HorizontalOptions = LayoutOptions.End
        };
        Grid.SetColumn(label, 1);
        return label;
    }

    public Task<GiftCardOrderPaymentResult> WaitAsync()
    {
        _tcs ??= new TaskCompletionSource<GiftCardOrderPaymentResult>();
        return _tcs.Task;
    }

    private async Task CheckBalanceAsync()
    {
        var cardNumber = _cardEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(cardNumber))
        {
            SetStatus("Enter or scan a gift card number.", true);
            return;
        }

        _checkButton.IsEnabled = false;
        _checkButton.Text = "Checking...";
        try
        {
            var lookup = await _giftCards.LookupAsync(cardNumber, GiftCardLookupPurposes.Redeem);
            if (!lookup.Success || lookup.GiftCard == null || !lookup.CanProceed)
            {
                _cardNumber = null;
                _applyButton.IsEnabled = false;
                _balanceLabel.Text = "Balance: —";
                SetStatus(lookup.Error ?? lookup.Message ?? "Gift card lookup failed.", true);
                return;
            }

            _cardNumber = string.IsNullOrWhiteSpace(lookup.GiftCard.CardNumber) ? cardNumber : lookup.GiftCard.CardNumber;
            _cardBalance = lookup.GiftCard.Balance;
            _balanceLabel.Text = $"Balance: {FormatMoney(_cardBalance)} ({lookup.GiftCard.CardNumberMasked})";
            var max = Math.Min(_cardBalance, _amountDue);
            _applyEntry.Text = max.ToString("F2", CultureInfo.InvariantCulture);
            SetStatus(lookup.Message ?? "Card ready.", false);
            UpdateApplyEnabled();
        }
        finally
        {
            _checkButton.IsEnabled = true;
            _checkButton.Text = "Check balance";
        }
    }

    private void UseMax()
    {
        if (_cardBalance <= 0)
        {
            SetStatus("Check the gift card first.", true);
            return;
        }

        _applyEntry.Text = Math.Min(_cardBalance, _amountDue).ToString("F2", CultureInfo.InvariantCulture);
        UpdateApplyEnabled();
    }

    private async void OnApplyClicked(object? sender, EventArgs e)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _applyButton.IsEnabled = false;
        try
        {
            await ApplyAsync();
        }
        finally
        {
            _busy = false;
            UpdateApplyEnabled();
        }
    }

    private bool _busy;

    private async Task ApplyAsync()
    {
        if (string.IsNullOrWhiteSpace(_cardNumber))
        {
            SetStatus("Check the gift card first.", true);
            return;
        }

        if (!TryParseAmount(_applyEntry.Text, out var amount) || amount <= 0)
        {
            SetStatus("Enter a valid amount to apply.", true);
            return;
        }

        if (amount > _cardBalance + 0.009m)
        {
            SetStatus($"Amount exceeds card balance ({FormatMoney(_cardBalance)}).", true);
            return;
        }

        if (amount > _amountDue + 0.009m)
        {
            SetStatus($"Amount exceeds order due ({FormatMoney(_amountDue)}).", true);
            return;
        }

        await CompleteAsync(new GiftCardOrderPaymentResult
        {
            Success = true,
            CardNumber = _cardNumber,
            AmountApplied = amount,
            Message = $"Apply {FormatMoney(amount)} from gift card."
        });
    }

    private void UpdateApplyEnabled()
    {
        _applyButton.IsEnabled = !string.IsNullOrWhiteSpace(_cardNumber)
            && TryParseAmount(_applyEntry.Text, out var amount)
            && amount > 0
            && amount <= _cardBalance + 0.009m
            && amount <= _amountDue + 0.009m;
    }

    private void SetStatus(string message, bool isError)
    {
        _statusLabel.Text = message;
        _statusLabel.TextColor = Color.FromArgb(isError ? "#DC2626" : "#64748B");
    }

    private async Task CompleteAsync(GiftCardOrderPaymentResult result)
    {
        _tcs?.TrySetResult(result);
        if (Navigation.NavigationStack.Contains(this) || Navigation.ModalStack.Contains(this))
        {
            await Navigation.PopModalAsync(false);
        }
    }

    protected override bool OnBackButtonPressed()
    {
        _ = CompleteAsync(new GiftCardOrderPaymentResult { Success = false, Message = "Cancelled." });
        return true;
    }

    private static bool TryParseAmount(string? text, out decimal amount) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out amount)
        || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);

    private static string FormatMoney(decimal amount) =>
        amount.ToString("C", CultureInfo.GetCultureInfo("en-GB"));
}
