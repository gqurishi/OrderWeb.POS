using Microsoft.Maui.Controls.Shapes;
using OrderWeb.Client.Models;

namespace OrderWeb.Client.Views.OnlineOrders;

public partial class OnlineOrderDetailsView : ContentView
{
    public OnlineOrderDetailsView()
    {
        InitializeComponent();
    }

    public event EventHandler<CachedOnlineOrder>? ViewClicked;
    public event EventHandler<CachedOnlineOrder>? AcceptClicked;
    public event EventHandler<CachedOnlineOrder>? PreparingClicked;
    public event EventHandler<CachedOnlineOrder>? ReadyClicked;
    public event EventHandler<CachedOnlineOrder>? CompleteClicked;

    public void SetOrders(IReadOnlyList<CachedOnlineOrder> orders)
    {
        OrdersStack.Children.Clear();
        if (orders.Count == 0)
        {
            OrdersStack.Children.Add(new Label
            {
                Text = "No live orders in local SQLite cache.",
                FontFamily = "OpenSansRegular",
                FontSize = 16,
                TextColor = Color.FromArgb("#64748B"),
                HorizontalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 80, 0, 0)
            });
            return;
        }

        foreach (var order in orders)
        {
            OrdersStack.Children.Add(BuildOrderCard(order));
        }
    }

    private View BuildOrderCard(CachedOnlineOrder order)
    {
        var statusColor = StatusColor(order.Status);
        var content = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(120),
                new ColumnDefinition(120),
                new ColumnDefinition(460)
            },
            ColumnSpacing = 14,
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 4,
                    Children =
                    {
                        new Label { Text = order.OrderNumber, FontFamily = "OpenSansBold", FontSize = 16, TextColor = Color.FromArgb("#1F2937") },
                        new Label { Text = order.CustomerName, FontFamily = "OpenSansSemibold", FontSize = 15, TextColor = Color.FromArgb("#1F2937") },
                        new Label { Text = $"{order.OrderType} · {order.Status}", FontFamily = "OpenSansRegular", FontSize = 13, TextColor = statusColor },
                    }
                },
                new Label { Text = order.DueTime, FontFamily = "OpenSansSemibold", FontSize = 14, TextColor = Color.FromArgb("#374151"), VerticalTextAlignment = TextAlignment.Center },
                new Label { Text = $"£{order.Total:F2}", FontFamily = "OpenSansBold", FontSize = 16, TextColor = Color.FromArgb("#1F2937"), VerticalTextAlignment = TextAlignment.Center },
                BuildActions(order)
            }
        };
        if (content.Children[1] is BindableObject dueTime)
        {
            dueTime.SetValue(Microsoft.Maui.Controls.Grid.ColumnProperty, 1);
        }

        if (content.Children[2] is BindableObject total)
        {
            total.SetValue(Microsoft.Maui.Controls.Grid.ColumnProperty, 2);
        }

        if (content.Children[3] is BindableObject actions)
        {
            actions.SetValue(Microsoft.Maui.Controls.Grid.ColumnProperty, 3);
        }

        return new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#E5E7EB"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(16, 14),
            Content = content
        };
    }

    private View BuildActions(CachedOnlineOrder order)
    {
        return new HorizontalStackLayout
        {
            Spacing = 8,
            HorizontalOptions = LayoutOptions.End,
            Children =
            {
                ActionButton("View", "#64748B", () => ViewClicked?.Invoke(this, order)),
                ActionButton("Accept", "#2563EB", () => AcceptClicked?.Invoke(this, order)),
                ActionButton("Preparing", "#F59E0B", () => PreparingClicked?.Invoke(this, order)),
                ActionButton("Ready", "#7C3AED", () => ReadyClicked?.Invoke(this, order)),
                ActionButton("Complete", "#64748B", () => CompleteClicked?.Invoke(this, order))
            }
        };
    }

    private static Button ActionButton(string text, string color, Action action)
    {
        var button = new Button
        {
            Text = text,
            BackgroundColor = Color.FromArgb(color),
            TextColor = Colors.White,
            FontFamily = "OpenSansSemibold",
            FontSize = 12,
            CornerRadius = 8,
            HeightRequest = 40,
            WidthRequest = text.Length > 7 ? 92 : 74,
            Padding = 0
        };
        button.Clicked += (_, _) => action();
        return button;
    }

    public static Color StatusColor(string status)
    {
        return Color.FromArgb(status switch
        {
            "New" => "#2563EB",
            "Accepted" => "#059669",
            "Preparing" => "#F59E0B",
            "Ready" => "#7C3AED",
            "Completed" or "Complete" => "#64748B",
            _ => "#64748B"
        });
    }
}
