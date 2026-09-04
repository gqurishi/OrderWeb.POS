using OrderWeb.SharedUI.Controls;

namespace OrderWeb.Client.Views.Layout;

public partial class ClientSidebar : ContentView
{
    public static readonly BindableProperty RoleProperty = BindableProperty.Create(nameof(Role), typeof(string), typeof(ClientSidebar), "User", propertyChanged: OnStateChanged);
    public static readonly BindableProperty SelectedMenuProperty = BindableProperty.Create(nameof(SelectedMenu), typeof(string), typeof(ClientSidebar), "Dashboard", propertyChanged: OnStateChanged);
    public static readonly BindableProperty ShowFooterProperty = BindableProperty.Create(nameof(ShowFooter), typeof(bool), typeof(ClientSidebar), false, propertyChanged: OnStateChanged);

    public ClientSidebar()
    {
        InitializeComponent();
        ApplyState();
    }

    public event EventHandler<string>? MenuItemSelected;
    public event EventHandler? UpdateAllClicked;
    public string Role { get => (string)GetValue(RoleProperty); set => SetValue(RoleProperty, value); }
    public string SelectedMenu { get => (string)GetValue(SelectedMenuProperty); set => SetValue(SelectedMenuProperty, value); }
    public bool ShowFooter { get => (bool)GetValue(ShowFooterProperty); set => SetValue(ShowFooterProperty, value); }

    private static void OnStateChanged(BindableObject bindable, object oldValue, object newValue) => ((ClientSidebar)bindable).ApplyState();
    private void ApplyState()
    {
        if (SharedSidebar == null) return;
        SharedSidebar.CurrentRole = Role;
        SharedSidebar.SelectedRoute = RouteForTitle(SelectedMenu);
        SharedSidebar.ShowUpdateButton = ShowFooter;
        SharedSidebar.ItemsSource = MenuItems;
    }
    private void OnSharedNavigationRequested(object? sender, NavigationRequestedEventArgs e)
    {
        SelectedMenu = e.Item.Title;
        MenuItemSelected?.Invoke(this, e.Item.Title);
    }
    private void OnUpdateAllClicked(object? sender, EventArgs e) => UpdateAllClicked?.Invoke(this, EventArgs.Empty);

    private static readonly IReadOnlyList<ApplicationNavigationItem> MenuItems =
    [
        new("dashboard", "Dashboard", "dashboard.png", "User", "Manager", "Admin"),
        new("cashdrawer", "Cash Drawer", "giftcard.png", "Manager", "Admin"),
        new("liveorder", "Live Order", "liveorder.png", "User", "Manager", "Admin"),
        new("restaurant", "Restaurant", "restaurant.png", "User", "Manager", "Admin"),
        new("collection", "Collection", "collection.png", "User", "Manager", "Admin"),
        new("delivery", "Delivery", "delivery.png", "User", "Manager", "Admin"),
        new("weborders", "Web Orders", "weborders.png", "Manager", "Admin"),
        new("giftcards", "Gift Cards", "giftcards.png", "Manager", "Admin"),
        new("loyalty", "Loyalty Points", "loyalty.png", "Manager", "Admin"),
        new("reservation", "Reservation", "reservation.png", "User", "Manager", "Admin"),
        new("orderhistory", "Order History", "orderhistory.png", "Manager", "Admin")
    ];

    private static string RouteForTitle(string value) => MenuItems.FirstOrDefault(item => string.Equals(item.Title, value, StringComparison.OrdinalIgnoreCase))?.Route ?? "dashboard";
}
