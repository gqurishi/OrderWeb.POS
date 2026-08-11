using Microsoft.Maui.Controls;
using POS_in_NET.Services;
using POS_in_NET.Models;
using POS_in_NET.Views;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace POS_in_NET.Pages
{
    public partial class DashboardPage : ContentPage
    {
        private readonly AuthenticationService _authService;
        private readonly RoleAccessService _roleAccessService;
        private readonly OrderService _orderService;
        private readonly ZReportService _zReportService;
        private readonly ZReportPrintService _zReportPrintService;
        private readonly CloudOrderService? _cloudService;
        private readonly NavigationCoordinator _navigationCoordinator;
        private bool _isDashboardLoading;
        private bool _isZReportLoading;
        private bool _isRestaurantNavigationInProgress;
        private ZReportSnapshot? _currentZReport;

        public DashboardPage()
        {
            InitializeComponent();
            _authService = AuthenticationService.Instance;
            _roleAccessService = ServiceHelper.GetService<RoleAccessService>() ?? new RoleAccessService();
            _orderService = ServiceHelper.GetService<OrderService>() ?? new OrderService();
            _zReportService = ServiceHelper.GetService<ZReportService>()
                ?? new ZReportService(
                    new DatabaseService(),
                    new DailyReportService(new DatabaseService()),
                    new TillExpenseService(new DatabaseService(), _authService),
                    new DiscountAuditService(new DatabaseService(), _authService),
                    new BusinessSettingsService());
            _zReportPrintService = ServiceHelper.GetService<ZReportPrintService>()
                ?? new ZReportPrintService(
                    _zReportService,
                    new NetworkPrinterDatabaseService(new DatabaseService()),
                    new NetworkPrinterService());
            _cloudService = ServiceHelper.GetService<CloudOrderService>();
            _navigationCoordinator = ServiceHelper.GetService<NavigationCoordinator>() ?? NavigationCoordinator.Shared;
            
            TopBar.SetPageTitle("Dashboard");
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            System.Diagnostics.Debug.WriteLine(" Dashboard appearing - starting initialization");

            if (!_roleAccessService.CanViewZReport(_authService.CurrentUser?.Role))
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Access Denied", "Only Admin can access Dashboard.");
                await _navigationCoordinator.NavigateShellAsync(_roleAccessService.ResolveDashboardRoute(_authService.CurrentUser?.Role));
                return;
            }
            
            try
            {
                UpdateZReportTerminalHint();
                _ = LoadDashboardDataSafely();
                _ = LoadZReportSummaryAsync(DateTime.Today);
                System.Diagnostics.Debug.WriteLine(" Dashboard initialization complete");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" Dashboard OnAppearing error: {ex.Message}");
            }
        }
        
        private async Task LoadDashboardDataSafely()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine(" Loading dashboard data...");
                await LoadDashboardData();
                System.Diagnostics.Debug.WriteLine(" Dashboard data loaded successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" Dashboard data load failed: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($" Stack trace: {ex.StackTrace}");
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
        }

        private async Task LoadDashboardData()
        {
            if (_isDashboardLoading)
            {
                return;
            }

            try
            {
                var performance = PosPerformanceMonitor.BeginDataLoad("Dashboard");
                _isDashboardLoading = true;
                System.Diagnostics.Debug.WriteLine(" Starting parallel data load...");

                var allOrders = await _orderService.GetOrdersAsync();
                var today = DateTime.Today;
                var startOfWindow = today.AddDays(-6);
                var todayOrders = allOrders.Where(o => o.CreatedAt.Date == today).ToList();
                var weekOrders = allOrders.Where(o => o.CreatedAt.Date >= startOfWindow && o.CreatedAt.Date <= today).ToList();
                
                var tasks = new List<Task>
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            await LoadTodaysStats(todayOrders);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($" LoadTodaysStats error: {ex.Message}");
                        }
                    }),
                    Task.Run(async () =>
                    {
                        try
                        {
                            await LoadWeeklySales(weekOrders);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($" LoadWeeklySales error: {ex.Message}");
                        }
                    })
                };
                
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10));
                var completedTask = await Task.WhenAny(Task.WhenAll(tasks), timeoutTask);
                
                if (completedTask == timeoutTask)
                {
                    System.Diagnostics.Debug.WriteLine(" Dashboard data load timed out after 10 seconds");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine(" Dashboard data loaded successfully");
                    PosPerformanceMonitor.MarkDataVisible(performance);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" Dashboard load error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($" Stack trace: {ex.StackTrace}");
            }
            finally
            {
                _isDashboardLoading = false;
            }
        }

        private async Task LoadZReportSummaryAsync(DateTime reportDate)
        {
            if (_isZReportLoading)
            {
                return;
            }

            try
            {
                _isZReportLoading = true;
                var user = _authService.CurrentUser;
                var displayName = user == null
                    ? "Admin"
                    : !string.IsNullOrWhiteSpace(user.Name) ? user.Name : user.Username;

                var snapshot = await _zReportService.GetSummaryAsync(reportDate, displayName);
                _currentZReport = snapshot;
                await ApplyZReportToUiAsync(snapshot);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" Z-Report load error: {ex.Message}");
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (ZReportUpdatedLabel != null)
                    {
                        ZReportUpdatedLabel.Text = "Could not load Z-Report summary";
                    }
                });
            }
            finally
            {
                _isZReportLoading = false;
            }
        }

        private async Task ApplyZReportToUiAsync(ZReportSnapshot snapshot)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (TodaysOrdersLabel != null)
                {
                    TodaysOrdersLabel.Text = snapshot.OrderCount.ToString();
                }

                if (TodaysSalesLabel != null)
                {
                    TodaysSalesLabel.Text = snapshot.GrossDisplay;
                }

                if (ZReportDateLabel != null)
                {
                    ZReportDateLabel.Text = $"{snapshot.DateDisplay} · {snapshot.TerminalName}";
                }

                if (ZReportUpdatedLabel != null)
                {
                    ZReportUpdatedLabel.Text = snapshot.LastUpdatedDisplay;
                }

                if (ZReportGrossLabel != null) ZReportGrossLabel.Text = snapshot.GrossDisplay;
                if (ZReportNetLabel != null) ZReportNetLabel.Text = snapshot.NetDisplay;
                if (ZReportVatLabel != null) ZReportVatLabel.Text = snapshot.VatDisplay;
                if (ZReportCashLabel != null) ZReportCashLabel.Text = snapshot.CashDisplay;
                if (ZReportCardLabel != null) ZReportCardLabel.Text = snapshot.CardDisplay;
                if (ZReportGiftTipsLabel != null)
                {
                    ZReportGiftTipsLabel.Text = $"£{(snapshot.GiftCardTotal + snapshot.TipsTotal):F2}";
                }
                if (ZReportPosLabel != null)
                {
                    ZReportPosLabel.Text = $"{snapshot.PosDisplay} ({snapshot.PosOrderCount})";
                }
                if (ZReportOnlineLabel != null)
                {
                    ZReportOnlineLabel.Text = $"{snapshot.OnlineDisplay} ({snapshot.OnlineOrderCount})";
                }
                if (ZReportTillOutLabel != null) ZReportTillOutLabel.Text = snapshot.TillNetOutDisplay;
                if (ZReportExpectedCashLabel != null) ZReportExpectedCashLabel.Text = snapshot.ExpectedCashDisplay;
                if (ZReportVsYesterdayLabel != null) ZReportVsYesterdayLabel.Text = snapshot.SalesVsYesterdayDisplay;

                UpdateZReportTerminalHint();
            });
        }

        private void UpdateZReportTerminalHint()
        {
            if (ZReportMotherTerminalLabel == null || ZReportPrintTodayButton == null || ZReportPrintYesterdayButton == null)
            {
                return;
            }

            var canPrint = TerminalRoleService.CanPrintZReport;
            ZReportMotherTerminalLabel.IsVisible = !canPrint;
            ZReportPrintTodayButton.IsEnabled = canPrint;
            ZReportPrintYesterdayButton.IsEnabled = canPrint;
            ZReportPrintTodayButton.Opacity = canPrint ? 1 : 0.5;
            ZReportPrintYesterdayButton.Opacity = canPrint ? 1 : 0.5;
        }

        private async Task LoadTodaysStats(List<Order> todayOrders)
        {
            try
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (TodaysOrdersLabel != null)
                        TodaysOrdersLabel.Text = "0";
                    if (TodaysSalesLabel != null)
                        TodaysSalesLabel.Text = "£0.00";
                });
                
                decimal todaysSales = todayOrders.Sum(order => order.TotalAmount);

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (TodaysOrdersLabel != null)
                        TodaysOrdersLabel.Text = todayOrders.Count.ToString();
                    if (TodaysSalesLabel != null)
                        TodaysSalesLabel.Text = $"£{todaysSales:F2}";
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" Error loading today's stats: {ex.Message}");
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (TodaysOrdersLabel != null)
                        TodaysOrdersLabel.Text = "0";
                    if (TodaysSalesLabel != null)
                        TodaysSalesLabel.Text = "£0.00";
                });
            }
        }

        private async Task LoadWeeklySales(List<Order> weekOrders)
        {
            try
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (WeeklySalesLabel != null)
                        WeeklySalesLabel.Text = "£0.00";
                });

                var weeklySales = weekOrders.Sum(order => order.TotalAmount);

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (WeeklySalesLabel != null)
                        WeeklySalesLabel.Text = $"£{weeklySales:F2}";
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" Error loading weekly sales: {ex.Message}");
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (WeeklySalesLabel != null)
                        WeeklySalesLabel.Text = "£0.00";
                });
            }
        }

        private async void OnZReportRefreshClicked(object sender, EventArgs e)
        {
            await LoadZReportSummaryAsync(DateTime.Today);
        }

        private async void OnZReportShortcutClicked(object sender, EventArgs e)
        {
            await LoadZReportSummaryAsync(DateTime.Today);
            await PrintZReportAsync(DateTime.Today, isReprint: false);
        }

        private async void OnPrintZReportTodayClicked(object sender, EventArgs e)
        {
            await PrintZReportAsync(DateTime.Today, isReprint: false);
        }

        private async void OnPrintZReportYesterdayClicked(object sender, EventArgs e)
        {
            var yesterday = DateTime.Today.AddDays(-1);
            await PrintZReportAsync(yesterday, isReprint: true);
        }

        private async Task PrintZReportAsync(DateTime reportDate, bool isReprint)
        {
            if (!_roleAccessService.CanPrintZReport(_authService.CurrentUser?.Role))
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Access Denied", "Only Admin can print Z-Reports.");
                return;
            }

            if (!TerminalRoleService.CanPrintZReport)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync(
                    "Mother Terminal Required",
                    "Z-Report printing runs on the mother terminal only. You can still view the live summary here.");
                return;
            }

            try
            {
                var user = _authService.CurrentUser;
                var displayName = user == null
                    ? "Admin"
                    : !string.IsNullOrWhiteSpace(user.Name) ? user.Name : user.Username;

                var snapshot = await _zReportService.GetSummaryAsync(reportDate, displayName);
                snapshot.IsReprint = isReprint;
                _currentZReport = snapshot;

                if (reportDate.Date == DateTime.Today)
                {
                    await ApplyZReportToUiAsync(snapshot);
                }

                var confirmDialog = new ModernConfirmDialog();
                confirmDialog.SetConfirm(
                    isReprint ? "Reprint Z-Report" : "Print Z-Report",
                    $"Print Z-Report for {snapshot.DateDisplay} to the receipt printer?",
                    "Print",
                    "Cancel",
                    "logo",
                    "#0F766E");
                var confirm = await confirmDialog.ShowAsync();

                if (!confirm)
                {
                    return;
                }

                // Z Print is intentionally a single compact summary receipt.
                var result = await _zReportPrintService.PrintAsync(
                    snapshot,
                    includeDetailSlip: false,
                    printedByUserId: user?.Id);

                if (result.Success)
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Z-Report Printed", result.Message);
                }
                else
                {
                    await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Print Failed", result.Message);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($" Z-Report print error: {ex.Message}");
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Print Failed", ex.Message);
            }
        }

        private async void OnCollectionClicked(object sender, EventArgs e)
        {
            try
            {
                NavigationCoordinator.PruneTemporaryPages();
                await _navigationCoordinator.NavigateTemporaryRouteAsync("collection", source: sender as VisualElement);
            }
            catch (Exception ex)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to open collection page: {ex.Message}");
            }
        }

        private async void OnDeliveryClicked(object sender, EventArgs e)
        {
            try
            {
                NavigationCoordinator.PruneTemporaryPages();
                await _navigationCoordinator.NavigateTemporaryRouteAsync("delivery", source: sender as VisualElement);
            }
            catch (Exception ex)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to open delivery page: {ex.Message}");
            }
        }

        private async void OnRestaurantClicked(object sender, EventArgs e)
        {
            if (_isRestaurantNavigationInProgress)
            {
                return;
            }

            try
            {
                _isRestaurantNavigationInProgress = true;
                ClearShellDetailStacks();
                await _navigationCoordinator.NavigateShellAsync("visuallayout", source: sender as VisualElement);
            }
            catch (Exception ex)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to open restaurant layout: {ex.Message}");
            }
            finally
            {
                _isRestaurantNavigationInProgress = false;
            }
        }

        private static void ClearShellDetailStacks()
        {
            NavigationCoordinator.PruneTemporaryPages();
        }

        private async void OnViewWebOrdersClicked(object sender, EventArgs e)
        {
            try
            {
                await _navigationCoordinator.NavigateShellAsync("weborders", source: sender as VisualElement);
            }
            catch (Exception ex)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to navigate to web orders: {ex.Message}");
            }
        }

        private async void OnCloudSettingsClicked(object sender, EventArgs e)
        {
            try
            {
                await _navigationCoordinator.NavigateShellAsync("cloudsettings", source: sender as VisualElement);
            }
            catch (Exception ex)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to navigate to cloud settings: {ex.Message}");
            }
        }

        private async void OnLogoutClicked(object sender, EventArgs e)
        {
            var authService = ServiceHelper.GetService<AuthenticationService>();
            if (authService != null)
            {
                await authService.LogoutAsync();
            }

            await _navigationCoordinator.NavigateShellAsync("login", animated: false, source: sender as VisualElement);
        }
    }
}
