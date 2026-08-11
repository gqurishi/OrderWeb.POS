using POS_in_NET.Services;
using POS_in_NET.Models;

namespace POS_in_NET.Pages;

public partial class InventoryPage : ContentPage
{
    private readonly AuthenticationService _authService;
    private readonly RoleAccessService _roleAccessService;
    private readonly PermissionService _permissionService;

    public InventoryPage()
    {
        InitializeComponent();
        TopBar.SetPageTitle("Inventory");
        _authService = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
        _roleAccessService = ServiceHelper.GetService<RoleAccessService>() ?? new RoleAccessService();
        _permissionService = ServiceHelper.GetService<PermissionService>()
            ?? new PermissionService(new DatabaseService(), _authService);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            if (!await _permissionService.HasPermissionAsync(PermissionKeys.InventoryView))
            {
                await AppAlertService.ShowAlertAsync("Access Denied", "Only Admin can access Inventory.");
                await NavigationCoordinator.Shared.NavigateShellAsync(_roleAccessService.ResolveDashboardRoute(_authService.CurrentUser?.Role));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Inventory page error: {ex.Message}");
        }
    }
}
