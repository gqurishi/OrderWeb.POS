using OrderWeb.SharedUI.Views;
using POS_in_NET.Models;
using POS_in_NET.Services;

namespace POS_in_NET.Pages;

public partial class InventoryPage : ContentPage
{
    private readonly AuthenticationService _authService;
    private readonly RoleAccessService _roleAccessService;
    private readonly PermissionService _permissionService;

    public InventoryPage()
    {
        InitializeComponent();
        TopBar.SetPageTitle("Bar Inventory");
        _authService = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
        _roleAccessService = ServiceHelper.GetService<RoleAccessService>() ?? new RoleAccessService();
        _permissionService = ServiceHelper.GetService<PermissionService>()
            ?? new PermissionService(new DatabaseService(), _authService);

        Inventory.ActionRequested += OnInventoryActionRequested;
        Inventory.SetStockSummary(
            "No stock lines yet",
            [
                ("Receive Stock", "Tap to log bottles and cases when deliveries arrive."),
                ("Stock Count", "Count before service so low-stock alerts stay accurate."),
                ("Waste / Breakage", "Record spills and breakage against the correct item.")
            ]);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            if (!await SessionAccessGuard.RequireSignedInAsync(_authService))
            {
                return;
            }

            await _permissionService.EnsureInventoryRolePermissionsAsync();

            var role = _authService.CurrentUser?.Role;
            if (!_roleAccessService.CanAccessRoute(role, "inventory"))
            {
                await AppAlertService.ShowAlertAsync(
                    "Access Denied",
                    "Only Admin or Bar Manager can access Bar Inventory.");
                await NavigationCoordinator.Shared.NavigateShellAsync(
                    _roleAccessService.ResolveDashboardRoute(role));
                return;
            }

            var who = role is UserRole.BarManager ? "Bar Manager" : role?.ToString() ?? "Staff";
            Inventory.SetStatus($"{who} · online to Mother · shared with Client tills");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Inventory page error: {ex.Message}");
        }
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

        await AppAlertService.ShowAlertAsync(
            title,
            "This stock tool is ready in SharedUI. Next step: wire live stock data from Mother.");
    }
}
