using Microsoft.Maui.Controls.Shapes;

namespace OrderWeb.SharedUI.Controls;

/// <summary>
/// Shared "re-pairing required" presentation for Client terminals disabled by Mother.
/// Pairing workflow itself remains Client-owned.
/// </summary>
public sealed class RepairRequiredView : ContentView
{
    private readonly Label _title = new()
    {
        Text = "Client POS Disabled",
        FontSize = 32,
        FontAttributes = FontAttributes.Bold,
        HorizontalTextAlignment = TextAlignment.Center
    };

    private readonly Label _message = new()
    {
        FontSize = 17,
        HorizontalTextAlignment = TextAlignment.Center,
        LineBreakMode = LineBreakMode.WordWrap
    };

    private readonly Label _guidance = new()
    {
        Text = "Ask the Mother POS operator to create a new pairing code before this terminal can be used again.",
        FontSize = 14,
        HorizontalTextAlignment = TextAlignment.Center,
        LineBreakMode = LineBreakMode.WordWrap
    };

    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title), typeof(string), typeof(RepairRequiredView), "Client POS Disabled",
        propertyChanged: (b, _, v) => ((RepairRequiredView)b)._title.Text = v?.ToString() ?? "Client POS Disabled");

    public static readonly BindableProperty MessageProperty = BindableProperty.Create(
        nameof(Message), typeof(string), typeof(RepairRequiredView),
        "This terminal has been disabled by the Mother POS.",
        propertyChanged: (b, _, v) => ((RepairRequiredView)b)._message.Text = v?.ToString() ?? string.Empty);

    public static readonly BindableProperty ActionTextProperty = BindableProperty.Create(
        nameof(ActionText), typeof(string), typeof(RepairRequiredView), "Enter New Pairing Code",
        propertyChanged: (b, _, v) => ((RepairRequiredView)b)._action.Text = v?.ToString() ?? "Enter New Pairing Code");

    private readonly SharedButton _action = new() { Text = "Enter New Pairing Code" };

    public RepairRequiredView()
    {
        _title.Use(Label.TextColorProperty, "PosErrorStrong");
        _message.Text = "This terminal has been disabled by the Mother POS.";
        _message.Use(Label.TextColorProperty, "PosErrorText");
        _guidance.Use(Label.TextColorProperty, "PosTextMuted");
        _action.Clicked += (_, _) => ActionRequested?.Invoke(this, EventArgs.Empty);

        var card = new Border
        {
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Padding = 32,
            WidthRequest = 620,
            MaximumWidthRequest = 620,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = new VerticalStackLayout
            {
                Spacing = 18,
                Children = { _title, _message, _guidance, _action }
            }
        };
        card.Use(Border.BackgroundColorProperty, "PosErrorSoft");
        card.Use(Border.StrokeProperty, "PosErrorBorder");

        Content = new Grid
        {
            Children = { card }
        };
        this.Use(BackgroundColorProperty, "PosBackground");
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

    public string ActionText
    {
        get => (string)GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public event EventHandler? ActionRequested;
}
