using POS_in_NET.Services;
using POS_in_NET.Pages;
using POS_in_NET.Models;
using System.ComponentModel;

namespace POS_in_NET;

public partial class AppShell : Shell, INotifyPropertyChanged
{
	private string _currentDateTime = string.Empty;
	private System.Timers.Timer? _timer;
	private readonly AuthenticationService _authService;
	private readonly RoleAccessService _roleAccessService;
	private bool _isSyncingDatabase;

	private static readonly HashSet<string> ModalRoutes = new(StringComparer.OrdinalIgnoreCase)
	{
		"collection",
		"delivery"
	};

	public string CurrentDateTime
	{
		get => _currentDateTime;
		set
		{
			_currentDateTime = value;
			OnPropertyChanged(nameof(CurrentDateTime));
		}
	}

	public AppShell()
	{
		InitializeComponent();
		BindingContext = this;
		_authService = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
		_roleAccessService = ServiceHelper.GetService<RoleAccessService>() ?? new RoleAccessService();
		
		// Register modal pages for navigation
		Routing.RegisterRoute(nameof(CustomColorPickerPage), typeof(CustomColorPickerPage));
		Routing.RegisterRoute(nameof(FoodMenuManagement), typeof(FoodMenuManagement));
		Routing.RegisterRoute("reportdetails", typeof(ReportOrderDetailsPage));
		Routing.RegisterRoute("collection", typeof(CollectionCustomerModal));
		Routing.RegisterRoute("delivery", typeof(DeliveryCustomerModal));
		
		// Register User Dashboard route
		Routing.RegisterRoute("userdashboard", typeof(UserDashboardPage));
		Routing.RegisterRoute("managerdashboard", typeof(ManagerDashboardPage));
		
		// Initialize date/time
		UpdateDateTime();
		
		// Setup timer to update every second
		_timer = new System.Timers.Timer(1000);
		_timer.Elapsed += (s, e) => UpdateDateTime();
		_timer.Start();
		
		// Subscribe to navigation events to update user info
		this.Navigated += OnShellNavigated;
		this.Navigating += OnShellNavigating;
		ApplyRoleBasedMenuVisibility();
	}

	private void UpdateDateTime()
	{
		MainThread.BeginInvokeOnMainThread(() =>
		{
			CurrentDateTime = DateTime.Now.ToString("dddd, MMMM dd, yyyy - HH:mm:ss");
		});
	}

	private void OnShellNavigated(object? sender, ShellNavigatedEventArgs e)
	{
		// Update user info when navigating (handled by TopBar component now)
		System.Diagnostics.Debug.WriteLine($"Navigated to: {e.Current.Location}");
		ApplyRoleBasedMenuVisibility();
	}

	private void OnShellNavigating(object? sender, ShellNavigatingEventArgs e)
	{
		try
		{
			var target = e.Target.Location.OriginalString;
			if (!TryResolveRoute(target, out var route))
			{
				return;
			}

			if (!IsRouteAllowed(route))
			{
				e.Cancel();
				MainThread.BeginInvokeOnMainThread(async () =>
				{
					await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Access Denied", "You do not have permission to access this page.");
				});
			}
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Role guard navigation error: {ex.Message}");
		}
	}

	private async void OnLogoutClicked(object sender, EventArgs e)
	{
		// Stop the timer
		_timer?.Stop();
		
		// Get authentication service
		var authService = ServiceHelper.GetService<AuthenticationService>();
		if (authService != null)
		{
			await authService.LogoutAsync();
		}

		// Navigate to login page immediately
		await Shell.Current.GoToAsync("//login");
		
		// Restart timer
		_timer?.Start();
	}

	private async void OnSyncDatabaseClicked(object sender, EventArgs e)
	{
		if (_isSyncingDatabase)
		{
			return;
		}

		SetSyncingState(true);

		try
		{
			// Close the flyout
			Shell.Current.FlyoutIsPresented = false;

			var databaseService = ServiceHelper.GetService<DatabaseService>() ?? new DatabaseService();
			await databaseService.InitializeDatabaseAsync();
			AppDataRefreshService.RequestRefresh();
			
			// Show success message
			await POS_in_NET.Services.AppAlertService.ShowAlertAsync("✅ Sync Complete", "Database synced successfully!\nFloors and tables refreshed.");
		}
		catch (Exception ex)
		{
			await POS_in_NET.Services.AppAlertService.ShowAlertAsync("❌ Sync Error", $"Failed to sync: {ex.Message}");
		}
		finally
		{
			SetSyncingState(false);
		}
	}

	private void SetSyncingState(bool isSyncing)
	{
		_isSyncingDatabase = isSyncing;
		MainThread.BeginInvokeOnMainThread(() =>
		{
			if (SyncDatabaseButton == null)
			{
				return;
			}

			SyncDatabaseButton.IsEnabled = !isSyncing;
			SyncDatabaseButton.Text = isSyncing ? "Syncing..." : "Update All";
			SyncDatabaseButton.BackgroundColor = isSyncing ? Color.FromArgb("#F59E0B") : Color.FromArgb("#6366F1");
			SyncDatabaseButton.TextColor = Colors.White;
		});
	}

	private async void OnMenuItemTapped(object sender, EventArgs e)
	{
		try
		{
			// Clear all menu item selections first
			ClearMenuSelections();
			
			// Highlight the selected menu item
			if (sender is StackLayout stackLayout)
			{
				stackLayout.BackgroundColor = Color.FromArgb("#E3F2FD"); // Light blue selection
			}
			
			if (sender is View view && view.GestureRecognizers.FirstOrDefault() is TapGestureRecognizer tapGesture)
			{
				var route = tapGesture.CommandParameter?.ToString();
				if (!string.IsNullOrEmpty(route))
				{
					route = ResolveDashboardRoute(route);

					if (!IsRouteAllowed(route))
					{
						await POS_in_NET.Services.AppAlertService.ShowAlertAsync("Access Denied", "You do not have permission to access this feature.");
						return;
					}

					// Close the flyout
					Shell.Current.FlyoutIsPresented = false;

					if (ModalRoutes.Contains(route))
					{
						// Navigate to modal route without //
						await Shell.Current.GoToAsync(route);
					}
					else
					{
						// Navigate to shell content route with //
						await Shell.Current.GoToAsync($"//{route}");
					}
				}
			}
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Navigation error: {ex.Message}");
		}
	}

	private string ResolveDashboardRoute(string route)
	{
		if (!route.Equals("dashboard", StringComparison.OrdinalIgnoreCase))
		{
			return route;
		}

		return _roleAccessService.ResolveDashboardRoute(_authService.CurrentUser?.Role);
	}

	private bool IsRouteAllowed(string route)
	{
		return _roleAccessService.CanAccessRoute(_authService.CurrentUser?.Role, route);
	}

	private bool TryResolveRoute(string location, out string route)
	{
		route = string.Empty;
		if (string.IsNullOrWhiteSpace(location))
		{
			return false;
		}

		return _roleAccessService.TryResolveRoute(location, out route);
	}

	private void ApplyRoleBasedMenuVisibility()
	{
		var role = _authService.CurrentUser?.Role;
		var isLoggedIn = role != null;

		DashboardMenuItem.IsVisible = _roleAccessService.IsManagerOrAdmin(role);
		LiveOrderMenuItem.IsVisible = isLoggedIn;
		RestaurantMenuItem.IsVisible = isLoggedIn;
		CollectionMenuItem.IsVisible = isLoggedIn;
		DeliveryMenuItem.IsVisible = isLoggedIn;

		WebOrdersMenuItem.IsVisible = _roleAccessService.IsManagerOrAdmin(role);
		GiftCardsMenuItem.IsVisible = _roleAccessService.IsManagerOrAdmin(role);
		LoyaltyMenuItem.IsVisible = _roleAccessService.IsManagerOrAdmin(role);
		ReservationMenuItem.IsVisible = _roleAccessService.IsManagerOrAdmin(role);
		OrderHistoryMenuItem.IsVisible = _roleAccessService.IsManagerOrAdmin(role);

		ReportMenuItem.IsVisible = _roleAccessService.IsAdmin(role);
		InventoryMenuItem.IsVisible = _roleAccessService.IsAdmin(role);
		FoodMenuMenuItem.IsVisible = _roleAccessService.IsAdmin(role);
		PrintersMenuItem.IsVisible = _roleAccessService.IsAdmin(role);
		SettingsMenuItem.IsVisible = _roleAccessService.IsAdmin(role);
	}

	private void ClearMenuSelections()
	{
		// Clear all menu item background colors
		var menuItems = new[]
		{
			DashboardMenuItem, RestaurantMenuItem, FoodMenuMenuItem, WebOrdersMenuItem,
			SettingsMenuItem, CollectionMenuItem, DeliveryMenuItem, OrderHistoryMenuItem,
			LiveOrderMenuItem, GiftCardsMenuItem, LoyaltyMenuItem, ReservationMenuItem,
			ReportMenuItem, InventoryMenuItem, PrintersMenuItem
		};

		foreach (var item in menuItems)
		{
			if (item != null)
			{
				item.BackgroundColor = Colors.Transparent;
			}
		}
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		_timer?.Stop();
		_timer?.Dispose();
	}
}
