using OrderWeb.Client.Services;

namespace OrderWeb.Client.Dialogs;

/// <summary>Read-only presentation of the authoritative Z preview returned by Mother POS.</summary>
public sealed class ZReportPreviewDialogPage : ContentPage
{
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private INavigation? _hostNavigation;
    private bool _closed;

    public ZReportPreviewDialogPage(CashierZReportPreview preview, bool canPrint)
    {
        BackgroundColor = Color.FromArgb("#990F172A");

        var close = ActionButton("Close", "#E2E8F0", "#334155");
        close.Clicked += async (_, _) => await CloseAsync(false);
        var print = ActionButton("Print Z Report", "#7C3AED", "#FFFFFF");
        print.IsEnabled = canPrint;
        print.Opacity = canPrint ? 1 : 0.45;
        print.Clicked += async (_, _) => await CloseAsync(true);

        var actions = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        actions.Add(close);
        actions.Add(print, 1);

        var paymentGrid = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        paymentGrid.Add(MetricCard("CASH", Money(preview.CashTotal), "#ECFDF5", "#047857"));
        paymentGrid.Add(MetricCard("CARD", Money(preview.CardTotal), "#EFF6FF", "#1D4ED8"), 1);
        paymentGrid.Add(MetricCard("OTHER", Money(preview.OtherPaymentTotal), "#F5F3FF", "#6D28D9"), 2);

        var reportRows = new VerticalStackLayout
        {
            Spacing = 0,
            Children =
            {
                ReportRow("Total orders", preview.TotalOrders.ToString()), Divider(),
                ReportRow("Gross sales", Money(preview.GrossSales), true), Divider(),
                ReportRow("Voided orders", preview.VoidCount.ToString()), Divider(),
                ReportRow("Discounts", Money(preview.DiscountTotal)), Divider(),
                ReportRow("Expected cash", Money(preview.ExpectedCash), true), Divider(),
                ReportRow("Counted cash", preview.CountedCash is { } counted ? Money(counted) : "Not counted"), Divider(),
                ReportRow("Variance", preview.Variance is { } variance ? SignedMoney(variance) : "Not available", preview.Variance is not null)
            }
        };

        var header = new Grid { Padding = new Thickness(26, 22), BackgroundColor = Color.FromArgb("#172554"), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        header.Add(new VerticalStackLayout
        {
            Spacing = 5,
            Children =
            {
                new Label { Text = "Z REPORT", FontSize = 25, FontAttributes = FontAttributes.Bold, TextColor = Colors.White },
                new Label { Text = "Daily financial summary", FontSize = 14, TextColor = Color.FromArgb("#BFDBFE") }
            }
        });
        header.Add(new Border
        {
            Padding = new Thickness(14, 7), BackgroundColor = Color.FromArgb("#FEF3C7"), StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
            Content = new Label { Text = "PREVIEW ONLY", FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#92400E") }
        }, 1);

        var body = new VerticalStackLayout
        {
            Padding = new Thickness(26, 20), Spacing = 18,
            Children =
            {
                new Label { Text = "PREVIEW \u2014 NOT A FINAL PRINT", FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#B45309"), HorizontalTextAlignment = TextAlignment.Center },
                Metadata(preview), SectionTitle("Payment breakdown"), paymentGrid, SectionTitle("Report totals"),
                new Border
                {
                    BackgroundColor = Color.FromArgb("#F8FAFC"), Stroke = Color.FromArgb("#E2E8F0"), StrokeThickness = 1,
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 }, Padding = new Thickness(18, 4), Content = reportRows
                },
                new Label
                {
                    Text = $"Report reference: {preview.ReportReference}\nGenerated {preview.GeneratedUtc.LocalDateTime:dd MMM yyyy, HH:mm}",
                    FontSize = 12, TextColor = Color.FromArgb("#64748B"), HorizontalTextAlignment = TextAlignment.Center
                },
                actions
            }
        };

        var cardLayout = new Grid
        {
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) }
        };
        cardLayout.Add(header);
        cardLayout.Add(new ScrollView { Content = body }, 0, 1);

        var card = new Border
        {
            WidthRequest = 650, MaximumWidthRequest = 650, MaximumHeightRequest = 820, BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#CBD5E1"), StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 24 },
            Content = cardLayout
        };
        Content = new Grid { Padding = new Thickness(22), Children = { new Grid { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Children = { card } } } };
    }

    public async Task<bool> ShowAsync(INavigation navigation)
    {
        _hostNavigation = navigation;
        await navigation.PushModalAsync(this, false);
        return await _completion.Task;
    }

    protected override bool OnBackButtonPressed() { _ = CloseAsync(false); return true; }

    private async Task CloseAsync(bool printRequested)
    {
        if (_closed) return;
        _closed = true;
        try
        {
            var navigation = _hostNavigation ?? Navigation;
            if (navigation.ModalStack.Contains(this)) await navigation.PopModalAsync(false);
        }
        finally { _completion.TrySetResult(printRequested); }
    }

    private static View Metadata(CashierZReportPreview preview)
    {
        var grid = new Grid
        {
            ColumnSpacing = 20, RowSpacing = 8,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) }
        };
        grid.Add(MetadataItem("BUSINESS DATE", preview.BusinessDate.ToString("dddd, dd MMMM yyyy")));
        grid.Add(MetadataItem("TERMINAL", preview.TerminalName), 1);
        grid.Add(MetadataItem("GENERATED", preview.GeneratedUtc.LocalDateTime.ToString("HH:mm:ss")), 0, 1);
        grid.Add(MetadataItem("REPORT TYPE", "Today's Z Report"), 1, 1);
        return grid;
    }

    private static View MetadataItem(string label, string value) => new VerticalStackLayout
    {
        Spacing = 2,
        Children =
        {
            new Label { Text = label, FontSize = 10, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#64748B") },
            new Label { Text = value, FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#1E293B"), LineBreakMode = LineBreakMode.TailTruncation }
        }
    };

    private static View MetricCard(string label, string value, string background, string foreground) => new Border
    {
        Padding = new Thickness(14, 12), BackgroundColor = Color.FromArgb(background), StrokeThickness = 0,
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
        Content = new VerticalStackLayout
        {
            Spacing = 3,
            Children =
            {
                new Label { Text = label, FontSize = 10, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb(foreground) },
                new Label { Text = value, FontSize = 20, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#0F172A") }
            }
        }
    };

    private static View ReportRow(string label, string value, bool emphasize = false)
    {
        var grid = new Grid { Padding = new Thickness(0, 11), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        grid.Add(new Label { Text = label, FontSize = 14, TextColor = Color.FromArgb("#475569") });
        grid.Add(new Label { Text = value, FontSize = emphasize ? 17 : 14, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#0F172A") }, 1);
        return grid;
    }

    private static Label SectionTitle(string text) => new() { Text = text, FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#1E293B") };
    private static BoxView Divider() => new() { HeightRequest = 1, BackgroundColor = Color.FromArgb("#E2E8F0") };
    private static Button ActionButton(string text, string background, string foreground) => new() { Text = text, HeightRequest = 56, CornerRadius = 14, BackgroundColor = Color.FromArgb(background), TextColor = Color.FromArgb(foreground), FontSize = 16, FontAttributes = FontAttributes.Bold };
    private static string Money(decimal value) => $"\u00A3{value:N2}";
    private static string SignedMoney(decimal value) => value > 0 ? $"+\u00A3{value:N2}" : value < 0 ? $"-\u00A3{Math.Abs(value):N2}" : "\u00A30.00";
}
