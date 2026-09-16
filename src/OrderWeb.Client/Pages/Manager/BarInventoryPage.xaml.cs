using OrderWeb.Client.Services;
using OrderWeb.Contracts.Features;
using OrderWeb.SharedUI.Views;

namespace OrderWeb.Client.Pages.Manager;

public partial class BarInventoryPage : ContentPage
{
    private readonly ClientCacheService _cache = new();

    public BarInventoryPage()
    {
        InitializeComponent();

        TopBar.SetPageTitle("Bar Inventory");
        TopBar.MenuClicked += async (_, _) => await OpenSidebarAsync();
        TopBar.LogoutClicked += async (_, _) => await ClientSignOut.RequestAsync(this);
        Sidebar.MenuItemSelected += async (_, menu) => await ClientSidebarNavigation.SwitchAsync(this, menu, "inventory");

        Inventory.ActionRequested += OnInventoryActionRequested;
        Inventory.SetStockSummary(
            "No stock lines yet",
            [
                ("Receive Stock", "Log bottles and cases when deliveries arrive."),
                ("Stock Count", "Count before service so low-stock alerts stay accurate."),
                ("Waste / Breakage", "Record spills and breakage against the correct item.")
            ]);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshAccessFromMotherAsync();
        if (!HasBarInventoryAccess())
        {
            await DisplayAlert(
                "Bar Inventory",
                "This till cannot open Bar Inventory. On Mother: create a Bar Manager user, and turn Terminal Access → Bar Inventory ON, then Update All.",
                "OK");
            await Navigation.PopAsync(false);
            return;
        }

        var session = await _cache.GetCurrentLoginSessionAsync();
        var who = string.Equals(session?.Role, "BarManager", StringComparison.OrdinalIgnoreCase)
            ? "Bar Manager"
            : session?.Role ?? "Staff";
        Inventory.SetStatus($"{who} · online via Mother · shared stock workspace");
    }

    private async void OnInventoryActionRequested(object? sender, BarInventoryActionKind action)
    {
        var title = action switch
        {
            BarInventoryActionKind.Receive => "Receive Stock",
            BarInventoryActionKind.Count => "Stock Count",
            BarInventoryActionKind.Waste => "Waste / Breakage",
            BarInventoryActionKind.CurrentStock => "Current Stock",
            BarInventoryActionKind.SuggestedOrder => "Suggested Order",
            BarInventoryActionKind.WeeklyReport => "Weekly Report",
            _ => "Bar Inventory"
        };

        await DisplayAlert(
            title,
            "This stock tool is ready in SharedUI. Next step: Mother stock APIs for live data.",
            "OK");
    }

    private bool HasBarInventoryAccess()
    {
        if (!ClientHostAccess.Features.Contains(PosFeatureKeys.BarInventory) &&
            !ClientHostAccess.Routes.Contains("inventory"))
        {
            return false;
        }

        return ClientHostAccess.CanOpenMenu("Bar Inventory") ||
               ClientHostAccess.RoutesForRole(ClientHostAccess.SessionRole).Contains("inventory");
    }

    private async Task RefreshAccessFromMotherAsync()
    {
        try
        {
            var session = await _cache.GetCurrentLoginSessionAsync();
            if (session is not null)
            {
                ClientHostAccess.ApplyFromSession(session);
                Sidebar.Role = session.Role ?? "BarManager";
            }
        }
        catch
        {
            // Keep last-good access when Mother refresh fails.
        }
    }

    private async Task OpenSidebarAsync()
    {
        SidebarLayer.IsVisible = true;
        Sidebar.TranslationX = -280;
        await Sidebar.TranslateTo(0, 0, 180, Easing.CubicOut);
    }

    private async void OnBackdropTapped(object? sender, EventArgs e)
    {
        await Sidebar.TranslateTo(-280, 0, 160, Easing.CubicIn);
        SidebarLayer.IsVisible = false;
    }
}
