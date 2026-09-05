namespace OrderWeb.Client.Pages.Manager;

using OrderWeb.Client.Pages.Orders;
using OrderWeb.Client.Pages.Pos;
using OrderWeb.Client.Services;
using Microsoft.Maui.Controls.Shapes;

public partial class OrderHistoryPage : ContentPage
{
    private readonly ClientCacheService _cache = new();
    private readonly MotherPrintClient _printClient;

    public OrderHistoryPage()
    {
        InitializeComponent();
        _printClient = new MotherPrintClient(_cache);
        LoadEmptyState();
        TopBar.MenuClicked += async (_, _) => await OpenSidebarAsync();
        TopBar.LogoutClicked += async (_, _) => await Navigation.PopToRootAsync(false);
        Sidebar.MenuItemSelected += async (_, menu) => await NavigateFromSidebarAsync(menu);
    }

    private async void OnFilterClicked(object sender, EventArgs e) => await DisplayAlert("Order History", "Today filter applied.", "OK");
    private async void OnSearchClicked(object sender, EventArgs e) => await DisplayAlert("Order History", "Order search complete.", "OK");
    private async void OnViewClicked(object sender, EventArgs e) => await DisplayAlert("Order History", "Order details opened.", "OK");
    private async void OnReprintClicked(object sender, EventArgs e) => await ReprintAsync();
    private async void OnBackdropTapped(object sender, TappedEventArgs e) => await CloseSidebarAsync();

    private async Task ReprintAsync()
    {
        var orderId = await DisplayPromptAsync(
            "Reprint",
            "Enter the Mother order id to reprint.",
            "Send to Mother",
            "Cancel",
            "order-id");
        if (string.IsNullOrWhiteSpace(orderId))
            return;

        var session = await _cache.GetCurrentLoginSessionAsync();
        // Mother is authoritative — display only Mother's reprint response.
        var request = await _printClient.RequestPrintAsync("reprint", orderId.Trim(), session, isReprint: true);
        await _cache.SavePrintRequestAsync(request);
        await DisplayAlert("Reprint", $"{request.Status}: {request.Message}", "OK");
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

    private async Task NavigateFromSidebarAsync(string menu)
    {
        await CloseSidebarAsync();
        await Navigation.PushAsync(menu switch
        {
            "Cash Drawer" => new CashDrawerPage(),
            "Restaurant" => new TableLayoutPage(),
            "Collection" => new CollectionOrderPage(),
            "Delivery" => new DeliveryOrderPage(),
            "Live Order" => new LiveOrderPage(),
            "Web Orders" => new OnlineOrdersPage(),
            "Gift Cards" => new GiftCardPage(),
            "Loyalty Points" => new LoyaltyPage(),
            "Reservation" => new ReservationPage(),
            _ => new OrderHistoryPage()
        }, false);
    }

    private void LoadEmptyState()
    {
        OrderHistoryStack.Children.Clear();
        OrderHistoryStack.Children.Add(new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#E5E7EB"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = 16,
            Content = new Label
            {
                Text = "No order history loaded. Connect to Mother POS.",
                FontFamily = "OpenSansRegular",
                FontSize = 14,
                TextColor = Color.FromArgb("#64748B")
            }
        });
    }

    private static Button HistoryButton(string text, string color, Func<Task> action)
    {
        var button = new Button
        {
            Text = text,
            BackgroundColor = Color.FromArgb(color),
            TextColor = Colors.White,
            FontFamily = "OpenSansSemibold",
            FontSize = 12,
            CornerRadius = 8,
            WidthRequest = text == "Reprint" ? 82 : 74,
            HeightRequest = 40
        };
        button.Clicked += async (_, _) => await action();
        return button;
    }
}
