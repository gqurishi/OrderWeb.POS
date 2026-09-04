using Microsoft.Maui.Controls.Shapes;

namespace OrderWeb.Client.Views.Orders;

public partial class OrderSummaryView : ContentView
{
    public OrderSummaryView()
    {
        InitializeComponent();
    }

    public event EventHandler? SendClicked;
    public event EventHandler? PaymentClicked;
    public event EventHandler? ServiceClicked;
    public event EventHandler? NotesClicked;
    public event EventHandler? VoidClicked;
    public event EventHandler? MoreClicked;
    public event EventHandler? PrintClicked;

    public void SetOrder(string title, string detail, IEnumerable<OrderSummaryLine> lines)
    {
        var lineList = lines.ToList();
        var subtotal = lineList.Sum(line => line.Quantity * line.UnitPrice);
        var vat = subtotal * 0.2m;
	        var total = subtotal + vat;
	
	        TitleLabel.Text = title;
	        DetailLabel.Text = detail;
        TotalLabel.Text = $"£{total:F2}";

        LinesStack.Children.Clear();
        if (lineList.Count == 0)
        {
            LinesStack.Children.Add(new Label
            {
                Text = "No items added yet.",
                FontFamily = "OpenSansRegular",
                FontSize = 14,
                TextColor = Color.FromArgb("#64748B"),
                HorizontalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 70, 0, 0)
            });
            return;
        }

        foreach (var line in lineList)
        {
            LinesStack.Children.Add(BuildLine(line));
        }
    }

    private static View BuildLine(OrderSummaryLine line)
    {
        var meta = string.Join(" · ", new[] { line.Modifiers, line.Note }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var detailStack = new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                new Label { Text = $"{line.Quantity} x {line.Name}", FontFamily = "OpenSansSemibold", FontSize = 15, TextColor = Color.FromArgb("#1F2937") },
                new Label { Text = meta, FontFamily = "OpenSansRegular", FontSize = 12, TextColor = Color.FromArgb("#64748B") }
            }
        };
        var priceLabel = new Label
        {
            Text = $"£{line.Quantity * line.UnitPrice:F2}",
            FontFamily = "OpenSansSemibold",
            FontSize = 15,
            TextColor = Color.FromArgb("#1F2937"),
            HorizontalTextAlignment = TextAlignment.End,
            VerticalTextAlignment = TextAlignment.Center
        };
        Grid.SetColumn(priceLabel, 1);

        return new Border
        {
            BackgroundColor = Colors.White,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(12, 10),
            Content = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(78)
                },
                Children =
                {
                    detailStack,
                    priceLabel
                }
            }
        };
    }

    private void OnSendClicked(object sender, EventArgs e) => SendClicked?.Invoke(this, EventArgs.Empty);
    private void OnPaymentClicked(object sender, EventArgs e) => PaymentClicked?.Invoke(this, EventArgs.Empty);
    private void OnServiceClicked(object sender, EventArgs e) => ServiceClicked?.Invoke(this, EventArgs.Empty);
    private void OnNotesClicked(object sender, EventArgs e) => NotesClicked?.Invoke(this, EventArgs.Empty);
    private void OnVoidClicked(object sender, EventArgs e) => VoidClicked?.Invoke(this, EventArgs.Empty);
    private void OnMoreClicked(object sender, EventArgs e) => MoreClicked?.Invoke(this, EventArgs.Empty);
    private void OnPrintClicked(object sender, EventArgs e) => PrintClicked?.Invoke(this, EventArgs.Empty);
}

public sealed record OrderSummaryLine(string Name, int Quantity, decimal UnitPrice, string? Modifiers = null, string? Note = null);
