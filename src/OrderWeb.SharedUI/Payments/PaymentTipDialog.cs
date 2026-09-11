using Microsoft.Maui.Controls.Shapes;
using OrderWeb.SharedUI.Controls;

namespace OrderWeb.SharedUI.Payments;

/// <summary>
/// ADD TIP dialog — Mother chrome as SharedUI source of truth (Mother + Client).
/// Sized and styled to match Mother TipSelectionDialog.
/// </summary>
public sealed class PaymentTipDialog : ContentView
{
    private const double PreferredWidth = 560;
    private const double PreferredHeight = 720;

    private readonly Border _dialogCard;
    private readonly VerticalStackLayout _dialogContent;
    private readonly Label _dialogTitle;
    private readonly Button _closeButton;
    private readonly Border _orderTotalCard;
    private readonly Label _orderTotalLabel;
    private readonly Grid _tipOptionsGrid;
    private readonly Label _tipAmountLabel;
    private readonly Entry _customTipEntry;
    private readonly Button _continueButton;
    private TaskCompletionSource<decimal>? _tcs;
    private Grid? _parent;
    private ContentPage? _hostPage;
    private decimal _selectedTip;
    private bool _keyboardOpen;

    public PaymentTipDialog()
    {
        BackgroundColor = Color.FromArgb("#80000000");
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;

        _orderTotalLabel = new Label
        {
            Text = "£0.00",
            FontSize = 36,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            HorizontalOptions = LayoutOptions.Center,
            TextColor = Color.FromArgb("#047857")
        };

        _tipAmountLabel = new Label
        {
            Text = "£0.00",
            FontSize = 28,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#B45309"),
            VerticalOptions = LayoutOptions.Center
        };

        _customTipEntry = new Entry
        {
            Placeholder = "Custom tip amount",
            Keyboard = Keyboard.Numeric,
            FontSize = 16,
            HeightRequest = 55,
            BackgroundColor = Color.FromArgb("#F3F4F6"),
            IsReadOnly = true,
            // Taps are handled by the host grid (WinUI IsReadOnly Entry often skips Focused).
            InputTransparent = true
        };
        SharedTouchKeyboard.SetEnabled(_customTipEntry, false);

        var customFieldHost = new Grid { HeightRequest = 55 };
        customFieldHost.Children.Add(_customTipEntry);
        var openKeyboardTap = new TapGestureRecognizer { NumberOfTapsRequired = 1 };
        openKeyboardTap.Tapped += async (_, _) => await OpenCustomTipKeyboardAsync();
        customFieldHost.GestureRecognizers.Add(openKeyboardTap);

        _tipOptionsGrid = new Grid
        {
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) },
            RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto) },
            ColumnSpacing = 15,
            RowSpacing = 15
        };
        // NO TIP stays muted gray; amount presets use Mother blue + larger type.
        _tipOptionsGrid.Add(MakeTipButton("NO TIP", Color.FromArgb("#F3F4F6"), Color.FromArgb("#374151"), 20, () => SetTip(0)), 0, 0);
        _tipOptionsGrid.Add(MakeTipButton("£5", Color.FromArgb("#DBEAFE"), Color.FromArgb("#1E40AF"), 24, () => SetTip(5)), 1, 0);
        _tipOptionsGrid.Add(MakeTipButton("£10", Color.FromArgb("#DBEAFE"), Color.FromArgb("#1E40AF"), 24, () => SetTip(10)), 0, 1);
        _tipOptionsGrid.Add(MakeTipButton("£20", Color.FromArgb("#DBEAFE"), Color.FromArgb("#1E40AF"), 24, () => SetTip(20)), 1, 1);

        var enter = new Button
        {
            Text = "ENTER",
            BackgroundColor = Color.FromArgb("#7C3AED"),
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            FontSize = 14,
            CornerRadius = 14,
            WidthRequest = 90,
            HeightRequest = 55
        };
        enter.Clicked += async (_, _) => await OpenCustomTipKeyboardAsync();

        var customRow = new Grid
        {
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) },
            ColumnSpacing = 15
        };
        customRow.Add(customFieldHost, 0);
        customRow.Add(enter, 1);

        _orderTotalCard = new Border
        {
            BackgroundColor = Color.FromArgb("#F0FDF4"),
            Stroke = Color.FromArgb("#A7F3D0"),
            StrokeThickness = 1,
            Padding = 20,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Content = new VerticalStackLayout
            {
                Spacing = 5,
                HorizontalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label
                    {
                        Text = "ORDER TOTAL",
                        FontSize = 16,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#047857"),
                        HorizontalTextAlignment = TextAlignment.Center,
                        HorizontalOptions = LayoutOptions.Center
                    },
                    _orderTotalLabel
                }
            }
        };

        var tipCard = new Border
        {
            BackgroundColor = Color.FromArgb("#FFFBEB"),
            Stroke = Color.FromArgb("#FCD34D"),
            StrokeThickness = 1,
            Padding = 16,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Content = new Grid
            {
                ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) },
                Children =
                {
                    new Label
                    {
                        Text = "TIP AMOUNT:",
                        FontSize = 20,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#92400E"),
                        VerticalOptions = LayoutOptions.Center
                    }
                }
            }
        };
        ((Grid)tipCard.Content!).Add(_tipAmountLabel, 1);

        _closeButton = new Button
        {
            Text = "CLOSE",
            BackgroundColor = Color.FromArgb("#DC2626"),
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            FontSize = 14,
            WidthRequest = 90,
            HeightRequest = 45,
            CornerRadius = 14
        };
        _closeButton.Clicked += (_, _) => Complete(-1);

        _continueButton = new Button
        {
            Text = "CONTINUE TO PAYMENT",
            BackgroundColor = Color.FromArgb("#059669"),
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            FontSize = 20,
            CornerRadius = 14,
            HeightRequest = 60
        };
        _continueButton.Clicked += (_, _) => Complete(_selectedTip);

        _dialogTitle = new Label
        {
            Text = "ADD TIP",
            FontSize = 30,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#111827"),
            VerticalOptions = LayoutOptions.Center
        };

        var header = new Grid
        {
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) },
            Children = { _dialogTitle }
        };
        header.Add(_closeButton, 1);

        _dialogContent = new VerticalStackLayout
        {
            Spacing = 20,
            Children =
            {
                header,
                new BoxView { HeightRequest = 1, Color = Color.FromArgb("#E5E7EB") },
                _orderTotalCard,
                _tipOptionsGrid,
                customRow,
                tipCard,
                _continueButton
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

    public void SetOrderTotal(decimal total)
    {
        _orderTotalLabel.Text = $"£{total:F2}";
        _selectedTip = 0;
        _customTipEntry.Text = string.Empty;
        _tipAmountLabel.Text = "£0.00";
    }

    /// <summary>Returns selected tip amount, or -1 if cancelled.</summary>
    public async Task<decimal> ShowAsync(ContentPage? hostPage = null)
    {
        _tcs = new TaskCompletionSource<decimal>(TaskCreationOptions.RunContinuationsAsynchronously);
        _hostPage = hostPage;
        _parent = PaymentOverlayHost.Attach(this, hostPage);
        if (_parent is null)
        {
            return -1;
        }

        ApplyResponsiveLayout();
        return await _tcs.Task;
    }

    private void ApplyResponsiveLayout()
    {
        var width = Width;
        var height = Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var tablet = width > 0 && width < 1280;
        var shortWindow = height > 0 && height <= 720;
        var margin = tablet ? 12d : 16d;
        var availableWidth = Math.Max(280, width - (margin * 2));
        var cardWidth = Math.Min(PreferredWidth, availableWidth);

        _dialogCard.HorizontalOptions = LayoutOptions.Center;
        _dialogCard.VerticalOptions = LayoutOptions.Center;
        _dialogCard.WidthRequest = cardWidth;
        _dialogCard.MaximumWidthRequest = cardWidth;
        _dialogCard.Margin = new Thickness(margin);
        _dialogCard.Padding = new Thickness(tablet ? 24 : 30);
        _dialogCard.MaximumHeightRequest = Math.Max(
            280,
            Math.Min(PreferredHeight, height - (margin * 2) - 24));

        _dialogContent.Spacing = shortWindow ? 10 : tablet ? 12 : 20;
        _dialogTitle.FontSize = tablet ? 24 : 30;
        _closeButton.WidthRequest = tablet ? 78 : 90;
        _closeButton.HeightRequest = 45;
        _orderTotalCard.Padding = shortWindow ? 12 : tablet ? 16 : 20;
        _orderTotalLabel.FontSize = shortWindow ? 28 : tablet ? 31 : 36;
        _tipOptionsGrid.ColumnSpacing = tablet ? 10 : 15;
        _tipOptionsGrid.RowSpacing = tablet ? 10 : 15;
        _continueButton.HeightRequest = tablet ? 54 : 60;
    }

    private void SetTip(decimal tip)
    {
        _selectedTip = Math.Round(Math.Max(0, tip), 2);
        _tipAmountLabel.Text = $"£{_selectedTip:F2}";
        // Presets clear the custom field (Mother behavior).
        if (tip is 0 or 5 or 10 or 20)
        {
            _customTipEntry.Text = string.Empty;
        }
    }

    private async Task OpenCustomTipKeyboardAsync()
    {
        if (_keyboardOpen)
        {
            return;
        }

        _keyboardOpen = true;
        try
        {
            // Prefer page-root overlay (sibling above tip ZIndex 15000). Fallback: tip content grid.
            // Page-root AddToPage at ZIndex 10000 sits behind the tip and looks dead.
            var overlayHost = _parent
                ?? Content as Grid;
            if (overlayHost is null)
            {
                return;
            }

            var hostPage = _hostPage
                ?? PaymentOverlayHost.FindPage()
                ?? overlayHost.Window?.Page as ContentPage;

            var keyboard = new NumericKeyboardDialog();
            var amount = await keyboard.ShowCurrencyAsync(
                initialValue: _selectedTip > 0 ? _selectedTip : null,
                title: "Custom tip amount",
                hostPage: hostPage,
                minimum: 0m,
                maximum: 999999.99m,
                overlayHost: overlayHost);

            // Cancel / scrim close → null: leave current tip unchanged (Mother parity).
            if (amount.HasValue)
            {
                _selectedTip = Math.Round(Math.Max(0, amount.Value), 2);
                _customTipEntry.Text = amount.Value.ToString("0.00");
                _tipAmountLabel.Text = $"£{_selectedTip:F2}";
            }
        }
        finally
        {
            _keyboardOpen = false;
        }
    }

    private static Button MakeTipButton(string text, Color bg, Color fg, double fontSize, Action onClick)
    {
        var button = new Button
        {
            Text = text,
            BackgroundColor = bg,
            TextColor = fg,
            FontSize = fontSize,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 10,
            HeightRequest = 65
        };
        button.Clicked += (_, _) => onClick();
        return button;
    }

    private void Complete(decimal result)
    {
        PaymentOverlayHost.Detach(this, _parent);
        _parent = null;
        _tcs?.TrySetResult(result);
    }
}
