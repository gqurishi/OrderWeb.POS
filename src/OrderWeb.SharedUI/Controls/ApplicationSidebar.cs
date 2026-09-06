using System.Collections;

namespace OrderWeb.SharedUI.Controls;

public class ApplicationSidebar : ContentView
{
    private readonly VerticalStackLayout _items;
    private readonly UserTerminalInfo _identity;
    private readonly ConnectionIndicator _connection;
    private readonly SharedButton _update;
    private readonly Label _role;
    private readonly Image _logo;
    private readonly Label _brand;

    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(nameof(ItemsSource), typeof(IEnumerable), typeof(ApplicationSidebar), propertyChanged: (b, _, _) => ((ApplicationSidebar)b).Rebuild());
    public static readonly BindableProperty CurrentRoleProperty = BindableProperty.Create(nameof(CurrentRole), typeof(string), typeof(ApplicationSidebar), string.Empty, propertyChanged: (b, _, v) => { var c = (ApplicationSidebar)b; c._role.Text = v?.ToString(); c.Rebuild(); });
    public static readonly BindableProperty RestaurantNameProperty = BindableProperty.Create(nameof(RestaurantName), typeof(string), typeof(ApplicationSidebar), "Order Web", propertyChanged: (b, _, v) => ((ApplicationSidebar)b)._brand.Text = v?.ToString() ?? "Order Web");
    public static readonly BindableProperty RestaurantLogoProperty = BindableProperty.Create(nameof(RestaurantLogo), typeof(ImageSource), typeof(ApplicationSidebar), propertyChanged: (b, _, v) => ((ApplicationSidebar)b)._logo.Source = (ImageSource?)v);
    public static readonly BindableProperty SelectedRouteProperty = BindableProperty.Create(nameof(SelectedRoute), typeof(string), typeof(ApplicationSidebar), string.Empty, BindingMode.TwoWay, propertyChanged: (b, _, _) => ((ApplicationSidebar)b).Rebuild());
    public static readonly BindableProperty UserNameProperty = BindableProperty.Create(nameof(UserName), typeof(string), typeof(ApplicationSidebar), "No user", propertyChanged: (b, _, v) => ((ApplicationSidebar)b)._identity.UserName = v?.ToString() ?? "No user");
    public static readonly BindableProperty TerminalNameProperty = BindableProperty.Create(nameof(TerminalName), typeof(string), typeof(ApplicationSidebar), "Terminal", propertyChanged: (b, _, v) => ((ApplicationSidebar)b)._identity.TerminalName = v?.ToString() ?? "Terminal");
    public static readonly BindableProperty ConnectionStatusProperty = BindableProperty.Create(nameof(ConnectionStatus), typeof(string), typeof(ApplicationSidebar), "Connected", propertyChanged: (b, _, v) => ((ApplicationSidebar)b)._connection.Status = v?.ToString() ?? "Connected");
    public static readonly BindableProperty ShowUpdateButtonProperty = BindableProperty.Create(nameof(ShowUpdateButton), typeof(bool), typeof(ApplicationSidebar), false, propertyChanged: (b, _, v) => ((ApplicationSidebar)b)._update.IsVisible = (bool)v);
    public static readonly BindableProperty IsUpdatingProperty = BindableProperty.Create(nameof(IsUpdating), typeof(bool), typeof(ApplicationSidebar), false, propertyChanged: (b, _, v) => ((ApplicationSidebar)b).ApplyUpdating((bool)v));
    public static readonly BindableProperty AvailableCapabilitiesProperty = BindableProperty.Create(nameof(AvailableCapabilities), typeof(IReadOnlySet<string>), typeof(ApplicationSidebar), propertyChanged: (b, _, _) => ((ApplicationSidebar)b).Rebuild());
    public static readonly BindableProperty AvailableFeaturesProperty = BindableProperty.Create(nameof(AvailableFeatures), typeof(IReadOnlySet<string>), typeof(ApplicationSidebar), propertyChanged: (b, _, _) => ((ApplicationSidebar)b).Rebuild());

    public ApplicationSidebar()
    {
        WidthRequest = 280;
        _logo = new Image { Source = "companymark.png", WidthRequest = 48, HeightRequest = 48, Aspect = Aspect.AspectFit, HorizontalOptions = LayoutOptions.Center };
        _brand = new Label { Text = "Order Web", FontSize = 17, FontAttributes = FontAttributes.Bold, CharacterSpacing = .3, HorizontalTextAlignment = TextAlignment.Center };
        _brand.TextColor = Color.FromArgb("#0F2F5F");
        var subtitle = new Label { Text = "Restaurant Management", FontSize = 10, FontAttributes = FontAttributes.Bold, CharacterSpacing = .5, HorizontalTextAlignment = TextAlignment.Center };
        subtitle.Use(Label.TextColorProperty, "OwTextMuted");
        _role = new Label { FontSize = 10, HorizontalTextAlignment = TextAlignment.Center };
        _role.Use(Label.TextColorProperty, "OwTextMuted");
        var heading = new VerticalStackLayout { Padding = new Thickness(12, 10, 12, 8), Spacing = 2, Children = { _logo, _brand, subtitle, _role } };

        _items = new VerticalStackLayout { Padding = new Thickness(16, 10), Spacing = 5 };
        var scroll = new ScrollView { Content = _items };
        _identity = new UserTerminalInfo();
        _connection = new ConnectionIndicator { HorizontalOptions = LayoutOptions.Start };
        _update = new SharedButton { Text = "Update All", IsVisible = false };
        _update.Clicked += (_, _) => UpdateRequested?.Invoke(this, EventArgs.Empty);
        var logout = new SharedButton { Text = "Logout", Variant = ButtonVariant.Secondary };
        logout.Clicked += (_, _) => LogoutRequested?.Invoke(this, EventArgs.Empty);
        // Keep the sidebar footer focused on the shared refresh action. The
        // signed-in user, role, terminal name, connection badge, and logout
        // controls are intentionally not repeated in this navigation area.
        // User/session details remain available in the application header.
        var footer = new VerticalStackLayout { Padding = new Thickness(14), Spacing = 10, Children = { _update } };
        var grid = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) } };
        grid.Use(Grid.BackgroundColorProperty, "OwSurface");
        grid.Add(heading); grid.Add(scroll, 0, 1); grid.Add(footer, 0, 2); Content = grid;
    }

    public event EventHandler<NavigationRequestedEventArgs>? NavigationRequested;
    public event EventHandler? LogoutRequested;
    public event EventHandler? UpdateRequested;
    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public string CurrentRole { get => (string)GetValue(CurrentRoleProperty); set => SetValue(CurrentRoleProperty, value); }
    public string RestaurantName { get => (string)GetValue(RestaurantNameProperty); set => SetValue(RestaurantNameProperty, value); }
    public ImageSource? RestaurantLogo { get => (ImageSource?)GetValue(RestaurantLogoProperty); set => SetValue(RestaurantLogoProperty, value); }
    public string SelectedRoute { get => (string)GetValue(SelectedRouteProperty); set => SetValue(SelectedRouteProperty, value); }
    public string UserName { get => (string)GetValue(UserNameProperty); set => SetValue(UserNameProperty, value); }
    public string TerminalName { get => (string)GetValue(TerminalNameProperty); set => SetValue(TerminalNameProperty, value); }
    public string ConnectionStatus { get => (string)GetValue(ConnectionStatusProperty); set => SetValue(ConnectionStatusProperty, value); }
    public bool ShowUpdateButton { get => (bool)GetValue(ShowUpdateButtonProperty); set => SetValue(ShowUpdateButtonProperty, value); }
    public bool IsUpdating { get => (bool)GetValue(IsUpdatingProperty); set => SetValue(IsUpdatingProperty, value); }
    public IReadOnlySet<string>? AvailableCapabilities { get => (IReadOnlySet<string>?)GetValue(AvailableCapabilitiesProperty); set => SetValue(AvailableCapabilitiesProperty, value); }
    public IReadOnlySet<string>? AvailableFeatures { get => (IReadOnlySet<string>?)GetValue(AvailableFeaturesProperty); set => SetValue(AvailableFeaturesProperty, value); }

    public void Refresh() => Rebuild();
    private void Rebuild()
    {
        if (_items == null) return;
        _items.Children.Clear();
        if (ItemsSource is null) return;
        foreach (var item in ItemsSource.OfType<ApplicationNavigationItem>().Where(item => item.IsAllowed(CurrentRole, AvailableCapabilities, AvailableFeatures)))
        {
            var row = new SidebarItemView { Text = item.Title, IconSource = item.IconSource, IsSelected = string.Equals(item.Route, SelectedRoute, StringComparison.OrdinalIgnoreCase), IsEnabled = item.IsEnabled, Opacity = item.IsEnabled ? 1 : .5 };
            row.Tapped += (_, _) => { if (!item.IsEnabled) return; SelectedRoute = item.Route; NavigationRequested?.Invoke(this, new NavigationRequestedEventArgs(item)); };
            _items.Children.Add(row);
        }
    }
    private void ApplyUpdating(bool updating) { _update.IsEnabled = !updating; _update.Text = updating ? "Updating…" : "Update All"; _update.Variant = updating ? ButtonVariant.Secondary : ButtonVariant.Primary; }
}
