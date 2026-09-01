using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using OrderWeb.Client.Models;
using OrderWeb.Client.Pages.Manager;
using OrderWeb.Client.Pages.Pos;
using OrderWeb.Client.Services;

namespace OrderWeb.Client.Pages.Orders;

public partial class LiveOrderPage : ContentPage
{
    private readonly ClientCacheService _cache = new();
    private readonly IDispatcherTimer _clockTimer;
    private string _selectedFilter = "All";
    private IReadOnlyList<LiveOrderCardModel> _allCards = Array.Empty<LiveOrderCardModel>();

    public LiveOrderPage()
    {
        InitializeComponent();
        Sidebar.MenuItemSelected += async (_, label) => await SelectSidebarItemAsync(label);

        _clockTimer = Dispatcher.CreateTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(1);
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();

        UpdateClock();
        ApplyFilterButtonStyles();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshOrdersAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _clockTimer.Stop();
    }

    private async Task RefreshOrdersAsync()
    {
        var openOrders = await _cache.GetOpenOrderStatesAsync();
        var onlineOrders = await _cache.GetOnlineOrdersAsync();
        _allCards = BuildLiveOrderCards(openOrders, onlineOrders);
        RenderCards();
    }

    private void RenderCards()
    {
        var cards = _allCards
            .Where(card => _selectedFilter == "All" || card.Type == _selectedFilter)
            .ToList();

        OrderCards.Children.Clear();
        foreach (var card in cards)
        {
            OrderCards.Children.Add(CreateOrderCard(card));
        }

        EmptyStateLabel.Text = _selectedFilter switch
        {
            "Collection" => "No unpaid collection orders",
            "Delivery" => "No unpaid delivery orders",
            "Table" => "No active table sessions",
            _ => "No open local orders"
        };
        EmptyStateLabel.IsVisible = cards.Count == 0;
    }

    private IReadOnlyList<LiveOrderCardModel> BuildLiveOrderCards(
        IReadOnlyList<MotherOrderState> openOrders,
        IReadOnlyList<CachedOnlineOrder> onlineOrders)
    {
        var cards = new List<LiveOrderCardModel>();

        foreach (var order in openOrders.Where(order => !string.Equals(order.Status, "Closed", StringComparison.OrdinalIgnoreCase)))
        {
            var type = NormalizeLiveOrderType(order.OrderType);
            var tableDisplay = !string.IsNullOrWhiteSpace(order.TableNumber) ? $"Table {order.TableNumber}" : "Table";
            var subtitle = type == "Table" ? tableDisplay : order.Status;
            var accent = type == "Table" ? "#DC2626" : "#10B981";

            cards.Add(new LiveOrderCardModel(
                Type: type,
                Title: type,
                OrderNumber: FormatLiveOrderNumber(order.OrderNumber, order.OrderId),
                Subtitle: subtitle,
                Total: order.Total,
                TimeText: FormatLiveOrderTime(order.UpdatedUtc),
                AccentColor: accent,
                Details: $"{subtitle}\nStatus: {order.Status}\nGuests: {order.Guests}\nTotal: {Money(order.Total)}"));
        }

        foreach (var order in onlineOrders.Where(order => !string.Equals(order.Status, "Completed", StringComparison.OrdinalIgnoreCase)))
        {
            var type = NormalizeLiveOrderType(order.OrderType);
            cards.Add(new LiveOrderCardModel(
                Type: type,
                Title: type,
                OrderNumber: FormatLiveOrderNumber(order.OrderNumber, order.MotherId),
                Subtitle: string.IsNullOrWhiteSpace(order.CustomerName) ? order.Status : order.CustomerName,
                Total: order.Total,
                TimeText: FormatLiveOrderTime(order.DueTime),
                AccentColor: type == "Delivery" ? "#2563EB" : "#10B981",
                Details: $"{order.CustomerName}\n{order.OrderType} - {order.Status}\nDue {order.DueTime}\nTotal: {Money(order.Total)}"));
        }

        return cards
            .OrderByDescending(card => card.Type == "Table")
            .ThenBy(card => card.TimeText)
            .ToList();
    }

    private Border CreateOrderCard(LiveOrderCardModel card)
    {
        var accent = Color.FromArgb(card.AccentColor);
        var border = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = accent,
            StrokeThickness = 2,
            Padding = new Thickness(16, 14),
            Margin = new Thickness(0, 0, 22, 22),
            WidthRequest = 220,
            MinimumHeightRequest = 132,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Shadow = new Shadow
            {
                Brush = Colors.Black,
                Offset = new Point(0, 2),
                Radius = 8,
                Opacity = 0.08f
            }
        };

        border.Content = new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                new Label
                {
                    Text = card.Title,
                    FontSize = 22,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#1E293B"),
                    LineBreakMode = LineBreakMode.TailTruncation
                },
                new Label
                {
                    Text = card.OrderNumber,
                    FontSize = 12,
                    TextColor = Color.FromArgb("#94A3B8"),
                    LineBreakMode = LineBreakMode.TailTruncation
                },
                new Label
                {
                    Text = card.Subtitle,
                    FontSize = 15,
                    TextColor = Color.FromArgb("#475569"),
                    LineBreakMode = LineBreakMode.TailTruncation,
                    MaxLines = 2,
                    Margin = new Thickness(0, 2, 0, 0)
                },
                new Label
                {
                    Text = Money(card.Total),
                    FontSize = 24,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = accent,
                    Margin = new Thickness(0, 6, 0, 0)
                },
                new Label
                {
                    Text = card.TimeText,
                    FontSize = 12,
                    TextColor = Color.FromArgb("#94A3B8")
                }
            }
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await DisplayAlert(card.OrderNumber, card.Details, "OK");
        border.GestureRecognizers.Add(tap);
        return border;
    }

    private void ApplyFilterButtonStyles()
    {
        StyleFilterButton(AllFilterButton);
        StyleFilterButton(CollectionFilterButton);
        StyleFilterButton(DeliveryFilterButton);
        StyleFilterButton(TableFilterButton);
    }

    private void StyleFilterButton(Button button)
    {
        var selected = NormalizeLiveOrderType(button.Text) == _selectedFilter;
        button.FontSize = 15;
        button.FontAttributes = FontAttributes.Bold;
        button.TextColor = selected ? Colors.White : Color.FromArgb("#64748B");
        button.BackgroundColor = selected ? Color.FromArgb("#10B981") : Color.FromArgb("#F5F5F5");
        button.CornerRadius = 8;
        button.HeightRequest = 50;
        button.WidthRequest = button.Text == "All" ? 84 : 132;
        button.Padding = new Thickness(0);
        button.BorderWidth = 0;
    }

    private async void OnFilterClicked(object sender, EventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        _selectedFilter = NormalizeLiveOrderType(button.Text);
        ApplyFilterButtonStyles();
        RenderCards();
        await Task.CompletedTask;
    }

    private async void OnRefreshClicked(object sender, EventArgs e) => await RefreshOrdersAsync();

    private async void OnMenuClicked(object sender, EventArgs e) => await OpenSidebarAsync();

    private async void OnLogoutClicked(object sender, EventArgs e) => await Navigation.PopToRootAsync(false);

    private async void OnBackdropTapped(object sender, TappedEventArgs e) => await CloseSidebarAsync();

    private void UpdateClock()
    {
        var now = DateTime.Now;
        DateLabel.Text = now.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture);
        TimeLabel.Text = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private async Task SelectSidebarItemAsync(string label)
    {
        await CloseSidebarAsync();
        if (label == "Live Order")
        {
            return;
        }

        await Navigation.PushAsync(label switch
        {
            "Dashboard" => new Pages.Dashboards.ManagerDashboardPage(),
            "Cash Drawer" => new CashDrawerPage(),
            "Restaurant" => new TableLayoutPage(),
            "Collection" => new CollectionOrderPage(),
            "Delivery" => new DeliveryOrderPage(),
            "Web Orders" => new OnlineOrdersPage(),
            "Gift Cards" => new GiftCardPage(),
            "Loyalty Points" => new LoyaltyPage(),
            "Reservation" => new ReservationPage(),
            "Order History" => new OrderHistoryPage(),
            _ => new LiveOrderPage()
        }, false);
    }

    private async Task OpenSidebarAsync()
    {
        SidebarLayer.IsVisible = true;
        Sidebar.TranslationX = -280;
        await Sidebar.TranslateTo(0, 0, 180, Easing.CubicOut);
    }

    private async Task CloseSidebarAsync()
    {
        await Sidebar.TranslateTo(-280, 0, 160, Easing.CubicIn);
        SidebarLayer.IsVisible = false;
    }

    private static string NormalizeLiveOrderType(string? orderType)
    {
        return (orderType ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "all" => "All",
            "delivery" or "del" => "Delivery",
            "table" or "tbl" or "dine_in" or "dine-in" => "Table",
            _ => "Collection"
        };
    }

    private static string FormatLiveOrderNumber(string? orderNumber, string? fallbackId)
    {
        var value = !string.IsNullOrWhiteSpace(orderNumber) ? orderNumber.Trim() : fallbackId?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Order";
        }

        return value.StartsWith("#", StringComparison.Ordinal) ? value : $"#{value}";
    }

    private static string FormatLiveOrderTime(string? value)
    {
        if (DateTimeOffset.TryParse(value, out var timestamp))
        {
            return timestamp.ToLocalTime().ToString("HH:mm - dd/MM", CultureInfo.InvariantCulture);
        }

        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static string Money(decimal amount) => string.Create(CultureInfo.InvariantCulture, $"£{amount:0.00}");

    private sealed record LiveOrderCardModel(
        string Type,
        string Title,
        string OrderNumber,
        string Subtitle,
        decimal Total,
        string TimeText,
        string AccentColor,
        string Details);
}
