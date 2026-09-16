using Microsoft.Maui.Controls.Shapes;
using OrderWeb.SharedUI.Controls;

namespace OrderWeb.SharedUI.Views;

public enum BarInventoryActionKind
{
    Receive,
    Count,
    Waste,
    CurrentStock,
    SuggestedOrder,
    WeeklyReport
}

/// <summary>
/// Host-neutral Bar Inventory workspace for Mother and Client POS.
/// Presentation only — hosts own data, Mother hub, and authorization.
/// </summary>
public sealed class BarInventoryView : ContentView
{
    private readonly Label _statusLabel;
    private readonly Label _summaryLabel;
    private readonly VerticalStackLayout _stockList;

    public BarInventoryView()
    {
        _statusLabel = MutedLabel("Ready for stock work.", 14);
        _summaryLabel = StrongLabel("No stock lines yet", 22);
        _stockList = new VerticalStackLayout { Spacing = 8 };

        var actions = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12,
            RowSpacing = 12
        };

        actions.Add(ActionTile("Receive Stock", "Log bottles / cases in", "#ECFDF5", "#047857", BarInventoryActionKind.Receive), 0, 0);
        actions.Add(ActionTile("Stock Count", "Count before service", "#EFF6FF", "#1D4ED8", BarInventoryActionKind.Count), 1, 0);
        actions.Add(ActionTile("Waste / Breakage", "Record loss", "#FDF2F8", "#BE185D", BarInventoryActionKind.Waste), 2, 0);
        actions.Add(ActionTile("Current Stock", "On-hand by item", "#FFF7ED", "#C2410C", BarInventoryActionKind.CurrentStock), 0, 1);
        actions.Add(ActionTile("Suggested Order", "What to buy next", "#F0F9FF", "#0369A1", BarInventoryActionKind.SuggestedOrder), 1, 1);
        actions.Add(ActionTile("Weekly Report", "Usage & variance", "#F8FAFC", "#334155", BarInventoryActionKind.WeeklyReport), 2, 1);

        var header = new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                StrongLabel("Bar Inventory", 32),
                MutedLabel("Receive stock, count before service, log waste, and review what to order.", 15),
                _statusLabel
            }
        };

        var stockCard = SurfaceCard(
            "Stock board",
            "Live stock lines appear here once items are linked from Food Menu.",
            new VerticalStackLayout
            {
                Spacing = 12,
                Children =
                {
                    _summaryLabel,
                    _stockList,
                    InfoNote("Stock is shared across Mother and Client tills. Bar Manager can work from either POS when online to Mother.")
                }
            });

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(20, 16, 20, 24),
                Spacing = 16,
                Children =
                {
                    header,
                    actions,
                    stockCard
                }
            }
        };
    }

    public event EventHandler<BarInventoryActionKind>? ActionRequested;

    public void SetStatus(string message) => _statusLabel.Text = string.IsNullOrWhiteSpace(message)
        ? "Ready for stock work."
        : message.Trim();

    public void SetStockSummary(string title, IEnumerable<(string Title, string Detail)>? rows = null)
    {
        _summaryLabel.Text = string.IsNullOrWhiteSpace(title) ? "No stock lines yet" : title.Trim();
        _stockList.Children.Clear();
        if (rows is null)
        {
            return;
        }

        foreach (var row in rows)
        {
            _stockList.Children.Add(new Border
            {
                StrokeThickness = 1,
                Stroke = Color.FromArgb("#E2E8F0"),
                BackgroundColor = Colors.White,
                StrokeShape = new RoundRectangle { CornerRadius = 12 },
                Padding = new Thickness(14, 12),
                Content = new VerticalStackLayout
                {
                    Spacing = 2,
                    Children =
                    {
                        StrongLabel(row.Title, 15),
                        MutedLabel(row.Detail, 13)
                    }
                }
            });
        }
    }

    private View ActionTile(string title, string subtitle, string background, string accent, BarInventoryActionKind action)
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => ActionRequested?.Invoke(this, action);

        var border = new Border
        {
            StrokeThickness = 1,
            Stroke = Color.FromArgb("#E2E8F0"),
            BackgroundColor = Color.FromArgb(background),
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Padding = new Thickness(16, 14),
            Content = new VerticalStackLayout
            {
                Spacing = 4,
                Children =
                {
                    new Label
                    {
                        Text = title,
                        FontFamily = "OpenSansSemibold",
                        FontSize = 16,
                        TextColor = Color.FromArgb(accent)
                    },
                    MutedLabel(subtitle, 12)
                }
            }
        };
        border.GestureRecognizers.Add(tap);
        return border;
    }

    private static Border SurfaceCard(string title, string subtitle, View body) =>
        new()
        {
            StrokeThickness = 1,
            Stroke = Color.FromArgb("#D8E1ED"),
            BackgroundColor = Colors.White,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Padding = new Thickness(18),
            Content = new VerticalStackLayout
            {
                Spacing = 12,
                Children =
                {
                    StrongLabel(title, 20),
                    MutedLabel(subtitle, 13),
                    body
                }
            }
        };

    private static Border InfoNote(string text) =>
        new()
        {
            StrokeThickness = 1,
            Stroke = Color.FromArgb("#BFDBFE"),
            BackgroundColor = Color.FromArgb("#EFF6FF"),
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(12, 10),
            Content = MutedLabel(text, 13)
        };

    private static Label StrongLabel(string text, double size) =>
        new()
        {
            Text = text,
            FontFamily = "OpenSansSemibold",
            FontSize = size,
            TextColor = Color.FromArgb("#0F172A")
        };

    private static Label MutedLabel(string text, double size) =>
        new()
        {
            Text = text,
            FontFamily = "OpenSansRegular",
            FontSize = size,
            TextColor = Color.FromArgb("#64748B"),
            LineBreakMode = LineBreakMode.WordWrap
        };
}
