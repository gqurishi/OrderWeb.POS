using Microsoft.Maui.Controls.Shapes;

namespace OrderWeb.SharedUI.Controls;

/// <summary>Shared, host-neutral transient notification surface for Mother and Client.</summary>
public sealed class PosToast : ContentView
{
    private readonly Border _card;
    private readonly Label _title;
    private readonly Label _message;
    private readonly SharedButton _retry;

    public static readonly BindableProperty TitleProperty = BindableProperty.Create(nameof(Title), typeof(string), typeof(PosToast), string.Empty, propertyChanged: (b, _, v) => ((PosToast)b)._title.Text = v?.ToString() ?? string.Empty);
    public static readonly BindableProperty MessageProperty = BindableProperty.Create(nameof(Message), typeof(string), typeof(PosToast), string.Empty, propertyChanged: (b, _, v) => ((PosToast)b)._message.Text = v?.ToString() ?? string.Empty);
    public static readonly BindableProperty KindProperty = BindableProperty.Create(nameof(Kind), typeof(StatusKind), typeof(PosToast), StatusKind.Info, propertyChanged: (b, _, _) => ((PosToast)b).ApplyKind());
    public static readonly BindableProperty IsRetryVisibleProperty = BindableProperty.Create(nameof(IsRetryVisible), typeof(bool), typeof(PosToast), false, propertyChanged: (b, _, v) => ((PosToast)b)._retry.IsVisible = (bool)v);

    public PosToast()
    {
        _title = new Label { FontAttributes = FontAttributes.Bold, FontSize = 15 };
        _message = new Label { FontSize = 13, LineBreakMode = LineBreakMode.WordWrap };
        _retry = new SharedButton { Text = "Retry", Variant = ButtonVariant.Secondary, IsVisible = false };
        _retry.Clicked += (_, _) => RetryRequested?.Invoke(this, EventArgs.Empty);
        var dismiss = new Button { Text = "×", FontSize = 22, Padding = 0, WidthRequest = 32, HeightRequest = 32, BackgroundColor = Colors.Transparent, BorderWidth = 0 };
        dismiss.Clicked += (_, _) => DismissRequested?.Invoke(this, EventArgs.Empty);
        var text = new VerticalStackLayout { Spacing = 2, Children = { _title, _message } };
        var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 10 };
        row.Add(text); row.Add(_retry, 1); row.Add(dismiss, 2);
        _card = new Border { Padding = new Thickness(16, 12), StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = 12 }, Content = row };
        Content = _card;
        ApplyKind();
    }

    public event EventHandler? RetryRequested;
    public event EventHandler? DismissRequested;
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Message { get => (string)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public StatusKind Kind { get => (StatusKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public bool IsRetryVisible { get => (bool)GetValue(IsRetryVisibleProperty); set => SetValue(IsRetryVisibleProperty, value); }

    private void ApplyKind()
    {
        var (surface, border, text) = Kind switch
        {
            StatusKind.Success => ("OwSuccessSoft", "OwSuccess", "OwSuccessText"),
            StatusKind.Warning => ("OwWarningSoft", "OwWarningBorder", "OwWarningText"),
            StatusKind.Error => ("OwErrorSoft", "OwErrorBorder", "OwErrorText"),
            _ => ("OwInfoSoft", "OwInfo", "OwInfoText")
        };
        _card.Use(Border.BackgroundColorProperty, surface);
        _card.Use(Border.StrokeProperty, border);
        _title.Use(Label.TextColorProperty, text);
        _message.Use(Label.TextColorProperty, text);
    }
}

/// <summary>Shared action list; the host decides what each action does.</summary>
public sealed class PosActionSheet : ContentView
{
    private readonly VerticalStackLayout _actions;
    public static readonly BindableProperty TitleProperty = BindableProperty.Create(nameof(Title), typeof(string), typeof(PosActionSheet), "Options", propertyChanged: (b, _, v) => ((PosActionSheet)b)._title.Text = v?.ToString() ?? "Options");
    public static readonly BindableProperty ActionsProperty = BindableProperty.Create(nameof(Actions), typeof(IEnumerable<HeaderAction>), typeof(PosActionSheet), propertyChanged: (b, _, v) => ((PosActionSheet)b).Build((IEnumerable<HeaderAction>?)v));
    private readonly Label _title;
    public PosActionSheet()
    {
        _title = new Label { FontSize = 21, FontAttributes = FontAttributes.Bold }; _title.Use(Label.TextColorProperty, "OwTextStrong");
        _actions = new VerticalStackLayout { Spacing = 8 };
        var panel = new Border { Padding = 20, StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = 18 }, Content = new VerticalStackLayout { Spacing = 16, Children = { _title, _actions } } };
        panel.Use(Border.BackgroundColorProperty, "OwSurface"); panel.Use(Border.StrokeProperty, "OwBorder");
        Content = panel;
    }
    public event EventHandler<HeaderActionEventArgs>? ActionRequested;
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public IEnumerable<HeaderAction>? Actions { get => (IEnumerable<HeaderAction>?)GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }
    private void Build(IEnumerable<HeaderAction>? actions)
    {
        if (_actions is null) return;
        _actions.Children.Clear();
        foreach (var action in actions ?? [])
        {
            var button = new SharedButton { Text = action.Text, IsEnabled = action.IsEnabled, Variant = action.IsPrimary ? ButtonVariant.Primary : ButtonVariant.Secondary };
            button.Clicked += (_, _) => { action.Command?.Execute(action.Parameter); ActionRequested?.Invoke(this, new HeaderActionEventArgs(action)); };
            _actions.Children.Add(button);
        }
    }
}

/// <summary>Shared text prompt with optional keyboard supplied by the host platform.</summary>
public sealed class PosPromptDialog : ContentView
{
    private readonly Label _title;
    private readonly Label _message;
    private readonly Entry _input;
    public static readonly BindableProperty TitleProperty = BindableProperty.Create(nameof(Title), typeof(string), typeof(PosPromptDialog), "Input", propertyChanged: (b, _, v) => ((PosPromptDialog)b)._title.Text = v?.ToString() ?? "Input");
    public static readonly BindableProperty MessageProperty = BindableProperty.Create(nameof(Message), typeof(string), typeof(PosPromptDialog), string.Empty, propertyChanged: (b, _, v) => ((PosPromptDialog)b)._message.Text = v?.ToString() ?? string.Empty);
    public PosPromptDialog()
    {
        _title = new Label { FontSize = 21, FontAttributes = FontAttributes.Bold }; _title.Use(Label.TextColorProperty, "OwTextStrong");
        _message = new Label { FontSize = 14 }; _message.Use(Label.TextColorProperty, "OwTextMuted");
        _input = new Entry { FontSize = 17 }; _input.Use(Entry.TextColorProperty, "OwTextPrimary");
        var cancel = new SharedButton { Text = "Cancel", Variant = ButtonVariant.Secondary }; cancel.Clicked += (_, _) => Cancelled?.Invoke(this, EventArgs.Empty);
        var confirm = new SharedButton { Text = "Confirm" }; confirm.Clicked += (_, _) => Confirmed?.Invoke(this, EventArgs.Empty);
        var buttons = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 10 }; buttons.Add(cancel); buttons.Add(confirm, 1);
        var panel = new Border { Padding = 20, StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = 18 }, Content = new VerticalStackLayout { Spacing = 14, Children = { _title, _message, _input, buttons } } };
        panel.Use(Border.BackgroundColorProperty, "OwSurface"); panel.Use(Border.StrokeProperty, "OwBorder"); Content = panel;
    }
    public event EventHandler? Confirmed;
    public event EventHandler? Cancelled;
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Message { get => (string)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public string Text { get => _input.Text ?? string.Empty; set => _input.Text = value; }
}

public sealed class PosLoadingOverlay : ContentView
{
    private readonly Label _message;
    public static readonly BindableProperty MessageProperty = BindableProperty.Create(nameof(Message), typeof(string), typeof(PosLoadingOverlay), "Loading…", propertyChanged: (b, _, v) => ((PosLoadingOverlay)b)._message.Text = v?.ToString() ?? "Loading…");
    public PosLoadingOverlay()
    {
        _message = new Label { FontSize = 16, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center }; _message.Use(Label.TextColorProperty, "OwTextStrong");
        var panel = new Border { Padding = 24, StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = 16 }, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Content = new VerticalStackLayout { Spacing = 12, Children = { new ActivityIndicator { IsRunning = true, WidthRequest = 42, HeightRequest = 42 }, _message } } };
        panel.Use(Border.BackgroundColorProperty, "OwSurface"); panel.Use(Border.StrokeProperty, "OwBorder");
        BackgroundColor = Color.FromArgb("#66000000"); Content = panel;
    }
    public string Message { get => (string)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
}
