namespace OrderWeb.Client.Dialogs;

/// <summary>POS-styled drawer-reason selector shared visually with Mother.</summary>
public sealed class CashDrawerReasonDialogPage : ContentPage
{
    private readonly TaskCompletionSource<string?> _completion = new();
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
        var card = new Border { WidthRequest = 520, MaximumWidthRequest = 520, Padding = new Thickness(28, 26), BackgroundColor = Colors.White, Stroke = Color.FromArgb("#DDE4EE"), StrokeThickness = 1, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 24 }, Content = new VerticalStackLayout { Spacing = 20, HorizontalOptions = LayoutOptions.Fill, Children = { icon, new Label { Text = "Cash Drawer Reason", FontSize = 24, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#202B3D"), HorizontalTextAlignment = TextAlignment.Center }, grid, cancel } } };
        Content = new Grid { Padding = 24, Children = { new Grid { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Children = { card } } } };
    }

    public async Task<string?> ShowAsync(INavigation navigation)
    {
        await navigation.PushModalAsync(this, false);
        return await _completion.Task;
    }

    private async Task CloseAsync(string? result)
    {
        if (_closed) return; _closed = true;
        _completion.TrySetResult(result);
        await Navigation.PopModalAsync(false);
    }
}
