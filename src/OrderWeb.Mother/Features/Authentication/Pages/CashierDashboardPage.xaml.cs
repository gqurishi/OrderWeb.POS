using POS_in_NET.Models;
using POS_in_NET.Services;
using OrderWeb.SharedUI.Controls;

namespace POS_in_NET.Pages;

public partial class CashierDashboardPage : ContentPage
{
    private readonly AuthenticationService _authService;
    private readonly NavigationCoordinator _navigationCoordinator;
    private readonly ZReportService _zReportService;
    private readonly ZReportPrintService _zReportPrintService;
    private readonly ApplicationShellFrame _shellFrame;

    public CashierDashboardPage()
    {
        InitializeComponent();
        _authService = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
        _navigationCoordinator = ServiceHelper.GetService<NavigationCoordinator>() ?? NavigationCoordinator.Shared;
        _zReportService = ServiceHelper.GetService<ZReportService>()
            ?? throw new InvalidOperationException("Z report service is unavailable.");
        _zReportPrintService = ServiceHelper.GetService<ZReportPrintService>()
            ?? throw new InvalidOperationException("Z report print service is unavailable.");
        var dashboardContent = Content as View
            ?? throw new InvalidOperationException("Cashier dashboard content was not initialized.");
        // The shared shell owns the common header and logout affordance. Hide
        // the original page-local header so Cashier has the same single top bar
        // as the other POS surfaces.
        if (dashboardContent is Grid dashboardGrid && dashboardGrid.Children.Count > 1)
        {
            if (dashboardGrid.Children[0] is View originalHeader)
            {
                originalHeader.IsVisible = false;
            }

            // Move the dashboard body into the shell's content area. The
            // original header is hidden, but the body row must remain
            // auto-sized; leaving it at zero makes the entire dashboard blank.
            dashboardGrid.RowDefinitions[0].Height = GridLength.Auto;
            if (dashboardGrid.Children[1] is View originalBody)
            {
                Grid.SetRow(originalBody, 0);
            }
        }
        _shellFrame = new ApplicationShellFrame
        {
            PageTitle = "Dashboard",
            MainContent = dashboardContent,
            SelectedRoute = "dashboard",
            ConnectionStatus = "Connected",
            MenuItems = CashierMenuItems(),
            ShowWelcomeBrand = true,
            ShowIdentity = false
        };
        _shellFrame.NavigationRequested += OnNavigationRequested;
        _shellFrame.LogoutRequested += OnLogoutRequested;
        Content = _shellFrame;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var user = _authService.CurrentUser;
        if (user?.Role != UserRole.Cashier)
        {
            _ = _navigationCoordinator.NavigateShellAsync("login", animated: false);
            return;
        }

        IdentityLabel.Text = $"{user.Name} · Cashier";
        BusinessDateLabel.Text = $"Business date: {DateTime.Today:dddd, dd MMMM yyyy}";
        _shellFrame.UserName = user.Name;
        _shellFrame.UserRole = "Cashier";
        _shellFrame.TerminalName = TerminalConfigurationService.GetConfiguration().TerminalName;
        _shellFrame.ConnectionStatus = "Connected";
        await LoadSummaryAsync(user);
    }

    private async Task LoadSummaryAsync(User user)
    {
        var snapshot = await _zReportService.GetSummaryAsync(DateTime.Today, user.Name, includeTopItems: false);
        TotalOrdersLabel.Text = snapshot.OrderCount.ToString();
        TotalSalesLabel.Text = $"£{snapshot.GrossSales:N2}";
        VoidsLabel.Text = snapshot.VoidCount.ToString();
        DiscountsLabel.Text = $"£{snapshot.DiscountTotal:N2}";
        CashTotalLabel.Text = $"£{snapshot.CashTotal:N2}";
        CardTotalLabel.Text = $"£{snapshot.CardTotal:N2}";
        ExpectedCashLabel.Text = $"£{snapshot.ExpectedCashInDrawer:N2}";
        VarianceLabel.Text = snapshot.CashCountVariance is { } variance ? $"£{variance:N2}" : "Not counted";
    }

    private async void OnReportsClicked(object sender, EventArgs e)
    {
        var user = _authService.CurrentUser;
        var permissionService = ServiceHelper.GetService<PermissionService>();
        if (user is not { IsActive: true, Role: UserRole.Cashier } || permissionService is null ||
            !await permissionService.HasCashierCapabilityAsync(CashierCapabilities.PreviewZ))
        {
            await AppAlertService.ShowAlertAsync("Z Report Preview", "You do not have permission to preview the Z report.");
            return;
        }

        var snapshot = await _zReportService.GetSummaryAsync(DateTime.Today, user.Name, includeTopItems: false);
        var canPrint = await permissionService.HasCashierCapabilityAsync(CashierCapabilities.PrintZ);
        var printRequested = await new CashierZReportPreviewDialogPage(snapshot, canPrint).ShowAsync(Navigation);
        if (printRequested) OnPrintZReportClicked(sender, EventArgs.Empty);
    }

    private async void OnPrintZReportClicked(object sender, EventArgs e)
    {
        var user = _authService.CurrentUser;
        var permissionService = ServiceHelper.GetService<PermissionService>();
        if (user is not { IsActive: true, Role: UserRole.Cashier } || permissionService is null ||
            !await permissionService.HasCashierCapabilityAsync(CashierCapabilities.PrintZ))
        {
            await AppAlertService.ShowAlertAsync("Z Report", "You do not have permission to print the Z report.");
            return;
        }

        var button = sender as Button;
        if (button != null) button.IsEnabled = false;
        try
        {
            var snapshot = await _zReportService.GetSummaryAsync(DateTime.Today, user.Name, includeTopItems: false);
            snapshot.IsReprint = false;
            var result = await _zReportPrintService.PrintAsync(snapshot, includeDetailSlip: false, printedByUserId: user.Id);
            await AppAlertService.ShowAlertAsync(result.Success ? "Z Report Printed" : "Print Failed", result.Message);
        }
        catch (Exception ex)
        {
            await AppAlertService.ShowAlertAsync("Print Failed", ex.Message);
        }
        finally
        {
            if (button != null) button.IsEnabled = true;
        }
    }

    private async void OnOpenCashDrawerClicked(object sender, EventArgs e)
    {
        var flow = ServiceHelper.GetService<CashDrawerFlowService>();
        if (flow is null)
        {
            await AppAlertService.ShowAlertAsync("Cash Drawer", "Cash drawer service is unavailable.");
            return;
        }

        await flow.RunAsync(new CashDrawerFlowContext { SourceArea = "cashier_dashboard" });
    }

    private async void OnLogoutClicked(object sender, EventArgs e)
    {
        await _authService.LogoutAsync();
        await _navigationCoordinator.NavigateShellAsync("login", animated: false, source: sender as VisualElement);
    }

    private async void OnLogoutRequested(object? sender, EventArgs e)
    {
        await _authService.LogoutAsync();
        await _navigationCoordinator.NavigateShellAsync("login", animated: false);
    }

    private async void OnNavigationRequested(object? sender, NavigationRequestedEventArgs e)
    {
        _shellFrame.SelectedRoute = e.Route;
        if (e.Route == "dashboard") return;
        if (e.Route == "yesterdayprint")
        {
            await PrintYesterdayZReportAsync();
            _shellFrame.SelectedRoute = "dashboard";
            return;
        }
        if (e.Route == "cashdrawer")
        {
            OnOpenCashDrawerClicked(this, EventArgs.Empty);
            return;
        }
        if (e.Route == "reconciliation")
        {
            await AppAlertService.ShowAlertAsync("Reconciliation", "Cash reconciliation is available from the Cash Drawer report while the dedicated workflow is completed.");
            return;
        }

        await _navigationCoordinator.NavigateShellAsync("report", animated: false, source: this);
    }

    private async Task PrintYesterdayZReportAsync()
    {
        var user = _authService.CurrentUser;
        var permissionService = ServiceHelper.GetService<PermissionService>();
        if (user is not { IsActive: true, Role: UserRole.Cashier } || permissionService is null ||
            !await permissionService.HasCashierCapabilityAsync(CashierCapabilities.ReprintZ))
        {
            await AppAlertService.ShowAlertAsync("Yesterday Report Print", "You do not have permission to print yesterday's Z report.");
            return;
        }

        try
        {
            var businessDate = DateTime.Today.AddDays(-1);
            var snapshot = await _zReportService.GetSummaryAsync(businessDate, user.Name, includeTopItems: false);
            snapshot.IsReprint = true;
            var result = await _zReportPrintService.PrintAsync(snapshot, includeDetailSlip: false, printedByUserId: user.Id);
            await AppAlertService.ShowAlertAsync(result.Success ? "Yesterday Report Printed" : "Print Failed", result.Message);
        }
        catch (Exception ex)
        {
            await AppAlertService.ShowAlertAsync("Print Failed", ex.Message);
        }
    }

    private static IReadOnlyList<ApplicationNavigationItem> CashierMenuItems() =>
    [
        new("dashboard", "Dashboard", "dashboard.png"),
        new("report", "Reports", "report.png"),
        new("yesterdayprint", "Yesterday Report Print", "printers.png")
    ];
}
