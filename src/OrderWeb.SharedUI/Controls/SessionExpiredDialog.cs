using Microsoft.Maui.Controls.Shapes;

namespace OrderWeb.SharedUI.Controls;

/// <summary>
/// Shared session-expired presentation. Hosts decide when to show it and what
/// happens after the user confirms (usually return to login).
/// </summary>
public sealed class SessionExpiredDialog : ContentView
{
    private readonly Label _title = new()
    {
        Text = "Session expired",
        FontSize = 22,
        FontAttributes = FontAttributes.Bold,
        HorizontalTextAlignment = TextAlignment.Center
    };

    private readonly Label _message = new()
    {
        Text = "For security, you have been signed out. Enter your PIN to continue.",
        FontSize = 15,
        HorizontalTextAlignment = TextAlignment.Center,
        LineBreakMode = LineBreakMode.WordWrap
    };

    private TaskCompletionSource<bool>? _completion;

    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title), typeof(string), typeof(SessionExpiredDialog), "Session expired",
        propertyChanged: (b, _, v) => ((SessionExpiredDialog)b)._title.Text = v?.ToString() ?? "Session expired");

    public static readonly BindableProperty MessageProperty = BindableProperty.Create(
        nameof(Message), typeof(string), typeof(SessionExpiredDialog),
        "For security, you have been signed out. Enter your PIN to continue.",
        propertyChanged: (b, _, v) => ((SessionExpiredDialog)b)._message.Text = v?.ToString() ?? string.Empty);

    public SessionExpiredDialog()
    {
        IsVisible = false;
        BackgroundColor = Color.FromArgb("#80000000");
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;

        _title.Use(Label.TextColorProperty, "PosTextStrong");
        _message.Use(Label.TextColorProperty, "PosTextMuted");

        var icon = new Border
        {
            WidthRequest = 74,
            HeightRequest = 74,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 37 },
            HorizontalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = "!",
                TextColor = Colors.White,
                FontSize = 30,
                FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        };
        icon.Use(Border.BackgroundColorProperty, "PosWarning");

        var confirm = new SharedButton { Text = "Enter PIN" };
        confirm.Clicked += (_, _) => Complete();

        var panel = new Border
        {
            WidthRequest = 450,
            MaximumWidthRequest = 450,
            Padding = 24,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 22 },
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = new VerticalStackLayout
            {
                Spacing = 20,
                Children = { icon, _title, _message, confirm }
            }
        };
        panel.Use(Border.BackgroundColorProperty, "PosSurface");
        panel.Use(Border.StrokeProperty, "PosWarningBorder");
        Content = new Grid { Padding = 24, Children = { panel } };
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public event EventHandler? Confirmed;

    public Task ShowAsync(string? message = null)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            Message = message;
        }

        IsVisible = true;
        _completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        return _completion.Task;
    }

    private void Complete()
    {
        IsVisible = false;
        Confirmed?.Invoke(this, EventArgs.Empty);
        _completion?.TrySetResult(true);
    }
}
