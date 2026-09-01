namespace OrderWeb.Client.Views.Layout;

using Microsoft.Maui.Controls.Shapes;

public partial class ClientSidebar : ContentView
{
    public static readonly BindableProperty RoleProperty = BindableProperty.Create(
        nameof(Role),
        typeof(string),
        typeof(ClientSidebar),
        "User",
        propertyChanged: OnSidebarStateChanged);

    public static readonly BindableProperty SelectedMenuProperty = BindableProperty.Create(
        nameof(SelectedMenu),
        typeof(string),
        typeof(ClientSidebar),
        "Dashboard",
        propertyChanged: OnSidebarStateChanged);

    public static readonly BindableProperty ShowFooterProperty = BindableProperty.Create(
        nameof(ShowFooter),
        typeof(bool),
        typeof(ClientSidebar),
        false,
        propertyChanged: OnShowFooterChanged);

    public ClientSidebar()
    {
        InitializeComponent();
        BuildMenu();
    }

    public event EventHandler<string>? MenuItemSelected;
    public event EventHandler? UpdateAllClicked;

    public string Role
    {
        get => (string)GetValue(RoleProperty);
        set => SetValue(RoleProperty, value);
    }

    public string SelectedMenu
    {
        get => (string)GetValue(SelectedMenuProperty);
        set => SetValue(SelectedMenuProperty, value);
    }

    public bool ShowFooter
    {
        get => (bool)GetValue(ShowFooterProperty);
        set => SetValue(ShowFooterProperty, value);
    }

    private static void OnSidebarStateChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is ClientSidebar sidebar)
        {
            sidebar.BuildMenu();
        }
    }

    private static void OnShowFooterChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is ClientSidebar sidebar)
        {
            sidebar.Footer.IsVisible = sidebar.ShowFooter;
        }
    }

    private void BuildMenu()
    {
        if (MenuStack == null)
        {
            return;
        }

        MenuStack.Children.Clear();
        foreach (var item in MenuItemsForRole(Role))
        {
            MenuStack.Children.Add(BuildMenuRow(item));
        }

        Footer.IsVisible = ShowFooter;
    }

    private View BuildMenuRow(SidebarItem item)
    {
        var iconView = string.Equals(item.Label, "Cash Drawer", StringComparison.OrdinalIgnoreCase)
            ? BuildCashDrawerIcon()
            : new Image
            {
                Source = item.ImageSource,
                WidthRequest = 32,
                HeightRequest = 32,
                Aspect = Aspect.AspectFit,
                VerticalOptions = LayoutOptions.Center
            };

        var row = new Grid
        {
            Padding = new Thickness(16, 14),
            BackgroundColor = SelectedMenu == item.Label ? Color.FromArgb("#E3F2FD") : Colors.Transparent,
            ColumnDefinitions =
            {
                new ColumnDefinition(32),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 20,
            Children =
            {
                iconView,
                new Label
                {
                    Text = item.Label,
                    FontFamily = "OpenSansBold",
                    FontSize = 16,
                    TextColor = Color.FromArgb("#1E293B"),
                    VerticalTextAlignment = TextAlignment.Center
                }
            }
        };
        if (row.Children[1] is BindableObject label)
        {
            label.SetValue(Microsoft.Maui.Controls.Grid.ColumnProperty, 1);
        }

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            SelectedMenu = item.Label;
            MenuItemSelected?.Invoke(this, item.Label);
        };
        row.GestureRecognizers.Add(tap);
        return row;
    }

    private static View BuildCashDrawerIcon()
    {
        return new Border
        {
            WidthRequest = 32,
            HeightRequest = 32,
            Stroke = Colors.Black,
            StrokeThickness = 2,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = "£",
                FontFamily = "OpenSansBold",
                FontSize = 20,
                TextColor = Colors.Black,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        };
    }

    private void OnUpdateAllClicked(object sender, EventArgs e)
    {
        UpdateAllClicked?.Invoke(this, EventArgs.Empty);
    }

    private static IReadOnlyList<SidebarItem> MenuItemsForRole(string? role)
    {
        var userItems = new[]
        {
            new SidebarItem("Dashboard", "dashboard.png"),
            new SidebarItem("Cash Drawer", "giftcard.png"),
            new SidebarItem("Live Order", "liveorder.png"),
            new SidebarItem("Restaurant", "restaurant.png"),
            new SidebarItem("Collection", "collection.png"),
            new SidebarItem("Delivery", "delivery.png"),
            new SidebarItem("Reservation", "reservation.png")
        };

        if (string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase))
        {
            return new[]
            {
                new SidebarItem("Dashboard", "dashboard.png"),
                new SidebarItem("Cash Drawer", "giftcard.png"),
                new SidebarItem("Live Order", "liveorder.png"),
                new SidebarItem("Restaurant", "restaurant.png"),
                new SidebarItem("Collection", "collection.png"),
                new SidebarItem("Delivery", "delivery.png"),
                new SidebarItem("Web Orders", "weborders.png"),
                new SidebarItem("Gift Cards", "giftcards.png"),
                new SidebarItem("Loyalty Points", "loyalty.png"),
                new SidebarItem("Reservation", "reservation.png"),
                new SidebarItem("Order History", "orderhistory.png")
            };
        }

        return userItems;
    }

    private sealed record SidebarItem(string Label, string ImageSource);
}
