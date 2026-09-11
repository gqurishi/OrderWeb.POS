using Microsoft.Maui.Controls.Shapes;

namespace OrderWeb.SharedUI.Payments;

/// <summary>
/// Mother-chrome alert overlay for payment wizard (replaces system DisplayAlert).
/// </summary>
public sealed class PaymentAlertDialog : ContentView
{
    private readonly Border _dialogCard;
    private readonly Border _iconBorder;
    private readonly Label _iconLabel;
    private readonly Label _titleLabel;
    private readonly Label _messageLabel;
    private readonly Button _okButton;
    private TaskCompletionSource? _tcs;
    private Grid? _parent;

    public PaymentAlertDialog()
    {
        BackgroundColor = Color.FromArgb("#80000000");
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;
        InputTransparent = false;
        ZIndex = 16000;

        _iconLabel = new Label
        {
            Text = "!",
            TextColor = Colors.White,
            FontSize = 28,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };
        _iconBorder = new Border
        {
            WidthRequest = 64,
            HeightRequest = 64,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 32 },
            BackgroundColor = Color.FromArgb("#F59E0B"),
            HorizontalOptions = LayoutOptions.Center,
            Content = _iconLabel
        };

        _titleLabel = new Label
        {
            Text = "Notice",
            FontSize = 24,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#0F172A"),
            HorizontalTextAlignment = TextAlignment.Center
        };

        _messageLabel = new Label
        {
            Text = string.Empty,
            FontSize = 16,
            TextColor = Color.FromArgb("#475569"),
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.WordWrap
        };

        _okButton = new Button
        {
            Text = "OK",
            BackgroundColor = Color.FromArgb("#059669"),
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            FontSize = 17,
            CornerRadius = 14,
            HeightRequest = 54,
            Margin = new Thickness(0, 8, 0, 0)
        };
        _okButton.Clicked += (_, _) => Complete();

        _dialogCard = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#E2E8F0"),
            StrokeThickness = 1,
            Padding = 28,
            WidthRequest = 440,
            MaximumWidthRequest = 480,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            StrokeShape = new RoundRectangle { CornerRadius = 22 },
            Shadow = new Shadow
            {
                Brush = Color.FromArgb("#0F172A"),
                Opacity = 0.18f,
                Radius = 22,
                Offset = new Point(0, 10)
            },
            Content = new VerticalStackLayout
            {
                Spacing = 14,
                Children =
                {
                    _iconBorder,
                    _titleLabel,
                    _messageLabel,
                    _okButton
                }
            }
        };

        Content = new Grid
        {
            Padding = 16,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            Children = { _dialogCard }
        };
    }

    public void SetAlert(
        string title,
        string message,
        string okText = "OK",
        PaymentAlertTone tone = PaymentAlertTone.Warning)
    {
        _titleLabel.Text = string.IsNullOrWhiteSpace(title) ? "Notice" : title.Trim();
        _messageLabel.Text = message ?? string.Empty;
        _okButton.Text = string.IsNullOrWhiteSpace(okText) ? "OK" : okText.Trim();

        switch (tone)
        {
            case PaymentAlertTone.Error:
                _iconLabel.Text = "!";
                _iconBorder.BackgroundColor = Color.FromArgb("#DC2626");
                _okButton.BackgroundColor = Color.FromArgb("#DC2626");
                break;
            case PaymentAlertTone.Success:
                _iconLabel.Text = "✓";
                _iconBorder.BackgroundColor = Color.FromArgb("#059669");
                _okButton.BackgroundColor = Color.FromArgb("#059669");
                break;
            case PaymentAlertTone.Info:
                _iconLabel.Text = "i";
                _iconBorder.BackgroundColor = Color.FromArgb("#2563EB");
                _okButton.BackgroundColor = Color.FromArgb("#2563EB");
                break;
            default:
                _iconLabel.Text = "!";
                _iconBorder.BackgroundColor = Color.FromArgb("#F59E0B");
                _okButton.BackgroundColor = Color.FromArgb("#059669");
                break;
        }
    }

    public async Task ShowAsync(ContentPage? hostPage = null)
    {
        _tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _parent = PaymentOverlayHost.Attach(this, hostPage);
        if (_parent is null)
        {
            return;
        }

        await _tcs.Task;
    }

    private void Complete()
    {
        PaymentOverlayHost.Detach(this, _parent);
        _parent = null;
        _tcs?.TrySetResult();
    }
}

public enum PaymentAlertTone
{
    Warning,
    Error,
    Success,
    Info
}
