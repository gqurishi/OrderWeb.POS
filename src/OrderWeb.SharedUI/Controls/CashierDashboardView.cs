namespace OrderWeb.SharedUI.Controls;

/// <summary>
/// Shared, host-neutral Cashier dashboard content. Mother and Client provide
/// the data and command handlers; this control owns the consistent layout.
/// </summary>
public sealed class CashierDashboardView : ContentView
{
    private readonly Label[] _values = new Label[8];

    public event EventHandler? OpenDrawerRequested;
    public event EventHandler? PreviewZRequested;
    public event EventHandler? PrintZRequested;

    public CashierDashboardView()
    {
        var cards = new[] { "TOTAL ORDERS", "TOTAL SALES", "CASH TOTAL", "CARD TOTAL", "VOIDS", "DISCOUNTS", "EXPECTED CASH", "VARIANCE" };
        var cardGrid = new Grid { ColumnSpacing = 14, RowSpacing = 14, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) } };
        for (var i = 0; i < cards.Length; i++)
        {
            _values[i] = new Label { Text = "—", FontSize = 24, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#0F172A") };
            var card = new Border { Padding = 18, BackgroundColor = Colors.White, Stroke = Color.FromArgb("#DCE5F2"), StrokeThickness = 1, Content = new VerticalStackLayout { Spacing = 4, Children = { new Label { Text = cards[i], FontSize = 12, TextColor = Color.FromArgb("#64748B") }, _values[i] } } };
            cardGrid.Add(card, i % 4, i / 4);
        }

        var actions = new Grid { ColumnSpacing = 14, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        var drawer = new Button { Text = "Open Cash Drawer", HeightRequest = 64, BackgroundColor = Color.FromArgb("#0F766E"), TextColor = Colors.White };
        var preview = new Button { Text = "Z Report Preview", HeightRequest = 64, BackgroundColor = Color.FromArgb("#1D4ED8"), TextColor = Colors.White };
        var print = new Button { Text = "Print Z Report", HeightRequest = 64, BackgroundColor = Color.FromArgb("#7C3AED"), TextColor = Colors.White };
        drawer.Clicked += (_, e) => OpenDrawerRequested?.Invoke(this, e); preview.Clicked += (_, e) => PreviewZRequested?.Invoke(this, e); print.Clicked += (_, e) => PrintZRequested?.Invoke(this, e);
        actions.Add(drawer); actions.Add(preview, 1); actions.Add(print, 2);
        Content = new ScrollView { Content = new VerticalStackLayout { Padding = 24, Spacing = 20, Children = { new Label { Text = "Business date", FontSize = 15, TextColor = Color.FromArgb("#475569") }, cardGrid, new Label { Text = "Quick actions", FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#0F172A") }, actions } } };
    }

    public void SetSummary(params string[] values)
    {
        for (var i = 0; i < _values.Length && i < values.Length; i++) _values[i].Text = values[i];
    }
}
