using OrderWeb.Client.Services;
using OrderWeb.SharedUI.Controls;
using OrderWeb.SharedUI.Navigation;

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
        Loaded += async (_, _) => await ApplyCurrentSessionAsync();
    }

    public event EventHandler<string>? MenuItemSelected;
    public event EventHandler? UpdateAllClicked;
    public string Role { get => (string)GetValue(RoleProperty); set => SetValue(RoleProperty, value); }
    public string SelectedMenu { get => (string)GetValue(SelectedMenuProperty); set => SetValue(SelectedMenuProperty, value); }
    public bool ShowFooter { get => (bool)GetValue(ShowFooterProperty); set => SetValue(ShowFooterProperty, value); }

    private static void OnStateChanged(BindableObject bindable, object oldValue, object newValue) => ((ClientSidebar)bindable).ApplyState();

    private async Task ApplyCurrentSessionAsync()
    {
        try
        {
            var session = await new ClientCacheService().GetCurrentLoginSessionAsync();
            if (string.IsNullOrWhiteSpace(session?.Role))
            {
                return;
            }

            Role = session.Role;
            // Match Mother: Update All is available for User and Manager on the POS sidebar.
            ShowFooter = string.Equals(session.Role, "Manager", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(session.Role, "User", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // Retain the declared state when no Client session has been saved yet.
        }
    }

    private void ApplyState()
    {
        if (SharedSidebar == null) return;
        SharedSidebar.CurrentRole = Role;
        SharedSidebar.SelectedRoute = RouteForTitle(SelectedMenu);
        SharedSidebar.ShowUpdateButton = ShowFooter;
        // Match Mother AppShell: set capabilities/features so Rebuild does not
        // hide every catalog item that declares RequiredCapabilities.
        var capabilities = ClientCapabilityResolver.ForRole(Role);
        var features = ClientHostAccess.FeaturesForRole(Role);
        var routes = ClientHostAccess.RoutesForRole(Role);
        SharedSidebar.AvailableCapabilities = capabilities;
        SharedSidebar.AvailableFeatures = features;
        SharedSidebar.ItemsSource = PosNavigationCatalog.Filter(capabilities, features, routes);
    }
    private void OnSharedNavigationRequested(object? sender, NavigationRequestedEventArgs e)
    {
        SelectedMenu = e.Item.Title;
        MenuItemSelected?.Invoke(this, e.Item.Title);
    }
    private void OnUpdateAllClicked(object? sender, EventArgs e) => UpdateAllClicked?.Invoke(this, EventArgs.Empty);

    private static string RouteForTitle(string value) => ClientHostAccess.RouteForTitle(value);
}
