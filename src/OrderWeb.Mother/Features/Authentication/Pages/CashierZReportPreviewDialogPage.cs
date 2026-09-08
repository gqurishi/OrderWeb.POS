using POS_in_NET.Models;

namespace POS_in_NET.Pages;

/// <summary>Read-only Z report presentation for the Mother cashier dashboard.</summary>
public sealed class CashierZReportPreviewDialogPage : ContentPage
{
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private INavigation? _hostNavigation;
    private bool _closed;

    public CashierZReportPreviewDialogPage(ZReportSnapshot report, bool canPrint)
    {
        BackgroundColor = Color.FromArgb("#990F172A");

        var close = ActionButton("Close", "#E2E8F0", "#334155");
        close.Clicked += async (_, _) => await CloseAsync(false);
        var print = ActionButton("Print Z Report", "#7C3AED", "#FFFFFF");
        print.IsEnabled = canPrint;
        print.Opacity = canPrint ? 1 : 0.45;
        print.Clicked += async (_, _) => await CloseAsync(true);
        var actions = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        actions.Add(close); actions.Add(print, 1);

        var payments = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        payments.Add(Metric("CASH", Money(report.CashTotal), "#ECFDF5", "#047857"));
        payments.Add(Metric("CARD", Money(report.CardTotal), "#EFF6FF", "#1D4ED8"), 1);
        payments.Add(Metric("OTHER", Money(report.GiftCardTotal), "#F5F3FF", "#6D28D9"), 2);

        var totals = new VerticalStackLayout
        {
            Spacing = 0,
            Children =
            {
                Row("Total orders", report.OrderCount.ToString()), Divider(),
                Row("Gross sales", Money(report.GrossSales), true), Divider(),
                Row("Voided orders", report.VoidCount.ToString()), Divider(),
                Row("Discounts", Money(report.DiscountTotal)), Divider(),
                Row("Expected cash", Money(report.ExpectedCashInDrawer), true), Divider(),
                Row("Counted cash", report.LastCashCountAmount is { } counted ? Money(counted) : "Not counted"), Divider(),
                Row("Variance", report.CashCountVariance is { } variance ? SignedMoney(variance) : "Not available", report.CashCountVariance is not null)
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
                new Label { Text = "PREVIEW — NOT A FINAL PRINT", FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#B45309"), HorizontalTextAlignment = TextAlignment.Center },
                Metadata(report), SectionTitle("Payment breakdown"), payments, SectionTitle("Report totals"),
                new Border
                {
                    BackgroundColor = Color.FromArgb("#F8FAFC"), Stroke = Color.FromArgb("#E2E8F0"), StrokeThickness = 1,
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 }, Padding = new Thickness(18, 4), Content = totals
                },
                new Label { Text = $"Report reference: {report.ReportReference}\nGenerated {report.GeneratedAt:dd MMM yyyy, HH:mm}", FontSize = 12, TextColor = Color.FromArgb("#64748B"), HorizontalTextAlignment = TextAlignment.Center },
                actions
            }
        };
        var cardGrid = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) } };
        cardGrid.Add(header); cardGrid.Add(new ScrollView { Content = body }, 0, 1);
        var card = new Border
        {
            WidthRequest = 650, MaximumWidthRequest = 650, MaximumHeightRequest = 820, BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#CBD5E1"), StrokeThickness = 1, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 24 }, Content = cardGrid
        };
        Content = new Grid { Padding = 22, Children = { new Grid { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Children = { card } } } };
    }

    public async Task<bool> ShowAsync(INavigation navigation) { _hostNavigation = navigation; await navigation.PushModalAsync(this, false); return await _completion.Task; }
    protected override bool OnBackButtonPressed() { _ = CloseAsync(false); return true; }
    private async Task CloseAsync(bool print)
    {
        if (_closed) return;
        _closed = true;
        try { var navigation = _hostNavigation ?? Navigation; if (navigation.ModalStack.Contains(this)) await navigation.PopModalAsync(false); }
        finally { _completion.TrySetResult(print); }
    }

    private static View Metadata(ZReportSnapshot report)
    {
        var grid = new Grid { ColumnSpacing = 20, RowSpacing = 8, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) } };
        grid.Add(Meta("BUSINESS DATE", report.ReportDate.ToString("dddd, dd MMMM yyyy")));
        grid.Add(Meta("TERMINAL", report.TerminalName), 1);
        grid.Add(Meta("GENERATED", report.GeneratedAt.ToString("HH:mm:ss")), 0, 1);
        grid.Add(Meta("REPORT TYPE", "Today's Z Report"), 1, 1);
        return grid;
    }
    private static View Meta(string label, string value) => new VerticalStackLayout { Spacing = 2, Children = { new Label { Text = label, FontSize = 10, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#64748B") }, new Label { Text = value, FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#1E293B"), LineBreakMode = LineBreakMode.TailTruncation } } };
    private static View Metric(string label, string value, string background, string foreground) => new Border { Padding = new Thickness(14, 12), BackgroundColor = Color.FromArgb(background), StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 }, Content = new VerticalStackLayout { Spacing = 3, Children = { new Label { Text = label, FontSize = 10, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb(foreground) }, new Label { Text = value, FontSize = 20, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#0F172A") } } } };
    private static View Row(string label, string value, bool strong = false) { var grid = new Grid { Padding = new Thickness(0, 11), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } }; grid.Add(new Label { Text = label, FontSize = 14, TextColor = Color.FromArgb("#475569") }); grid.Add(new Label { Text = value, FontSize = strong ? 17 : 14, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#0F172A") }, 1); return grid; }
    private static Label SectionTitle(string text) => new() { Text = text, FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#1E293B") };
    private static BoxView Divider() => new() { HeightRequest = 1, BackgroundColor = Color.FromArgb("#E2E8F0") };
    private static Button ActionButton(string text, string background, string foreground) => new() { Text = text, HeightRequest = 56, CornerRadius = 14, BackgroundColor = Color.FromArgb(background), TextColor = Color.FromArgb(foreground), FontSize = 16, FontAttributes = FontAttributes.Bold };
    private static string Money(decimal value) => $"£{value:N2}";
    private static string SignedMoney(decimal value) => value > 0 ? $"+£{value:N2}" : value < 0 ? $"-£{Math.Abs(value):N2}" : "£0.00";
}
