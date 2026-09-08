namespace OrderWeb.Client.Dialogs;

/// <summary>Touch-friendly confirmation before requesting an official Z print from Mother POS.</summary>
public sealed class PrintZReportConfirmDialogPage : ContentPage
{
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private INavigation? _hostNavigation;
    private bool _closed;

    public PrintZReportConfirmDialogPage()
    {
        BackgroundColor = Color.FromArgb("#990F172A");

        var cancel = CreateButton("Cancel", "#E2E8F0", "#334155");
        cancel.Clicked += async (_, _) => await CloseAsync(false);
        var print = CreateButton("Print Z Report", "#7C3AED", "#FFFFFF");
        print.Clicked += async (_, _) => await CloseAsync(true);

        var actions = new Grid
        {
            ColumnSpacing = 14,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }
        };
        actions.Add(cancel);
        actions.Add(print, 1);

        var routeText = new Label
        {
            Text = "Mother POS will create the official report, send it to the configured receipt printer, and record the print in audit history.",
            FontSize = 14, TextColor = Color.FromArgb("#4C1D95"), LineBreakMode = LineBreakMode.WordWrap, VerticalTextAlignment = TextAlignment.Center
        };
        var routeGrid = new Grid
        {
            ColumnSpacing = 12,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }
        };
        routeGrid.Add(new Border
        {
            WidthRequest = 34, HeightRequest = 34, BackgroundColor = Color.FromArgb("#EDE9FE"), StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 17 },
            Content = new Label { Text = "i", FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#6D28D9"), HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center }
        });
        routeGrid.Add(routeText, 1);

        var routeNotice = new Border
        {
            BackgroundColor = Color.FromArgb("#F5F3FF"),
            Stroke = Color.FromArgb("#DDD6FE"),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 15 },
            Padding = new Thickness(16, 13),
            Content = routeGrid
        };

        var card = new Border
        {
            WidthRequest = 560,
            MaximumWidthRequest = 560,
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#CBD5E1"),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 24 },
            Padding = new Thickness(34, 30),
            Content = new VerticalStackLayout
            {
                Spacing = 20,
                Children =
                {
                    new Border
                    {
                        WidthRequest = 78, HeightRequest = 78, HorizontalOptions = LayoutOptions.Center,
                        BackgroundColor = Color.FromArgb("#7C3AED"), StrokeThickness = 0,
                        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 39 },
                        Content = new Label { Text = "Z", FontSize = 31, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center }
                    },
                    new Label { Text = "Print Z Report", FontSize = 28, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#172033"), HorizontalTextAlignment = TextAlignment.Center },
                    new Label
                    {
                        Text = "Send today's official Z Report to the Mother-configured printer?",
                        FontSize = 17, TextColor = Color.FromArgb("#64748B"), HorizontalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.WordWrap
                    },
                    routeNotice,
                    actions
                }
            }
        };

        Content = new Grid
        {
            Padding = new Thickness(22),
            Children = { new Grid { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Children = { card } } }
        };
    }

    public async Task<bool> ShowAsync(INavigation navigation)
    {
        _hostNavigation = navigation;
        await navigation.PushModalAsync(this, false);
        return await _completion.Task;
    }

    protected override bool OnBackButtonPressed() { _ = CloseAsync(false); return true; }

    private async Task CloseAsync(bool confirmed)
    {
        if (_closed) return;
        _closed = true;
        try
        {
            var navigation = _hostNavigation ?? Navigation;
            if (navigation.ModalStack.Contains(this)) await navigation.PopModalAsync(false);
        }
        finally { _completion.TrySetResult(confirmed); }
    }

    private static Button CreateButton(string text, string background, string foreground) => new()
    {
        Text = text,
        HeightRequest = 58,
        CornerRadius = 15,
        BackgroundColor = Color.FromArgb(background),
        TextColor = Color.FromArgb(foreground),
        FontSize = 16,
        FontAttributes = FontAttributes.Bold
    };
}
