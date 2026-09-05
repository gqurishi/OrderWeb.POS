using System.Collections;
using OrderWeb.Client.Services;
using OrderWeb.SharedUI.Controls;

namespace OrderWeb.Client.Views.Layout;

public partial class ClientSidebar : ContentView
{
    public static readonly BindableProperty RoleProperty = BindableProperty.Create(nameof(Role), typeof(string), typeof(ClientSidebar), "User", propertyChanged: OnStateChanged);
    public static readonly BindableProperty SelectedMenuProperty = BindableProperty.Create(nameof(SelectedMenu), typeof(string), typeof(ClientSidebar), "Dashboard", propertyChanged: OnStateChanged);
    public static readonly BindableProperty ShowFooterProperty = BindableProperty.Create(nameof(ShowFooter), typeof(bool), typeof(ClientSidebar), false, propertyChanged: OnStateChanged);
    public static readonly BindableProperty ConnectionStatusProperty = BindableProperty.Create(nameof(ConnectionStatus), typeof(string), typeof(ClientSidebar), "Connected", propertyChanged: OnStateChanged);
    public static readonly BindableProperty MenuItemsProperty = BindableProperty.Create(nameof(MenuItems), typeof(IEnumerable), typeof(ClientSidebar), null, propertyChanged: OnStateChanged);

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
    public string ConnectionStatus { get => (string)GetValue(ConnectionStatusProperty); set => SetValue(ConnectionStatusProperty, value); }
    public IEnumerable? MenuItems { get => (IEnumerable?)GetValue(MenuItemsProperty); set => SetValue(MenuItemsProperty, value); }

    private static void OnStateChanged(BindableObject bindable, object oldValue, object newValue) => ((ClientSidebar)bindable).ApplyState();

    private void ApplyState()
    {
        if (SharedSidebar == null) return;
        SharedSidebar.CurrentRole = Role;
        SharedSidebar.SelectedRoute = RouteForTitle(SelectedMenu);
        SharedSidebar.ShowUpdateButton = ShowFooter;
        SharedSidebar.ConnectionStatus = ConnectionStatus;
        SharedSidebar.ItemsSource = ResolveMenuItems();
    }

    private IEnumerable ResolveMenuItems()
    {
        if (MenuItems is not null)
        {
            return MenuItems;
        }

        return ClientNavigationService.BuildMenuItems(
            Role,
            permissions: null,
            ClientNavigationService.IsMotherConnected(ConnectionStatus));
    }

    private void OnSharedNavigationRequested(object? sender, NavigationRequestedEventArgs e)
    {
        SelectedMenu = e.Item.Title;
        MenuItemSelected?.Invoke(this, e.Item.Title);
    }

    private void OnUpdateAllClicked(object? sender, EventArgs e) => UpdateAllClicked?.Invoke(this, EventArgs.Empty);

    private string RouteForTitle(string value)
    {
        foreach (var item in ResolveMenuItems().OfType<ApplicationNavigationItem>())
        {
            if (string.Equals(item.Title, value, StringComparison.OrdinalIgnoreCase))
            {
                return item.Route;
            }
        }

        return "dashboard";
    }
}
