using Microsoft.Maui.Controls.Shapes;

namespace OrderWeb.SharedUI.Payments;

/// <summary>
/// Mother-chrome SELECT PAYMENT METHOD dialog (SharedUI — Mother + Client).
/// Cash / Card / Gift Card (+ optional Loyalty on Client).
/// </summary>
public sealed class PaymentMethodDialog : ContentView
{
    private const double PreferredWidth = 700;
    private const double PreferredHeight = 520;

    private readonly Border _dialogCard;
    private readonly VerticalStackLayout _dialogContent;
    private readonly Label _titleLabel;
    private readonly Button _closeButton;
    private readonly Border _amountDueCard;
    private readonly Label _amountDueLabel;
    private readonly Grid _paymentButtonsGrid;
    private readonly Button _cashButton;
    private readonly Button _cardButton;
    private readonly Button _giftCardButton;
    private readonly Button? _loyaltyButton;
    private readonly Border _remainingFrame;
    private readonly Label _remainingLabel;
    private TaskCompletionSource<PaymentMethodChoice>? _tcs;
    private Grid? _parent;
    private ContentPage? _hostPage;

    public PaymentMethodDialog(bool showLoyalty = false)
    {
        BackgroundColor = Color.FromArgb("#80000000");
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;

        _titleLabel = new Label
        {
            Text = "SELECT PAYMENT METHOD",
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
        _closeButton.Clicked += (_, _) => Complete(PaymentMethodChoice.Cancelled);

        var header = new Grid
        {
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) },
            Children = { _titleLabel }
        };
        header.Add(_closeButton, 1);

        _amountDueLabel = new Label
        {
            Text = "£0.00",
            FontSize = 36,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#B45309"),
            VerticalOptions = LayoutOptions.Center
        };

        _amountDueCard = new Border
        {
            BackgroundColor = Color.FromArgb("#FFFBEB"),
            Stroke = Color.FromArgb("#F59E0B"),
            StrokeThickness = 2,
            Padding = 20,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Content = new Grid
            {
                ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) },
                Children =
                {
                    new Label
                    {
                        Text = "AMOUNT DUE:",
                        FontSize = 24,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#92400E"),
                        VerticalOptions = LayoutOptions.Center
                    }
                }
            }
        };
        ((Grid)_amountDueCard.Content!).Add(_amountDueLabel, 1);

        _cashButton = MakeMethodButton("CASH", Color.FromArgb("#059669"), 28, PaymentMethodChoice.Cash);
        _cardButton = MakeMethodButton("CARD", Color.FromArgb("#2563EB"), 28, PaymentMethodChoice.Card);
        _giftCardButton = MakeMethodButton("GIFT CARD", Color.FromArgb("#7C3AED"), 24, PaymentMethodChoice.GiftCard);

        if (showLoyalty)
        {
            _loyaltyButton = MakeMethodButton("LOYALTY", Color.FromArgb("#0F766E"), 24, PaymentMethodChoice.Loyalty);
            _paymentButtonsGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new(GridLength.Star),
                    new(GridLength.Star),
                    new(GridLength.Star),
                    new(GridLength.Star)
                },
                ColumnSpacing = 16
            };
            _paymentButtonsGrid.Add(_cashButton, 0);
            _paymentButtonsGrid.Add(_cardButton, 1);
            _paymentButtonsGrid.Add(_giftCardButton, 2);
            _paymentButtonsGrid.Add(_loyaltyButton, 3);
        }
        else
        {
            _paymentButtonsGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new(GridLength.Star),
                    new(GridLength.Star),
                    new(GridLength.Star)
                },
                ColumnSpacing = 20
            };
            _paymentButtonsGrid.Add(_cashButton, 0);
            _paymentButtonsGrid.Add(_cardButton, 1);
            _paymentButtonsGrid.Add(_giftCardButton, 2);
        }

        _remainingLabel = new Label
        {
            Text = "£0.00",
            FontSize = 22,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#DC2626"),
            VerticalOptions = LayoutOptions.Center
        };

        _remainingFrame = new Border
        {
            BackgroundColor = Color.FromArgb("#FEF2F2"),
            Stroke = Color.FromArgb("#EF4444"),
            StrokeThickness = 2,
            Padding = 15,
            IsVisible = false,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Content = new Grid
            {
                ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) },
                Children =
                {
                    new Label
                    {
                        Text = "REMAINING BALANCE:",
                        FontSize = 18,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#DC2626"),
                        VerticalOptions = LayoutOptions.Center
                    }
                }
            }
        };
        ((Grid)_remainingFrame.Content!).Add(_remainingLabel, 1);

        _dialogContent = new VerticalStackLayout
        {
            Spacing = 25,
            Children =
            {
                header,
                new BoxView { HeightRequest = 2, Color = Color.FromArgb("#E5E7EB") },
                _amountDueCard,
                _paymentButtonsGrid,
                _remainingFrame
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

    public void SetAmountDue(decimal amount, decimal remaining = 0, string? title = null)
    {
        _titleLabel.Text = string.IsNullOrWhiteSpace(title)
            ? "SELECT PAYMENT METHOD"
            : title;
        _amountDueLabel.Text = $"£{amount:F2}";

        if (remaining > 0.009m)
        {
            _remainingFrame.IsVisible = true;
            _remainingLabel.Text = $"£{remaining:F2}";
        }
        else
        {
            _remainingFrame.IsVisible = false;
        }
    }

    public async Task<PaymentMethodChoice> ShowAsync(ContentPage? hostPage = null)
    {
        _tcs = new TaskCompletionSource<PaymentMethodChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        _hostPage = hostPage;
        _parent = PaymentOverlayHost.Attach(this, hostPage);
        if (_parent is null)
        {
            return PaymentMethodChoice.Cancelled;
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
            280,
            Math.Min(PreferredHeight, height - (margin * 2) - 24));

        _dialogContent.Spacing = shortWindow ? 12 : tablet ? 17 : 25;
        _titleLabel.FontSize = tablet ? 25 : 30;
        _closeButton.WidthRequest = tablet ? 82 : 90;
        _amountDueCard.Padding = shortWindow ? 14 : tablet ? 17 : 20;
        _amountDueLabel.FontSize = shortWindow ? 29 : tablet ? 32 : 36;
        _paymentButtonsGrid.ColumnSpacing = tablet ? 12 : (_loyaltyButton is null ? 20 : 16);

        var buttonHeight = shortWindow ? 88 : tablet ? 104 : 130;
        _cashButton.HeightRequest = buttonHeight;
        _cardButton.HeightRequest = buttonHeight;
        _giftCardButton.HeightRequest = buttonHeight;
        _cashButton.FontSize = tablet ? 24 : 28;
        _cardButton.FontSize = tablet ? 24 : 28;
        _giftCardButton.FontSize = tablet ? 21 : 24;
        if (_loyaltyButton is not null)
        {
            _loyaltyButton.HeightRequest = buttonHeight;
            _loyaltyButton.FontSize = tablet ? 18 : 22;
        }
    }

    private Button MakeMethodButton(string text, Color background, double fontSize, PaymentMethodChoice choice)
    {
        var button = new Button
        {
            Text = text,
            BackgroundColor = background,
            TextColor = Colors.White,
            FontSize = fontSize,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 14,
            HeightRequest = 130
        };
        button.Clicked += (_, _) => Complete(choice);
        return button;
    }

    private void Complete(PaymentMethodChoice choice)
    {
        PaymentOverlayHost.Detach(this, _parent);
        _parent = null;
        _tcs?.TrySetResult(choice);
    }
}
