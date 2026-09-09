namespace OrderWeb.Client.Dialogs;

/// <summary>POS-styled drawer-reason selector shared visually with Mother.</summary>
public sealed class CashDrawerReasonDialogPage : ContentPage
{
    private readonly TaskCompletionSource<string?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private INavigation? _hostNavigation;
    private bool _closed;

    public CashDrawerReasonDialogPage()
    {
        BackgroundColor = Color.FromArgb("#99000000");
        var choices = new[] { "No Sale", "Shopping", "Delivery", "Refund", "Cash Count", "Other" };
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 12, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) } };
        for (var i = 0; i < choices.Length; i++)
        {
            var choice = choices[i];
            var button = new Button { Text = choice, HeightRequest = 72, BackgroundColor = Color.FromArgb("#F8FAFC"), TextColor = Color.FromArgb("#243047"), FontSize = 16, FontAttributes = FontAttributes.Bold, BorderColor = Color.FromArgb("#D8E2F1"), BorderWidth = 1, CornerRadius = 16 };
            button.Clicked += async (_, _) => await CloseAsync(choice);
            grid.Add(button, i % 2, i / 2);
        }
        var cancel = new Button { Text = "Cancel", HeightRequest = 50, BackgroundColor = Color.FromArgb("#E8EDF5"), TextColor = Color.FromArgb("#40516C"), FontSize = 16, FontAttributes = FontAttributes.Bold, CornerRadius = 14 };
        cancel.Clicked += async (_, _) => await CloseAsync(null);
        var icon = new Border { WidthRequest = 80, HeightRequest = 80, BackgroundColor = Color.FromArgb("#0F8278"), StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 40 }, Content = new Label { Text = "£", FontSize = 36, TextColor = Colors.White, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center } };
        var card = new Border { WidthRequest = 640, MaximumWidthRequest = 640, Padding = new Thickness(38, 34), BackgroundColor = Colors.White, Stroke = Color.FromArgb("#DDE4EE"), StrokeThickness = 1, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 28 }, Content = new VerticalStackLayout { Spacing = 24, HorizontalOptions = LayoutOptions.Fill, Children = { icon, new Label { Text = "Cash Drawer Reason", FontSize = 30, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#202B3D"), HorizontalTextAlignment = TextAlignment.Center }, grid, cancel } } };
        Content = new Grid { Padding = 24, Children = { new Grid { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Children = { card } } } };
    }

    public async Task<string?> ShowAsync(INavigation navigation)
    {
        _hostNavigation = navigation;
        await navigation.PushModalAsync(this, false);
        return await _completion.Task;
    }

    private async Task CloseAsync(string? result)
    {
        if (_closed) return; _closed = true;
        try
        {
            var navigation = _hostNavigation ?? Navigation;
            if (navigation.ModalStack.Contains(this))
            {
                await navigation.PopModalAsync(false);
            }
        }
        finally
        {
            _completion.TrySetResult(result);
        }
    }
}

public sealed record CashDrawerFormResult(string? Details, decimal? Amount);

/// <summary>Client-side presentation only; Mother validates and records the action.</summary>
public sealed class CashDrawerFormDialogPage : ContentPage
{
    private readonly TaskCompletionSource<CashDrawerFormResult?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private INavigation? _hostNavigation;
    private readonly Entry? _details;
    private readonly Entry? _amount;
    private bool _closed;

    public CashDrawerFormDialogPage(string title, string message, string actionText, string accent, string? detailsPlaceholder = null, string? amountLabel = null)
    {
        BackgroundColor = Color.FromArgb("#99000000");
        var content = new VerticalStackLayout { Spacing = 18 };
        content.Children.Add(Icon(accent, "£"));
        content.Children.Add(new Label { Text = title, FontSize = 30, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#202B3D"), HorizontalTextAlignment = TextAlignment.Center });
        content.Children.Add(new Label { Text = message, FontSize = 17, TextColor = Color.FromArgb("#687386"), HorizontalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.WordWrap });
        if (!string.IsNullOrWhiteSpace(detailsPlaceholder))
        {
            _details = new Entry { Placeholder = detailsPlaceholder, FontSize = 18, TextColor = Color.FromArgb("#202B3D"), PlaceholderColor = Color.FromArgb("#9AA6B8"), IsReadOnly = true };
            AttachTouchKeyboard(_details, title, numeric: false);
            content.Children.Add(Field(null, _details));
        }
        if (!string.IsNullOrWhiteSpace(amountLabel))
        {
            _amount = new Entry { Placeholder = "0.00", Keyboard = Keyboard.Numeric, FontSize = 18, TextColor = Color.FromArgb("#202B3D"), PlaceholderColor = Color.FromArgb("#9AA6B8"), IsReadOnly = true };
            AttachTouchKeyboard(_amount, amountLabel, numeric: true);
            content.Children.Add(new Label { Text = amountLabel, FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#60718C") });
            content.Children.Add(Field(null, _amount));
        }
        var cancel = new Button { Text = "Cancel", HeightRequest = 56, BackgroundColor = Color.FromArgb("#E2E8F0"), TextColor = Color.FromArgb("#334155"), FontSize = 18, FontAttributes = FontAttributes.Bold, CornerRadius = 16 };
        cancel.Clicked += async (_, _) => await CloseAsync(null);
        var proceed = new Button { Text = actionText, HeightRequest = 56, BackgroundColor = Color.FromArgb(accent), TextColor = Colors.White, FontSize = 18, FontAttributes = FontAttributes.Bold, CornerRadius = 16 };
        proceed.Clicked += async (_, _) =>
        {
            decimal? amount = null;
            if (_amount != null)
            {
                if (!decimal.TryParse(_amount.Text, out var parsed) || parsed < 0)
                {
                    await DisplayAlertAsync("Amount required", "Enter a valid amount.", "OK");
                    return;
                }
                amount = parsed;
            }
            if (_details != null && string.IsNullOrWhiteSpace(_details.Text))
            {
                await DisplayAlertAsync("Reason required", "Enter a reason before continuing.", "OK");
                return;
            }
            await CloseAsync(new CashDrawerFormResult(_details?.Text?.Trim(), amount));
        };
        var buttons = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 18, Children = { cancel, proceed } };
        Grid.SetColumn(proceed, 1); content.Children.Add(buttons);
        var card = new Border { WidthRequest = 620, MaximumWidthRequest = 620, Padding = new Thickness(46, 38), BackgroundColor = Colors.White, Stroke = Color.FromArgb("#DDE4EE"), StrokeThickness = 1, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 28 }, Content = content };
        Content = new Grid { Padding = 24, Children = { new Grid { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Children = { card } } } };
    }

    public async Task<CashDrawerFormResult?> ShowAsync(INavigation navigation) { _hostNavigation = navigation; await navigation.PushModalAsync(this, false); return await _completion.Task; }
    private async Task CloseAsync(CashDrawerFormResult? result) { if (_closed) return; _closed = true; try { var navigation = _hostNavigation ?? Navigation; if (navigation.ModalStack.Contains(this)) await navigation.PopModalAsync(false); } finally { _completion.TrySetResult(result); } }
    private void AttachTouchKeyboard(Entry entry, string title, bool numeric)
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            var keyboard = new CashDrawerTouchKeyboardPage(title, entry.Text ?? string.Empty, numeric);
            var result = await keyboard.ShowAsync(_hostNavigation ?? Navigation);
            if (result is not null) entry.Text = result;
        };
        entry.GestureRecognizers.Add(tap);
    }
    private static Border Field(string? label, View value) => new() { BackgroundColor = Color.FromArgb("#F8FAFC"), Stroke = Color.FromArgb("#D8E2F1"), StrokeThickness = 1, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 }, Padding = new Thickness(16, 6), Content = value };
    private static Border Icon(string accent, string text) => new() { WidthRequest = 80, HeightRequest = 80, HorizontalOptions = LayoutOptions.Center, BackgroundColor = Color.FromArgb(accent), StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 40 }, Content = new Label { Text = text, FontSize = 36, TextColor = Colors.White, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center } };
}

/// <summary>Touch-first keyboard matching the Mother POS virtual keyboard behavior.</summary>
public sealed class CashDrawerTouchKeyboardPage : ContentPage
{
    private readonly TaskCompletionSource<string?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Label _value;
    private INavigation? _hostNavigation;
    private bool _closed;
    private string _text;
    private bool _isNumeric;

    public CashDrawerTouchKeyboardPage(string title, string initialValue, bool numeric)
    {
        _text = initialValue;
        BackgroundColor = Color.FromArgb("#99000000");
        _isNumeric = numeric;

        if (numeric)
        {
            _value = new Label { Text = _text, FontSize = 24, TextColor = Color.FromArgb("#202B3D"), VerticalTextAlignment = TextAlignment.Center };
            Content = BuildNumericKeypad(title);
            return;
        }

        _value = new Label { Text = DisplayText(), FontSize = 22, TextColor = Color.FromArgb("#202B3D"), VerticalTextAlignment = TextAlignment.Center };
        var rows = new VerticalStackLayout { Spacing = 8 };
        var keyRows = numeric
            ? new[] { new[] { "1", "2", "3" }, new[] { "4", "5", "6" }, new[] { "7", "8", "9" }, new[] { ".", "0", "⌫" } }
            : new[] { new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" }, new[] { "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P" }, new[] { "A", "S", "D", "F", "G", "H", "J", "K", "L" }, new[] { "Z", "X", "C", "V", "B", "N", "M", "⌫" } };
        foreach (var keys in keyRows)
        {
            var row = new Grid { ColumnSpacing = 6 };
            for (var i = 0; i < keys.Length; i++) row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            for (var i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                var button = KeyButton(key, numeric ? 64 : 48);
                button.Clicked += (_, _) => Press(key, numeric);
                row.Add(button, i, 0);
            }
            rows.Children.Add(row);
        }
        if (!numeric)
        {
            var space = KeyButton("Space", 48); space.Clicked += (_, _) => Press(" ", false); rows.Children.Add(space);
        }
        var clear = new Button { Text = "Clear", HeightRequest = 52, CornerRadius = 14, BackgroundColor = Color.FromArgb("#E2E8F0"), TextColor = Color.FromArgb("#334155"), FontAttributes = FontAttributes.Bold };
        clear.Clicked += (_, _) => { _text = string.Empty; UpdateValue(); };
        var cancel = new Button { Text = "Cancel", HeightRequest = 56, CornerRadius = 14, BackgroundColor = Color.FromArgb("#E2E8F0"), TextColor = Color.FromArgb("#334155"), FontAttributes = FontAttributes.Bold };
        cancel.Clicked += async (_, _) => await CloseAsync(null);
        var done = new Button { Text = "Done", HeightRequest = 56, CornerRadius = 14, BackgroundColor = Color.FromArgb("#2563EB"), TextColor = Colors.White, FontAttributes = FontAttributes.Bold };
        done.Clicked += async (_, _) => await CloseAsync(_text);
        var actions = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 14, Children = { cancel, done } }; Grid.SetColumn(done, 1);
        var cardContent = new VerticalStackLayout { Spacing = 14, Children = { new Label { Text = title, FontSize = 24, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, BackgroundColor = Color.FromArgb("#2563EB"), Padding = new Thickness(18, 12), HorizontalTextAlignment = TextAlignment.Center }, new Border { BackgroundColor = Colors.White, Stroke = Color.FromArgb("#CBD5E1"), StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 }, Padding = new Thickness(16, 8), HeightRequest = 58, Content = _value }, rows, clear, actions } };
        var width = numeric ? 430 : 920;
        var card = new Border { WidthRequest = width, MaximumWidthRequest = width, BackgroundColor = Color.FromArgb("#F8FAFC"), Stroke = Color.FromArgb("#D8E2F1"), StrokeThickness = 1, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 22 }, Padding = new Thickness(18), Content = cardContent };
        Content = new Grid { Padding = 20, Children = { new Grid { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Children = { card } } } };
    }

    private View BuildNumericKeypad(string title)
    {
        var cancel = new Button { Text = "CANCEL", WidthRequest = 110, HeightRequest = 48, CornerRadius = 15, BackgroundColor = Color.FromArgb("#EF2A2A"), TextColor = Colors.White, FontAttributes = FontAttributes.Bold };
        cancel.Clicked += async (_, _) => await CloseAsync(null);
        var header = new Grid { Padding = new Thickness(28, 14), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        header.Add(new Label { Text = title, FontSize = 25, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, VerticalTextAlignment = TextAlignment.Center }, 0);
        header.Add(cancel, 1);

        var valueField = new Border { BackgroundColor = Color.FromArgb("#F1F5F9"), Stroke = Color.FromArgb("#D8E2F1"), StrokeThickness = 2, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 }, Padding = new Thickness(18, 8), HeightRequest = 74, Content = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 10, Children = { new Label { Text = "£", FontSize = 34, TextColor = Color.FromArgb("#202B3D"), VerticalTextAlignment = TextAlignment.Center }, _value } } };
        Grid.SetColumn(_value, 1);

        var keypad = new Grid { RowSpacing = 12, ColumnSpacing = 12, RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) }, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        var keys = new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", ".", "0", "DEL" };
        for (var index = 0; index < keys.Length; index++)
        {
            var key = keys[index];
            var button = NumericKeyButton(key);
            button.Clicked += (_, _) => Press(key, true);
            keypad.Add(button, index % 3, index / 3);
        }

        var done = new Button { Text = "DONE", HeightRequest = 64, CornerRadius = 18, BackgroundColor = Color.FromArgb("#10B981"), TextColor = Colors.White, FontSize = 20, FontAttributes = FontAttributes.Bold };
        done.Clicked += async (_, _) => await CloseAsync(_text);
        var body = new VerticalStackLayout { Padding = new Thickness(30, 28, 30, 30), Spacing = 20, Children = { valueField, keypad, done } };
        var card = new Border { WidthRequest = 480, MaximumWidthRequest = 480, BackgroundColor = Colors.White, StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 28 }, Content = new VerticalStackLayout { Spacing = 0, Children = { new Border { BackgroundColor = Color.FromArgb("#3B82F6"), StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(28, 28, 0, 0) }, Content = header }, body } } };
        return new Grid { Padding = 20, Children = { new Grid { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Children = { card } } } };
    }

    public async Task<string?> ShowAsync(INavigation navigation) { _hostNavigation = navigation; await navigation.PushModalAsync(this, false); return await _completion.Task; }
    private void Press(string key, bool numeric)
    {
        if (key == "DEL")
        {
            if (_text.Length > 0) _text = _text[..^1];
            UpdateValue();
            return;
        }
        if (key == "⌫") { if (_text.Length > 0) _text = _text[..^1]; }
        else if (numeric && key == "." && _text.Contains('.')) { return; }
        else if (!numeric || _text.Length < 12) _text += key;
        UpdateValue();
    }
    private void UpdateValue() => _value.Text = DisplayText();
    private string DisplayText() => _isNumeric ? _text : string.IsNullOrEmpty(_text) ? "Tap the keys below" : _text;
    private static Button KeyButton(string text, double height) => new() { Text = text, HeightRequest = height, CornerRadius = 12, BackgroundColor = Colors.White, TextColor = Color.FromArgb("#202B3D"), BorderColor = Color.FromArgb("#D8E2F1"), BorderWidth = 1, FontSize = 17, FontAttributes = FontAttributes.Bold, Padding = 0 };
    private static Button NumericKeyButton(string text) => new() { Text = text == "DEL" ? "⌫" : text, HeightRequest = 58, CornerRadius = 17, BackgroundColor = text == "." ? Color.FromArgb("#FEE2E2") : text == "DEL" ? Color.FromArgb("#FEF3C7") : Color.FromArgb("#F1F5F9"), TextColor = text == "." ? Color.FromArgb("#DC2626") : text == "DEL" ? Color.FromArgb("#D97706") : Color.FromArgb("#202B3D"), BorderColor = Color.FromArgb("#D8E2F1"), BorderWidth = 1, FontSize = text == "DEL" ? 22 : 24, FontAttributes = FontAttributes.Bold };
    private async Task CloseAsync(string? result) { if (_closed) return; _closed = true; try { var navigation = _hostNavigation ?? Navigation; if (navigation.ModalStack.Contains(this)) await navigation.PopModalAsync(false); } finally { _completion.TrySetResult(result); } }
}

public sealed class CashDrawerConfirmDialogPage : ContentPage
{
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private INavigation? _hostNavigation;
    private bool _closed;
    public CashDrawerConfirmDialogPage(string reason)
    {
        BackgroundColor = Color.FromArgb("#99000000");
        var no = Button("No", "#E2E8F0", "#334155", false); var open = Button("Open", "#2563EB", "#FFFFFF", true);
        var buttons = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 18, Children = { no, open } }; Grid.SetColumn(open, 1);
        var content = new VerticalStackLayout { Spacing = 22, Children = { new Border { WidthRequest = 80, HeightRequest = 80, HorizontalOptions = LayoutOptions.Center, BackgroundColor = Color.FromArgb("#2563EB"), StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 40 }, Content = new Label { Text = "£", FontSize = 36, TextColor = Colors.White, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center } }, new Label { Text = "Open Cash Drawer", FontSize = 30, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#202B3D"), HorizontalTextAlignment = TextAlignment.Center }, new Label { Text = $"Open the cash drawer for: {reason}?", FontSize = 17, TextColor = Color.FromArgb("#687386"), HorizontalTextAlignment = TextAlignment.Center }, buttons } };
        var card = new Border { WidthRequest = 600, MaximumWidthRequest = 600, Padding = new Thickness(46, 38), BackgroundColor = Colors.White, Stroke = Color.FromArgb("#DDE4EE"), StrokeThickness = 1, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 28 }, Content = content };
        Content = new Grid { Padding = 24, Children = { new Grid { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Children = { card } } } };
    }
    public async Task<bool> ShowAsync(INavigation navigation) { _hostNavigation = navigation; await navigation.PushModalAsync(this, false); return await _completion.Task; }
    private Button Button(string text, string background, string foreground, bool result) { var button = new Button { Text = text, HeightRequest = 56, BackgroundColor = Color.FromArgb(background), TextColor = Color.FromArgb(foreground), FontSize = 18, FontAttributes = FontAttributes.Bold, CornerRadius = 16 }; button.Clicked += async (_, _) => await CloseAsync(result); return button; }
    private async Task CloseAsync(bool result) { if (_closed) return; _closed = true; try { var navigation = _hostNavigation ?? Navigation; if (navigation.ModalStack.Contains(this)) await navigation.PopModalAsync(false); } finally { _completion.TrySetResult(result); } }
}
