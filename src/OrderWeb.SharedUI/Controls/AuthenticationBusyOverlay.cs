using Microsoft.Maui.Controls.Shapes;

namespace OrderWeb.SharedUI.Controls;

/// <summary>
/// Pre-auth / auth loading overlay shared by Mother and Client.
/// </summary>
public sealed class AuthenticationBusyOverlay : ContentView
{
    private readonly Label _message = new()
    {
        Text = "Checking PIN...",
        FontSize = 16,
        FontAttributes = FontAttributes.Bold,
        HorizontalTextAlignment = TextAlignment.Center
    };

    private readonly ActivityIndicator _indicator = new()
    {
        IsRunning = true,
        WidthRequest = 42,
        HeightRequest = 42
    };

    public static readonly BindableProperty MessageProperty = BindableProperty.Create(
        nameof(Message), typeof(string), typeof(AuthenticationBusyOverlay), "Checking PIN...",
        propertyChanged: (b, _, v) => ((AuthenticationBusyOverlay)b)._message.Text = string.IsNullOrWhiteSpace(v?.ToString()) ? "Checking PIN..." : v!.ToString()!);

    public AuthenticationBusyOverlay()
    {
        IsVisible = false;
        BackgroundColor = Color.FromArgb("#66F8FAFC");
        _message.Use(Label.TextColorProperty, "PosPrimary");
        _indicator.Color = Color.FromArgb("#2563EB");

        var card = new Border
        {
            Padding = 24,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = new VerticalStackLayout
            {
                Spacing = 14,
                Children = { _indicator, _message }
            }
        };
        card.Use(Border.BackgroundColorProperty, "PosSurface");
        card.Use(Border.StrokeProperty, "PosBorder");
        Content = card;
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public void Show(string? message = null)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            Message = message;
        }

        _indicator.IsRunning = true;
        IsVisible = true;
    }

    public void Hide()
    {
        _indicator.IsRunning = false;
        IsVisible = false;
    }
}
