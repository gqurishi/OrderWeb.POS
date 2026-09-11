using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using OrderWeb.SharedUI.Controls;

namespace OrderWeb.SharedUI.Payments;

public sealed class PaymentCashResult
{
    public bool Success { get; init; }
    public decimal AmountPaid { get; init; }
    public decimal AmountReceived { get; init; }
    public decimal Change { get; init; }
}

/// <summary>
/// Mother-chrome CASH PAYMENT dialog (Exact / quick amounts / change / confirm).
/// SharedUI — Client (and later Mother) hosts own post after confirm.
/// </summary>
public sealed class PaymentCashDialog : ContentView
{
    private const double PreferredWidth = 650;
    private const double PreferredHeight = 760;

    private readonly Border _dialogCard;
    private readonly VerticalStackLayout _dialogContent;
    private readonly Label _titleLabel;
    private readonly Button _closeButton;
    private readonly Border _amountDueCard;
    private readonly Label _amountDueLabel;
    private readonly Button _exactButton;
    private readonly Button _quick1Button;
    private readonly Button _quick2Button;
    private readonly Button _quick3Button;
    private readonly Grid _quickCashGrid;
    private readonly Entry _amountReceivedEntry;
    private readonly Border _changeFrame;
    private readonly Label _changeTitleLabel;
    private readonly Label _changeLabel;
    private readonly Border _remainingFrame;
    private readonly Label _remainingLabel;
    private readonly Button _confirmButton;

    private TaskCompletionSource<PaymentCashResult>? _tcs;
    private Grid? _parent;
    private ContentPage? _hostPage;
    private decimal _amountDue;
    private decimal _amountReceived;
    private bool _isUpdatingEntry;
    private bool _keyboardOpen;

    public PaymentCashDialog()
    {
        BackgroundColor = Color.FromArgb("#80000000");
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;

        _titleLabel = new Label
        {
            Text = "CASH PAYMENT",
            FontSize = 30,
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
        _closeButton.Clicked += (_, _) => Complete(new PaymentCashResult { Success = false });

        var header = new Grid
        {
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) },
            Children = { _titleLabel }
        };
        header.Add(_closeButton, 1);

        _amountDueLabel = new Label
        {
            Text = "£0.00",
            FontSize = 32,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#B45309"),
            VerticalOptions = LayoutOptions.Center
        };

        _amountDueCard = new Border
        {
            BackgroundColor = Color.FromArgb("#FFFBEB"),
            Stroke = Color.FromArgb("#F59E0B"),
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
                        FontSize = 22,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#92400E"),
                        VerticalOptions = LayoutOptions.Center
                    }
                }
            }
        };
        ((Grid)_amountDueCard.Content!).Add(_amountDueLabel, 1);

        _exactButton = MakeQuickButton("Exact £0.00", Color.FromArgb("#D1FAE5"), Color.FromArgb("#065F46"), 14);
        _exactButton.Clicked += (_, _) => SetAmount(_amountDue);

        _quick1Button = MakeQuickButton("£0.00", Color.FromArgb("#DBEAFE"), Color.FromArgb("#1E40AF"), 18);
        _quick2Button = MakeQuickButton("£0.00", Color.FromArgb("#DBEAFE"), Color.FromArgb("#1E40AF"), 18);
        _quick3Button = MakeQuickButton("£0.00", Color.FromArgb("#DBEAFE"), Color.FromArgb("#1E40AF"), 18);
        _quick1Button.Clicked += OnQuickAmountClicked;
        _quick2Button.Clicked += OnQuickAmountClicked;
        _quick3Button.Clicked += OnQuickAmountClicked;

        _quickCashGrid = new Grid
        {
            ColumnDefinitions =
            {
                new(GridLength.Star),
                new(GridLength.Star),
                new(GridLength.Star),
                new(GridLength.Star)
            },
            ColumnSpacing = 12
        };
        _quickCashGrid.Add(_exactButton, 0);
        _quickCashGrid.Add(_quick1Button, 1);
        _quickCashGrid.Add(_quick2Button, 2);
        _quickCashGrid.Add(_quick3Button, 3);

        _amountReceivedEntry = new Entry
        {
            Placeholder = "Enter amount received",
            FontSize = 18,
            BackgroundColor = Colors.Transparent,
            IsReadOnly = true,
            VerticalOptions = LayoutOptions.Center
        };
        _amountReceivedEntry.Focused += OnAmountEntryFocused;
        _amountReceivedEntry.TextChanged += OnAmountChanged;
        _amountReceivedEntry.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () => await OpenAmountKeyboardAsync())
        });

        var amountReceivedBorder = new Border
        {
            BackgroundColor = Color.FromArgb("#F3F4F6"),
            Stroke = Color.FromArgb("#CBD5E1"),
            StrokeThickness = 1,
            Padding = new Thickness(14, 0),
            HeightRequest = 55,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Content = _amountReceivedEntry
        };

        _changeTitleLabel = new Label
        {
            Text = "CHANGE:",
            FontSize = 22,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#64748B"),
            VerticalOptions = LayoutOptions.Center
        };
        _changeLabel = new Label
        {
            Text = "£0.00",
            FontSize = 30,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#334155"),
            VerticalOptions = LayoutOptions.Center
        };

        _changeFrame = new Border
        {
            BackgroundColor = Color.FromArgb("#F8FAFC"),
            Stroke = Color.FromArgb("#CBD5E1"),
            StrokeThickness = 2,
            Padding = 16,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Content = new Grid
            {
                ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) },
                Children = { _changeTitleLabel }
            }
        };
        ((Grid)_changeFrame.Content!).Add(_changeLabel, 1);

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
                        Text = "REMAINING:",
                        FontSize = 16,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#DC2626"),
                        VerticalOptions = LayoutOptions.Center
                    }
                }
            }
        };
        ((Grid)_remainingFrame.Content!).Add(_remainingLabel, 1);

        _confirmButton = new Button
        {
            Text = "CONFIRM CASH PAYMENT",
            BackgroundColor = Color.FromArgb("#9CA3AF"),
            TextColor = Colors.White,
            FontSize = 20,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 14,
            HeightRequest = 60,
            IsEnabled = false
        };
        _confirmButton.Clicked += OnConfirmClicked;

        _dialogContent = new VerticalStackLayout
        {
            Spacing = 18,
            Children =
            {
                header,
                new BoxView { HeightRequest = 2, Color = Color.FromArgb("#E5E7EB") },
                _amountDueCard,
                _quickCashGrid,
                amountReceivedBorder,
                _changeFrame,
                _remainingFrame
            }
        };

        var body = new Grid
        {
            RowDefinitions = { new(GridLength.Star), new(GridLength.Auto) },
            RowSpacing = 12
        };
        body.Add(new ScrollView
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Default,
            Content = _dialogContent
        }, 0, 0);
        body.Add(_confirmButton, 0, 1);

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
            Content = body
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
        _amountReceived = 0;
        _isUpdatingEntry = true;
        _amountReceivedEntry.Text = string.Empty;
        _isUpdatingEntry = false;
        SetQuickCashButtons();
        UpdateDisplay();
    }

    public async Task<PaymentCashResult> ShowAsync(ContentPage? hostPage = null)
    {
        _tcs = new TaskCompletionSource<PaymentCashResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _hostPage = hostPage;
        _parent = PaymentOverlayHost.Attach(this, hostPage);
        if (_parent is null)
        {
            return new PaymentCashResult { Success = false };
        }

        ApplyResponsiveLayout();
        return await _tcs.Task;
    }

    private void SetAmount(decimal amount)
    {
        _amountReceived = amount;
        _isUpdatingEntry = true;
        _amountReceivedEntry.Text = amount.ToString("F2", CultureInfo.InvariantCulture);
        _isUpdatingEntry = false;
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        if (_amountReceived >= _amountDue && _amountDue > 0)
        {
            var change = _amountReceived - _amountDue;
            _changeLabel.Text = $"£{change:F2}";
            _changeTitleLabel.Text = change == 0 ? "READY:" : "CHANGE:";
            _changeFrame.BackgroundColor = Color.FromArgb("#ECFDF5");
            _changeFrame.Stroke = Color.FromArgb("#10B981");
            _changeTitleLabel.TextColor = Color.FromArgb("#047857");
            _changeLabel.TextColor = Color.FromArgb("#047857");
            _remainingFrame.IsVisible = false;
            _confirmButton.IsEnabled = true;
            _confirmButton.BackgroundColor = Color.FromArgb("#059669");
            _confirmButton.Text = "CONFIRM CASH PAYMENT";
        }
        else if (_amountReceived > 0)
        {
            var shortAmount = _amountDue - _amountReceived;
            _changeTitleLabel.Text = "SHORT:";
            _changeLabel.Text = $"£{shortAmount:F2}";
            _changeFrame.BackgroundColor = Color.FromArgb("#FEF2F2");
            _changeFrame.Stroke = Color.FromArgb("#EF4444");
            _changeTitleLabel.TextColor = Color.FromArgb("#DC2626");
            _changeLabel.TextColor = Color.FromArgb("#DC2626");
            _remainingFrame.IsVisible = false;
            _confirmButton.IsEnabled = false;
            _confirmButton.BackgroundColor = Color.FromArgb("#9CA3AF");
            _confirmButton.Text = "ENTER ENOUGH CASH";
        }
        else
        {
            _changeTitleLabel.Text = "CHANGE:";
            _changeLabel.Text = "£0.00";
            _changeFrame.BackgroundColor = Color.FromArgb("#F8FAFC");
            _changeFrame.Stroke = Color.FromArgb("#CBD5E1");
            _changeTitleLabel.TextColor = Color.FromArgb("#64748B");
            _changeLabel.TextColor = Color.FromArgb("#334155");
            _remainingFrame.IsVisible = false;
            _confirmButton.IsEnabled = false;
            _confirmButton.BackgroundColor = Color.FromArgb("#9CA3AF");
            _confirmButton.Text = "CONFIRM CASH PAYMENT";
        }
    }

    private void OnAmountEntryFocused(object? sender, FocusEventArgs e)
    {
        if (e.IsFocused)
        {
            _amountReceivedEntry.Unfocus();
            _ = OpenAmountKeyboardAsync();
        }
    }

    private async Task OpenAmountKeyboardAsync()
    {
        if (_keyboardOpen)
        {
            return;
        }

        _keyboardOpen = true;
        try
        {
            _amountReceivedEntry.Unfocus();
            var overlayHost = _parent ?? Content as Grid;
            if (overlayHost is null)
            {
                return;
            }

            var hostPage = _hostPage
                ?? PaymentOverlayHost.FindPage()
                ?? overlayHost.Window?.Page as ContentPage;

            var keyboard = new NumericKeyboardDialog();
            var value = await keyboard.ShowCurrencyAsync(
                _amountReceived > 0 ? _amountReceived : null,
                "Amount received",
                hostPage,
                minimum: 0m,
                maximum: 999999.99m,
                overlayHost: overlayHost);
            if (value.HasValue)
            {
                SetAmount(value.Value);
            }
        }
        finally
        {
            _keyboardOpen = false;
        }
    }

    private void OnAmountChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingEntry)
        {
            return;
        }

        _amountReceived = TryParseCashAmount(e.NewTextValue, out var amount) ? amount : 0m;
        UpdateDisplay();
    }

    private void OnQuickAmountClicked(object? sender, EventArgs e)
    {
        if (sender is Button button && button.CommandParameter is decimal amount)
        {
            SetAmount(amount);
        }
    }

    private void OnConfirmClicked(object? sender, EventArgs e)
    {
        if (_amountReceived < _amountDue || _amountDue <= 0)
        {
            return;
        }

        Complete(new PaymentCashResult
        {
            Success = true,
            AmountPaid = _amountDue,
            AmountReceived = _amountReceived,
            Change = Math.Max(0m, _amountReceived - _amountDue)
        });
    }

    private void SetQuickCashButtons()
    {
        _exactButton.Text = $"Exact £{_amountDue:F2}";
        var quickAmounts = BuildQuickCashAmounts(_amountDue);
        SetQuickButton(_quick1Button, quickAmounts[0]);
        SetQuickButton(_quick2Button, quickAmounts[1]);
        SetQuickButton(_quick3Button, quickAmounts[2]);
    }

    private static void SetQuickButton(Button button, decimal amount)
    {
        button.Text = $"£{amount:0.##}";
        button.CommandParameter = amount;
    }

    private static decimal[] BuildQuickCashAmounts(decimal amountDue)
    {
        var roundedToTen = Math.Ceiling(amountDue / 10m) * 10m;
        if (roundedToTen <= amountDue)
        {
            roundedToTen += 10m;
        }

        return new[]
        {
            roundedToTen,
            roundedToTen + 10m,
            roundedToTen + 20m
        };
    }

    private static bool TryParseCashAmount(string? input, out decimal amount)
    {
        var normalized = (input ?? string.Empty)
            .Replace("£", string.Empty, StringComparison.Ordinal)
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Trim();
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out amount) && amount > 0;
    }

    private static Button MakeQuickButton(string text, Color background, Color foreground, double fontSize) =>
        new()
        {
            Text = text,
            BackgroundColor = background,
            TextColor = foreground,
            FontSize = fontSize,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 14,
            HeightRequest = 50
        };

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
        _titleLabel.FontSize = tablet ? 25 : 30;
        _closeButton.WidthRequest = tablet ? 82 : 90;
        _amountDueCard.Padding = shortWindow ? 12 : tablet ? 15 : 18;
        _amountDueLabel.FontSize = shortWindow ? 27 : tablet ? 30 : 32;
        _quickCashGrid.ColumnSpacing = tablet ? 8 : 12;
        _confirmButton.HeightRequest = tablet ? 54 : 60;
    }

    private void Complete(PaymentCashResult result)
    {
        PaymentOverlayHost.Detach(this, _parent);
        _parent = null;
        _tcs?.TrySetResult(result);
    }
}
