using System.Globalization;
using OrderWeb.Client.Services;
using OrderWeb.Contracts.Dtos;
using OrderWeb.Contracts.Features;

namespace OrderWeb.Client.Dialogs;

public sealed class LoyaltyOrderPaymentResult
{
    public bool Success { get; init; }
    public string? Lookup { get; init; }
    public int Points { get; init; }
    public decimal AmountApplied { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Client loyalty pay dialog: lookup customer via Mother, choose points to redeem.
/// Cloud redeem + order payment line happen on Mother when Client posts /api/client/payments.
/// </summary>
public sealed class LoyaltyOrderPaymentDialog : ContentPage
{
    private const decimal PointsPerPound = 100m;

    private readonly decimal _amountDue;
    private readonly MotherLoyaltyClient _loyalty = new();
    private readonly Entry _lookupEntry = new() { AutomationId = "LoyaltyLookup", Placeholder = "Phone or loyalty card", FontSize = 18, Keyboard = Keyboard.Telephone, MaxLength = 24 };
    private readonly Entry _pointsEntry = new() { AutomationId = "LoyaltyPoints", Keyboard = Keyboard.Numeric, FontSize = 18, Placeholder = "0", MaxLength = 9 };
    private readonly Label _statusLabel = new() { FontSize = 14, TextColor = Color.FromArgb("#64748B") };
    private readonly Label _customerLabel = new() { FontSize = 16, FontAttributes = FontAttributes.Bold, Text = "Customer: —" };
    private readonly Label _balanceLabel = new() { FontSize = 16, FontAttributes = FontAttributes.Bold, Text = "Points: —" };
    private readonly Button _checkButton = new() { Text = "Lookup customer", BackgroundColor = Color.FromArgb("#2563EB"), TextColor = Colors.White, HeightRequest = 48 };
    private readonly Button _applyButton = new() { Text = "Apply to order", BackgroundColor = Color.FromArgb("#16A34A"), TextColor = Colors.White, HeightRequest = 52, IsEnabled = false };
    private readonly Button _useFullButton = new() { Text = "Use max", BackgroundColor = Color.FromArgb("#F1F5F9"), TextColor = Color.FromArgb("#334155"), HeightRequest = 44 };
    private int _pointsBalance;
    private string? _lookup;
    private string? _customerName;
    private bool _busy;
    private TaskCompletionSource<LoyaltyOrderPaymentResult>? _tcs;

    public LoyaltyOrderPaymentDialog(decimal amountDue)
    {
        _amountDue = Math.Max(0, amountDue);
        Title = "Loyalty Points Payment";
        BackgroundColor = Color.FromArgb("#F8FAFC");

        _checkButton.Clicked += async (_, _) => await LookupAsync();
        _useFullButton.Clicked += (_, _) => UseMax();
        _applyButton.Clicked += OnApplyClicked;
        _pointsEntry.TextChanged += (_, _) => UpdateApplyEnabled();
        BindPhoneNumberPad(_lookupEntry);

        var cancelButton = new Button
        {
            Text = "Cancel",
            BackgroundColor = Colors.Transparent,
            TextColor = Color.FromArgb("#DC2626")
        };
        cancelButton.Clicked += async (_, _) => await CompleteAsync(new LoyaltyOrderPaymentResult { Success = false, Message = "Cancelled." });

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
                        Text = "LOYALTY POINTS PAYMENT",
                        FontSize = 24,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#111827")
                    },
                    new Border
                    {
                        BackgroundColor = Color.FromArgb("#EFF6FF"),
                        Stroke = Color.FromArgb("#3B82F6"),
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
                                new Label { Text = "AMOUNT DUE", FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#1D4ED8"), VerticalOptions = LayoutOptions.Center },
                                CreateAmountLabel()
                            }
                        }
                    },
                    new Label
                    {
                        Text = "100 points = £1.00. Points redeem through Mother → OrderWeb cloud. No local points ledger.",
                        FontSize = 13,
                        TextColor = Color.FromArgb("#64748B")
                    },
                    new Label { Text = "Phone or loyalty card", FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#6B7280") },
                    _lookupEntry,
                    _checkButton,
                    _customerLabel,
                    _balanceLabel,
                    new Label { Text = "Points to redeem", FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#6B7280") },
                    _pointsEntry,
                    _useFullButton,
                    _statusLabel,
                    _applyButton,
                    cancelButton
                }
            }
        };
    }

    private bool _phonePadOpen;

    private void BindPhoneNumberPad(Entry entry)
    {
        entry.IsReadOnly = true;
        entry.Focused += async (_, args) =>
        {
            if (!args.IsFocused || _phonePadOpen)
            {
                return;
            }

            entry.Unfocus();
            await OpenPhoneNumberPadAsync(entry);
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            if (!_phonePadOpen)
            {
                await OpenPhoneNumberPadAsync(entry);
            }
        };
        entry.GestureRecognizers.Add(tap);
    }

    private async Task OpenPhoneNumberPadAsync(Entry entry)
    {
        if (_phonePadOpen)
        {
            return;
        }

        _phonePadOpen = true;
        try
        {
            entry.Unfocus();
            var keyboard = new OrderWeb.SharedUI.Controls.NumericKeyboardDialog();
            var value = await keyboard.ShowDigitsAsync(
                entry.Text,
                title: "Enter phone number",
                maxDigits: 16,
                hostPage: this);
            if (value is not null)
            {
                entry.Text = value.Trim();
            }
        }
        finally
        {
            _phonePadOpen = false;
        }
    }

    private Label CreateAmountLabel()
    {
        var label = new Label
        {
            Text = FormatMoney(_amountDue),
            FontSize = 26,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#2563EB"),
            HorizontalOptions = LayoutOptions.End
        };
        Grid.SetColumn(label, 1);
        return label;
    }

    public Task<LoyaltyOrderPaymentResult> WaitAsync()
    {
        _tcs ??= new TaskCompletionSource<LoyaltyOrderPaymentResult>();
        return _tcs.Task;
    }

    private async Task LookupAsync()
    {
        if (_busy)
        {
            return;
        }

        if (!ClientHostAccess.Features.Contains(PosFeatureKeys.CustomerPoints) &&
            !ClientHostAccess.CanOpenMenu("Loyalty Points") &&
            !ClientHostAccess.CanOpenMenu("Loyalty"))
        {
            SetStatus("This terminal is not allowed to use Loyalty. Ask Mother to grant access.", true);
            return;
        }

        var lookup = _lookupEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(lookup))
        {
            SetStatus("Enter a phone number or loyalty card.", true);
            return;
        }

        _busy = true;
        _checkButton.IsEnabled = false;
        _checkButton.Text = "Looking up...";
        try
        {
            var result = await _loyalty.SearchAsync(lookup);
            if (!result.Success || result.Customer == null)
            {
                _lookup = null;
                _pointsBalance = 0;
                _applyButton.IsEnabled = false;
                _customerLabel.Text = "Customer: —";
                _balanceLabel.Text = "Points: —";
                ClientLoyaltyDiagnostics.Record("order-lookup", false, result.Error ?? result.Message, result.ErrorCode,
                    string.Equals(result.ErrorCode, LoyaltyErrorCodes.Queued, StringComparison.OrdinalIgnoreCase));
                SetStatus(FormatError(result), true);
                return;
            }

            ClientLoyaltyDiagnostics.Record("order-lookup", true, result.Message ?? "Customer ready.");
            _lookup = string.IsNullOrWhiteSpace(result.Customer.Phone) ? lookup : result.Customer.Phone;
            _customerName = result.Customer.Name;
            _pointsBalance = result.Customer.PointsBalance;
            var card = string.IsNullOrWhiteSpace(result.Customer.LoyaltyCardNumber)
                ? string.Empty
                : $" · Card {result.Customer.LoyaltyCardNumber}";
            _customerLabel.Text = $"Customer: {result.Customer.Name}{card}";
            _balanceLabel.Text = $"Points: {_pointsBalance:N0} (worth {FormatMoney(_pointsBalance / PointsPerPound)})";

            var maxPoints = MaxRedeemablePoints();
            _pointsEntry.Text = maxPoints.ToString(CultureInfo.InvariantCulture);
            SetStatus(result.Message ?? "Customer ready.", false);
            UpdateApplyEnabled();
        }
        finally
        {
            _checkButton.IsEnabled = true;
            _checkButton.Text = "Lookup customer";
            _busy = false;
        }
    }

    private void UseMax()
    {
        if (_pointsBalance <= 0 || string.IsNullOrWhiteSpace(_lookup))
        {
            SetStatus("Lookup a loyalty customer first.", true);
            return;
        }

        _pointsEntry.Text = MaxRedeemablePoints().ToString(CultureInfo.InvariantCulture);
        UpdateApplyEnabled();
    }

    private int MaxRedeemablePoints()
    {
        var duePoints = (int)Math.Floor(_amountDue * PointsPerPound);
        return Math.Max(0, Math.Min(_pointsBalance, duePoints));
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

    private async Task ApplyAsync()
    {
        if (string.IsNullOrWhiteSpace(_lookup))
        {
            SetStatus("Lookup a loyalty customer first.", true);
            return;
        }

        if ((!int.TryParse(_pointsEntry.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var points) &&
             !int.TryParse(_pointsEntry.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out points)) ||
            points <= 0)
        {
            SetStatus("Enter a valid positive points amount.", true);
            return;
        }

        if (points > _pointsBalance)
        {
            SetStatus($"Insufficient points. Customer has {_pointsBalance:N0}.", true);
            return;
        }

        var amount = Math.Round(points / PointsPerPound, 2, MidpointRounding.AwayFromZero);
        if (amount > _amountDue + 0.009m)
        {
            SetStatus($"Points exceed order due ({FormatMoney(_amountDue)}).", true);
            return;
        }

        if (amount <= 0)
        {
            SetStatus("Redeem at least 1 point (100 pts = £1.00).", true);
            return;
        }

        await CompleteAsync(new LoyaltyOrderPaymentResult
        {
            Success = true,
            Lookup = _lookup,
            Points = points,
            AmountApplied = amount,
            Message = $"Apply {points:N0} pts ({FormatMoney(amount)}) for {_customerName ?? _lookup}."
        });
    }

    private void UpdateApplyEnabled()
    {
        var ok = !string.IsNullOrWhiteSpace(_lookup)
            && (int.TryParse(_pointsEntry.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var points)
                || int.TryParse(_pointsEntry.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out points))
            && points > 0
            && points <= _pointsBalance
            && Math.Round(points / PointsPerPound, 2, MidpointRounding.AwayFromZero) <= _amountDue + 0.009m;
        _applyButton.IsEnabled = ok;
    }

    private void SetStatus(string message, bool isError)
    {
        _statusLabel.Text = message;
        _statusLabel.TextColor = Color.FromArgb(isError ? "#DC2626" : "#64748B");
    }

    private static string FormatError(ClientLoyaltyLookupResponseDto result)
    {
        var text = result.Error ?? result.Message ?? "Loyalty lookup failed.";
        if (string.Equals(result.ErrorCode, LoyaltyErrorCodes.OfflineMother, StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(text)
                ? "Loyalty operations require Mother POS. No points were changed."
                : text;
        }

        if (string.Equals(result.ErrorCode, LoyaltyErrorCodes.Queued, StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        return text;
    }

    private async Task CompleteAsync(LoyaltyOrderPaymentResult result)
    {
        _tcs?.TrySetResult(result);
        if (Navigation.NavigationStack.Contains(this) || Navigation.ModalStack.Contains(this))
        {
            await Navigation.PopModalAsync(false);
        }
    }

    protected override bool OnBackButtonPressed()
    {
        _ = CompleteAsync(new LoyaltyOrderPaymentResult { Success = false, Message = "Cancelled." });
        return true;
    }

    private static string FormatMoney(decimal amount) =>
        amount.ToString("C", CultureInfo.GetCultureInfo("en-GB"));
}
