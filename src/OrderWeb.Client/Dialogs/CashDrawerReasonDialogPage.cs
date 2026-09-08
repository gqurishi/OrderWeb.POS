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
            _details = new Entry { Placeholder = detailsPlaceholder, FontSize = 18, TextColor = Color.FromArgb("#202B3D"), PlaceholderColor = Color.FromArgb("#9AA6B8") };
            content.Children.Add(Field(null, _details));
        }
        if (!string.IsNullOrWhiteSpace(amountLabel))
        {
            _amount = new Entry { Placeholder = "0.00", Keyboard = Keyboard.Numeric, FontSize = 18, TextColor = Color.FromArgb("#202B3D"), PlaceholderColor = Color.FromArgb("#9AA6B8") };
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
    private static Border Field(string? label, View value) => new() { BackgroundColor = Color.FromArgb("#F8FAFC"), Stroke = Color.FromArgb("#D8E2F1"), StrokeThickness = 1, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 }, Padding = new Thickness(16, 6), Content = value };
    private static Border Icon(string accent, string text) => new() { WidthRequest = 80, HeightRequest = 80, HorizontalOptions = LayoutOptions.Center, BackgroundColor = Color.FromArgb(accent), StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 40 }, Content = new Label { Text = text, FontSize = 36, TextColor = Colors.White, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center } };
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
