using Microsoft.Maui.Controls.Shapes;

namespace OrderWeb.Client.Views.Orders;

public partial class OrderSummaryView : ContentView
{
    public OrderSummaryView()
    {
        InitializeComponent();
        // Mother order page never shows a SERVICE quick-action button.
        SetShowServiceButton(false);
    }

    public event EventHandler? SendClicked;
    public event EventHandler? PaymentClicked;
    public event EventHandler? ServiceClicked;
    public event EventHandler? NotesClicked;
    public event EventHandler? VoidClicked;
    public event EventHandler? MoreClicked;
    public event EventHandler? PrintClicked;
    public event EventHandler<OrderSummaryLine>? LineClicked;

    /// <summary>Mother uses NOTES / VOID / MORE only for all order types.</summary>
    public void SetShowServiceButton(bool show)
    {
        ServiceButton.IsVisible = show;
        if (show)
        {
            QuickActionsGrid.ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(GridLength.Star),
                new(GridLength.Star),
                new(GridLength.Star),
                new(GridLength.Star)
            };
            Grid.SetColumn(ServiceButton, 0);
            Grid.SetColumn(NotesButton, 1);
            Grid.SetColumn(VoidButton, 2);
            Grid.SetColumn(MoreButton, 3);
            return;
        }

        QuickActionsGrid.ColumnDefinitions = new ColumnDefinitionCollection
        {
            new(GridLength.Star),
            new(GridLength.Star),
            new(GridLength.Star)
        };
        Grid.SetColumn(NotesButton, 0);
        Grid.SetColumn(VoidButton, 1);
        Grid.SetColumn(MoreButton, 2);
    }

    public void SetOrder(
        string title,
        string detail,
        IEnumerable<OrderSummaryLine> lines,
        decimal? subtotal = null,
        decimal? total = null,
        bool showServiceChargeRow = false,
        string serviceChargeDescription = "Service charge",
        string serviceChargeValue = "Not included")
    {
        var lineList = lines.ToList();
        var computedSubtotal = subtotal ?? lineList.Sum(line => line.Quantity * line.UnitPrice);
        var computedTotal = total ?? computedSubtotal;

        TitleLabel.Text = title;
        DetailLabel.Text = detail;
        SubtotalLabel.Text = $"£{computedSubtotal:F2}";
        TotalLabel.Text = $"£{computedTotal:F2}";

        ServiceChargeRow.IsVisible = showServiceChargeRow;
        ServiceChargeDescriptionLabel.Text = serviceChargeDescription;
        ServiceChargeLabel.Text = serviceChargeValue;

        LinesStack.Children.Clear();
        if (lineList.Count == 0)
        {
            LinesStack.Children.Add(new Label
            {
                Text = string.Empty,
                HeightRequest = 1
            });
            return;
        }

        foreach (var line in lineList)
        {
            LinesStack.Children.Add(BuildLine(line));
        }
    }

    private View BuildLine(OrderSummaryLine line)
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

        var border = new Border
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

        if (!string.IsNullOrWhiteSpace(line.LineId))
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => LineClicked?.Invoke(this, line);
            border.GestureRecognizers.Add(tap);
        }

        return border;
    }

    private void OnSendClicked(object sender, EventArgs e) => SendClicked?.Invoke(this, EventArgs.Empty);
    private void OnPaymentClicked(object sender, EventArgs e) => PaymentClicked?.Invoke(this, EventArgs.Empty);
    private void OnServiceClicked(object sender, EventArgs e) => ServiceClicked?.Invoke(this, EventArgs.Empty);
    private void OnNotesClicked(object sender, EventArgs e) => NotesClicked?.Invoke(this, EventArgs.Empty);
    private void OnVoidClicked(object sender, EventArgs e) => VoidClicked?.Invoke(this, EventArgs.Empty);
    private void OnMoreClicked(object sender, EventArgs e) => MoreClicked?.Invoke(this, EventArgs.Empty);
    private void OnPrintClicked(object sender, EventArgs e) => PrintClicked?.Invoke(this, EventArgs.Empty);
}

public sealed record OrderSummaryLine(
    string Name,
    int Quantity,
    decimal UnitPrice,
    string? Modifiers = null,
    string? Note = null,
    string? LineId = null);
