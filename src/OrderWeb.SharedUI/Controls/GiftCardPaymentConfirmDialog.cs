using Microsoft.Maui.Controls.Shapes;

namespace OrderWeb.SharedUI.Controls;

/// <summary>
/// Elegant till confirm before Client/Mother posts gift-card top-up or activate to OrderWeb cloud.
/// </summary>
public sealed class GiftCardPaymentConfirmDialog : ContentView
{
    private readonly Label _amountLabel = new()
    {
        FontFamily = "OpenSansSemibold",
        FontSize = 36,
        TextColor = Color.FromArgb("#0F172A"),
        HorizontalTextAlignment = TextAlignment.Center
    };

    private readonly Label _methodLabel = new()
    {
        FontFamily = "OpenSansSemibold",
        FontSize = 13,
        TextColor = Color.FromArgb("#1D4ED8"),
        HorizontalTextAlignment = TextAlignment.Center,
        VerticalTextAlignment = TextAlignment.Center
    };

    private readonly Border _methodChip = new()
    {
        BackgroundColor = Color.FromArgb("#EFF6FF"),
        Stroke = Color.FromArgb("#BFDBFE"),
        StrokeThickness = 1,
        Padding = new Thickness(14, 6),
        HorizontalOptions = LayoutOptions.Center,
        StrokeShape = new RoundRectangle { CornerRadius = 20 }
    };

    private readonly Label _detailLabel = new()
    {
        FontFamily = "OpenSansRegular",
        FontSize = 14,
        TextColor = Color.FromArgb("#64748B"),
        HorizontalTextAlignment = TextAlignment.Center,
        LineBreakMode = LineBreakMode.WordWrap
    };

    private TaskCompletionSource<bool>? _completion;
    private Grid? _overlayHost;
    private bool _attached;

    public GiftCardPaymentConfirmDialog()
    {
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;
        BackgroundColor = Color.FromArgb("#990F172A");
        ZIndex = 2000;

        _methodChip.Content = _methodLabel;

        var confirm = new Button
        {
            Text = "Payment taken",
            BackgroundColor = Color.FromArgb("#059669"),
            TextColor = Colors.White,
            FontFamily = "OpenSansSemibold",
            FontSize = 16,
            CornerRadius = 12,
            HeightRequest = 52,
            HorizontalOptions = LayoutOptions.Fill
        };
        confirm.Clicked += (_, _) => Complete(true);

        var cancel = new Button
        {
            Text = "Cancel",
            BackgroundColor = Colors.Transparent,
            TextColor = Color.FromArgb("#64748B"),
            FontFamily = "OpenSansSemibold",
            FontSize = 15,
            CornerRadius = 12,
            HeightRequest = 48,
            HorizontalOptions = LayoutOptions.Fill
        };
        cancel.Clicked += (_, _) => Complete(false);

        var card = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#E2E8F0"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 22 },
            Padding = new Thickness(28, 26),
            WidthRequest = 420,
            MaximumWidthRequest = 440,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Shadow = new Shadow
            {
                Brush = Color.FromArgb("#0F172A"),
                Offset = new Point(0, 10),
                Radius = 28,
                Opacity = 0.18f
            },
            Content = new VerticalStackLayout
            {
                Spacing = 16,
                Children =
                {
                    new Border
                    {
                        BackgroundColor = Color.FromArgb("#ECFDF5"),
                        Stroke = Color.FromArgb("#A7F3D0"),
                        StrokeThickness = 1,
                        WidthRequest = 64,
                        HeightRequest = 64,
                        HorizontalOptions = LayoutOptions.Center,
                        StrokeShape = new RoundRectangle { CornerRadius = 32 },
                        Content = new Label
                        {
                            Text = "£",
                            FontFamily = "OpenSansSemibold",
                            FontSize = 26,
                            TextColor = Color.FromArgb("#059669"),
                            HorizontalOptions = LayoutOptions.Center,
                            VerticalOptions = LayoutOptions.Center
                        }
                    },
                    new Label
                    {
                        Text = "Confirm payment",
                        FontFamily = "OpenSansSemibold",
                        FontSize = 22,
                        TextColor = Color.FromArgb("#0F172A"),
                        HorizontalTextAlignment = TextAlignment.Center
                    },
                    _amountLabel,
                    _methodChip,
                    _detailLabel,
                    new BoxView
                    {
                        HeightRequest = 1,
                        Color = Color.FromArgb("#F1F5F9"),
                        Margin = new Thickness(0, 4)
                    },
                    confirm,
                    cancel
                }
            }
        };

        Content = card;
    }

    public Task<bool> ShowAsync(
        Grid overlayHost,
        string paymentMethod,
        decimal amount,
        string actionVerb)
    {
        ArgumentNullException.ThrowIfNull(overlayHost);

        var method = paymentMethod.Equals("card", StringComparison.OrdinalIgnoreCase) ? "Card" : "Cash";
        var action = string.IsNullOrWhiteSpace(actionVerb) ? "update" : actionVerb.Trim();

        _amountLabel.Text = $"£{amount:0.00}";
        _methodLabel.Text = method.ToUpperInvariant();
        _detailLabel.Text =
            $"Confirm {method.ToLowerInvariant()} was taken at the till before you {action} this gift card on OrderWeb cloud.";

        _completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _overlayHost = overlayHost;
        _attached = true;

        if (overlayHost.RowDefinitions.Count > 0)
        {
            Grid.SetRowSpan(this, Math.Max(1, overlayHost.RowDefinitions.Count));
        }

        if (overlayHost.ColumnDefinitions.Count > 0)
        {
            Grid.SetColumnSpan(this, Math.Max(1, overlayHost.ColumnDefinitions.Count));
        }

        overlayHost.Children.Add(this);
        return _completion.Task;
    }

    private void Complete(bool confirmed)
    {
        if (_completion is null)
        {
            return;
        }

        if (_attached && _overlayHost is not null)
        {
            _overlayHost.Children.Remove(this);
            _attached = false;
            _overlayHost = null;
        }

        _completion.TrySetResult(confirmed);
        _completion = null;
    }
}
