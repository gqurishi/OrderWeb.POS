using Microsoft.Maui.Controls;
using POS_in_NET.Services;
using POS_in_NET.Models;
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
        private readonly CloudOrderService? _cloudService;
        private bool _isDashboardLoading;
        private bool _isRestaurantNavigationInProgress;

        public DashboardPage()
        {
            InitializeComponent();
            _authService = AuthenticationService.Instance;
            _roleAccessService = ServiceHelper.GetService<RoleAccessService>() ?? new RoleAccessService();
            _orderService = new OrderService();
            _cloudService = ServiceHelper.GetService<CloudOrderService>();
            
            // Set the page title in the TopBar
            TopBar.SetPageTitle("Dashboard");
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            System.Diagnostics.Debug.WriteLine("🏠 Dashboard appearing - starting initialization");

            if (!_roleAccessService.IsAdmin(_authService.CurrentUser?.Role))
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Access Denied", "Only Admin can access Dashboard.");
                await Shell.Current.GoToAsync($"//{_roleAccessService.ResolveDashboardRoute(_authService.CurrentUser?.Role)}");
                return;
            }
            
            try
            {
                // Load dashboard data - run in background but catch errors
                _ = LoadDashboardDataSafely();
                
                System.Diagnostics.Debug.WriteLine("✅ Dashboard initialization complete");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Dashboard OnAppearing error: {ex.Message}");
            }
        }
        
        private async Task LoadDashboardDataSafely()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("📊 Loading dashboard data...");
                await LoadDashboardData();
                System.Diagnostics.Debug.WriteLine("✅ Dashboard data loaded successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Dashboard data load failed: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ Stack trace: {ex.StackTrace}");
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
                _isDashboardLoading = true;
                System.Diagnostics.Debug.WriteLine("📊 Starting parallel data load...");

                var allOrders = await _orderService.GetOrdersAsync();
                var today = DateTime.Today;
                var startOfWindow = today.AddDays(-6);
                var todayOrders = allOrders.Where(o => o.CreatedAt.Date == today).ToList();
                var weekOrders = allOrders.Where(o => o.CreatedAt.Date >= startOfWindow && o.CreatedAt.Date <= today).ToList();
                
                // Load stats and cloud status in parallel with individual error handling
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
                            System.Diagnostics.Debug.WriteLine($"❌ LoadTodaysStats error: {ex.Message}");
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
                            System.Diagnostics.Debug.WriteLine($"❌ LoadWeeklySales error: {ex.Message}");
                        }
                    })
                };
                
                // Wait for all with timeout
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10));
                var completedTask = await Task.WhenAny(Task.WhenAll(tasks), timeoutTask);
                
                if (completedTask == timeoutTask)
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ Dashboard data load timed out after 10 seconds");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("✅ Dashboard data loaded successfully");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Dashboard load error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ Stack trace: {ex.StackTrace}");
            }
            finally
            {
                _isDashboardLoading = false;
            }
        }

        private async Task LoadTodaysStats(List<Order> todayOrders)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("📈 Loading today's stats...");
                
                // Set defaults first on main thread
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (TodaysOrdersLabel != null)
                        TodaysOrdersLabel.Text = "0";
                    if (TodaysSalesLabel != null)
                        TodaysSalesLabel.Text = "£0.00";
                });
                System.Diagnostics.Debug.WriteLine($"✅ Today's orders filtered: {todayOrders.Count}");
                
                decimal todaysSales = 0;
                foreach (var order in todayOrders)
                {
                    todaysSales += order.TotalAmount;
                }
                System.Diagnostics.Debug.WriteLine($"✅ Today's sales calculated: £{todaysSales:F2}");

                // Update UI on main thread
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (TodaysOrdersLabel != null)
                        TodaysOrdersLabel.Text = todayOrders.Count.ToString();
                    if (TodaysSalesLabel != null)
                        TodaysSalesLabel.Text = $"£{todaysSales:F2}";
                });
                
                System.Diagnostics.Debug.WriteLine("✅ Today's stats updated in UI");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error loading today's stats: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ Stack trace: {ex.StackTrace}");
                
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
                System.Diagnostics.Debug.WriteLine("📈 Loading weekly sales...");
                
                // Set default first
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (WeeklySalesLabel != null)
                        WeeklySalesLabel.Text = "£0.00";
                });
                decimal weeklySales = 0;
                foreach (var order in weekOrders)
                {
                    weeklySales += order.TotalAmount;
                }

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (WeeklySalesLabel != null)
                        WeeklySalesLabel.Text = $"£{weeklySales:F2}";
                });
                
                System.Diagnostics.Debug.WriteLine($"✅ Weekly sales: £{weeklySales:F2}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error loading weekly sales: {ex.Message}");
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (WeeklySalesLabel != null)
                        WeeklySalesLabel.Text = "£0.00";
                });
            }
        }

        private async void OnCollectionClicked(object sender, EventArgs e)
        {
            try
            {
                // Clear navigation stack before navigating to modal
                while (Navigation.NavigationStack.Count > 1)
                {
                    await Navigation.PopAsync(false);
                }
                await Shell.Current.GoToAsync("collection");
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
                // Clear navigation stack before navigating to modal
                while (Navigation.NavigationStack.Count > 1)
                {
                    await Navigation.PopAsync(false);
                }
                await Shell.Current.GoToAsync("delivery");
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

                // Clear all shell section detail stacks so Visual Layout always opens at root page.
                ClearShellDetailStacks();

                // Now navigate to restaurant layout with instant route reset
                await Shell.Current.GoToAsync("//visuallayout");
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
            if (Shell.Current is not Shell shell)
            {
                return;
            }

            foreach (var shellItem in shell.Items)
            {
                foreach (var shellSection in shellItem.Items)
                {
                    var nav = shellSection.Navigation;
                    if (nav?.NavigationStack == null || nav.NavigationStack.Count <= 1)
                    {
                        continue;
                    }

                    var pagesToRemove = nav.NavigationStack.Skip(1).ToList();
                    foreach (var page in pagesToRemove)
                    {
                        nav.RemovePage(page);
                    }
                }
            }
        }

        private async void OnTodaysSalesClicked(object sender, EventArgs e)
        {
            try
            {
                await Shell.Current.GoToAsync("//report");
            }
            catch (Exception ex)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to open report page: {ex.Message}");
            }
        }

        private async void OnViewWebOrdersClicked(object sender, EventArgs e)
        {
            try
            {
                await Shell.Current.GoToAsync("//weborders");
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
                await Shell.Current.GoToAsync("//cloudsettings");
            }
            catch (Exception ex)
            {
                await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Error", $"Failed to navigate to cloud settings: {ex.Message}");
            }
        }

        private async void OnLogoutClicked(object sender, EventArgs e)
        {
            // Get authentication service
            var authService = ServiceHelper.GetService<AuthenticationService>();
            if (authService != null)
            {
                await authService.LogoutAsync();
            }

            // Navigate to login page immediately
            await Shell.Current.GoToAsync("//login");
        }
    }
}
