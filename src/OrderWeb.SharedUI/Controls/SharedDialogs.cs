using Microsoft.Maui.Controls.Shapes;

namespace OrderWeb.SharedUI.Controls;

public class ConfirmationDialog : ContentView
{
    private readonly Label _title;
    private readonly Label _message;
    private readonly Label _icon;
    private readonly SharedButton _confirm;
    private readonly SharedButton _cancel;
    private TaskCompletionSource<bool>? _completion;

    public static readonly BindableProperty TitleProperty = BindableProperty.Create(nameof(Title), typeof(string), typeof(ConfirmationDialog), "Confirm", propertyChanged: (b, _, v) => ((ConfirmationDialog)b)._title.Text = v?.ToString());
    public static readonly BindableProperty MessageProperty = BindableProperty.Create(nameof(Message), typeof(string), typeof(ConfirmationDialog), string.Empty, propertyChanged: (b, _, v) => ((ConfirmationDialog)b)._message.Text = v?.ToString());
    public static readonly BindableProperty ConfirmTextProperty = BindableProperty.Create(nameof(ConfirmText), typeof(string), typeof(ConfirmationDialog), "Yes", propertyChanged: (b, _, v) => ((ConfirmationDialog)b)._confirm.Text = v?.ToString());
    public static readonly BindableProperty CancelTextProperty = BindableProperty.Create(nameof(CancelText), typeof(string), typeof(ConfirmationDialog), "No", propertyChanged: (b, _, v) => ((ConfirmationDialog)b)._cancel.Text = v?.ToString());

    public ConfirmationDialog()
    {
        BackgroundColor = Color.FromArgb("#80000000"); HorizontalOptions = LayoutOptions.Fill; VerticalOptions = LayoutOptions.Fill;
        _icon = new Label { Text = "?", TextColor = Colors.White, FontSize = 30, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center };
        var iconShell = new Border { WidthRequest = 74, HeightRequest = 74, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 37 }, Content = _icon, HorizontalOptions = LayoutOptions.Center }; iconShell.Use(Border.BackgroundColorProperty, "PosPrimary");
        _title = new Label { Text = "Confirm", FontSize = 22, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center }; _title.Use(Label.TextColorProperty, "PosTextStrong");
        _message = new Label { FontSize = 15, HorizontalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.WordWrap }; _message.Use(Label.TextColorProperty, "PosTextMuted");
        _cancel = new SharedButton { Text = "No", Variant = ButtonVariant.Secondary };
        _confirm = new SharedButton { Text = "Yes", Variant = ButtonVariant.Primary };
        _cancel.Clicked += (_, _) => Complete(false); _confirm.Clicked += (_, _) => Complete(true);
        var buttons = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 12 }; buttons.Add(_cancel); buttons.Add(_confirm, 1);
        var panel = new Border { WidthRequest = 450, MaximumWidthRequest = 450, Padding = 24, StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = 22 }, Content = new VerticalStackLayout { Spacing = 20, Children = { iconShell, _title, _message, buttons } }, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        panel.Use(Border.BackgroundColorProperty, "PosSurface"); panel.Use(Border.StrokeProperty, "PosBorder");
        Content = new Grid { Padding = 24, Children = { panel } };
    }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Message { get => (string)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public string ConfirmText { get => (string)GetValue(ConfirmTextProperty); set => SetValue(ConfirmTextProperty, value); }
    public string CancelText { get => (string)GetValue(CancelTextProperty); set => SetValue(CancelTextProperty, value); }
    public event EventHandler? Confirmed;
    public event EventHandler? Cancelled;
    public Task<bool> WaitForResultAsync() { _completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); return _completion.Task; }
    private void Complete(bool result) { if (result) Confirmed?.Invoke(this, EventArgs.Empty); else Cancelled?.Invoke(this, EventArgs.Empty); _completion?.TrySetResult(result); }
}

public class ErrorDialog : ContentView
{
    private readonly Label _title;
    private readonly Label _message;
    private readonly SharedButton _close;
    private TaskCompletionSource<bool>? _completion;
    public static readonly BindableProperty TitleProperty = BindableProperty.Create(nameof(Title), typeof(string), typeof(ErrorDialog), "Something went wrong", propertyChanged: (b, _, v) => ((ErrorDialog)b)._title.Text = v?.ToString());
    public static readonly BindableProperty MessageProperty = BindableProperty.Create(nameof(Message), typeof(string), typeof(ErrorDialog), string.Empty, propertyChanged: (b, _, v) => ((ErrorDialog)b)._message.Text = v?.ToString());
    public static readonly BindableProperty CloseTextProperty = BindableProperty.Create(nameof(CloseText), typeof(string), typeof(ErrorDialog), "Close", propertyChanged: (b, _, v) => ((ErrorDialog)b)._close.Text = v?.ToString());
    public ErrorDialog()
    {
        BackgroundColor = Color.FromArgb("#80000000"); HorizontalOptions = LayoutOptions.Fill; VerticalOptions = LayoutOptions.Fill;
        var icon = new Border { WidthRequest = 74, HeightRequest = 74, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 37 }, HorizontalOptions = LayoutOptions.Center, Content = new Label { Text = "!", TextColor = Colors.White, FontSize = 30, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center } }; icon.Use(Border.BackgroundColorProperty, "PosError");
        _title = new Label { Text = "Something went wrong", FontSize = 22, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center }; _title.Use(Label.TextColorProperty, "PosTextStrong");
        _message = new Label { FontSize = 15, HorizontalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.WordWrap }; _message.Use(Label.TextColorProperty, "PosTextMuted");
        _close = new SharedButton { Text = "Close", Variant = ButtonVariant.Danger }; _close.Clicked += (_, _) => { Closed?.Invoke(this, EventArgs.Empty); _completion?.TrySetResult(true); };
        var panel = new Border { WidthRequest = 450, MaximumWidthRequest = 450, Padding = 24, StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = 22 }, Content = new VerticalStackLayout { Spacing = 20, Children = { icon, _title, _message, _close } }, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }; panel.Use(Border.BackgroundColorProperty, "PosSurface"); panel.Use(Border.StrokeProperty, "PosErrorBorder");
        Content = new Grid { Padding = 24, Children = { panel } };
    }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Message { get => (string)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public string CloseText { get => (string)GetValue(CloseTextProperty); set => SetValue(CloseTextProperty, value); }
    public event EventHandler? Closed;
    public Task WaitForCloseAsync() { _completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); return _completion.Task; }
}
