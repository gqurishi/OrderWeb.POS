using OrderWeb.SharedUI.Themes;
using POS_in_NET.Services;

namespace POS_in_NET;

public partial class App : Application
{
	private CloudOrderService? _cloudOrderService;
	private OrderWebDirectDatabaseService? _directDatabaseService;

	public App()
	{
		try
		{
			AppDiagnostics.Log("=== APP CONSTRUCTOR ===");

			InitializeComponent();
			DesignSystemBootstrap.LockHostResources(Resources);
			AppDiagnostics.Log("InitializeComponent() completed; SharedUI Pos* design system locked");

			AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
			TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

			AppDiagnostics.Log("App constructor completed");
		}
		catch (Exception ex)
		{
			AppDiagnostics.LogFatal("AppConstructor", ex);
			throw;
		}
	}

	private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
	{
		if (e.ExceptionObject is not Exception ex)
		{
			return;
		}

#if DEBUG
		AppDiagnostics.Log($"UNHANDLED EXCEPTION (terminating={e.IsTerminating}): {ex}");
#else
		AppDiagnostics.LogFatal("UnhandledException", ex);
#endif
	}

	private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
	{
#if DEBUG
		AppDiagnostics.Log($"UNOBSERVED TASK EXCEPTION: {e.Exception}");
#endif
		e.SetObserved();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		AppDiagnostics.Log("=== APP START ===");
		AppDiagnostics.Log("CreateWindow called - Starting app initialization");

		try
		{
			var appShell = new AppShell();
			AppDiagnostics.Log("AppShell created successfully");

			var window = new Window(appShell);
			AppDiagnostics.Log("Window created successfully");

			Dispatcher.Dispatch(async () =>
			{
				try
				{
					TerminalConfigurationService.TryApplyInstallerDatabaseConfig();
					var startupRoute = await StartupNavigationService.GetInitialRouteAsync();
					AppDiagnostics.Log($"Attempting navigation to {startupRoute}");
					await appShell.GoToAsync(startupRoute);
					AppDiagnostics.Log($"Navigation to {startupRoute} successful");
				}
				catch (Exception ex)
				{
					AppDiagnostics.LogFatal("StartupNavigation", ex);
				}
			});

			_ = Task.Run(async () =>
			{
				try
				{
					if (!TerminalConfigurationService.IsConfigured)
					{
						AppDiagnostics.Log("Database initialization skipped until terminal setup is complete");
						return;
					}

					AppDiagnostics.Log("Starting database initialization...");
					var authService = AuthenticationService.Instance;
					var connectionTest = await authService.TestDatabaseConnectionAsync();

					if (connectionTest.Success)
					{
						AppDiagnostics.Log(connectionTest.Message);
						await authService.EnsureAuthenticationSchemaAsync();
					}
					else
					{
						AppDiagnostics.Log($"Database connection failed: {connectionTest.Message}");
					}
				}
				catch (Exception ex)
				{
					AppDiagnostics.LogFatal("DatabaseInit", ex);
				}
			});

			_ = Task.Run(async () =>
			{
				try
				{
					if (!TerminalConfigurationService.IsConfigured)
					{
						AppDiagnostics.Log("Cloud services skipped until terminal setup is complete");
						return;
					}

					var onlineMasterCheck = await TerminalRoleService.CanRunOnlineOrderMasterJobsAsync();
					if (!onlineMasterCheck.Allowed)
					{
						AppDiagnostics.Log($"Cloud services skipped: {onlineMasterCheck.Reason}");
						return;
					}

					AppDiagnostics.Log("Starting cloud services initialization...");
					await Task.Delay(5000);
					await InitializeCloudServicesAsync();
				}
				catch (Exception ex)
				{
					AppDiagnostics.LogFatal("CloudServicesInit", ex);
				}
			});

			AppDiagnostics.Log("Window initialization complete, returning window");
			return window;
		}
		catch (Exception ex)
		{
			AppDiagnostics.LogFatal("CreateWindow", ex);
			throw;
		}
	}

	private async Task InitializeCloudServicesAsync()
	{
		try
		{
			var onlineMasterCheck = await TerminalRoleService.CanRunOnlineOrderMasterJobsAsync();
			if (!onlineMasterCheck.Allowed)
			{
				AppDiagnostics.Log($"Cloud services disabled: {onlineMasterCheck.Reason}");
				return;
			}

			var serviceProvider = Current?.Handler?.MauiContext?.Services;
			if (serviceProvider == null)
			{
				AppDiagnostics.Log("ServiceProvider is null during cloud init");
				return;
			}

			_cloudOrderService = serviceProvider.GetService<CloudOrderService>();

			var reservationSyncService = serviceProvider.GetService<ReservationSyncService>();
			if (reservationSyncService != null)
			{
				await reservationSyncService.StartAsync();
				AppDiagnostics.Log("Reservation sync service started");
			}

			AppDiagnostics.Log("Inbound HTTP webhook disabled; using authenticated WebSocket/polling sync.");

			var connectionKeeper = serviceProvider.GetService<OrderWebConnectionKeeperService>();
			if (connectionKeeper != null)
			{
				await connectionKeeper.StartAsync();
				AppDiagnostics.Log("OrderWeb connection keeper started");
			}

			_directDatabaseService = serviceProvider.GetService<OrderWebDirectDatabaseService>();
			if (_directDatabaseService != null)
			{
				await InitializeDirectDatabaseAsync();
			}
		}
		catch (Exception ex)
		{
			AppDiagnostics.LogFatal("InitializeCloudServices", ex);
		}
	}

	private async Task InitializeDirectDatabaseAsync()
	{
		try
		{
			var databaseService = Current?.Handler?.MauiContext?.Services?.GetService<DatabaseService>();
			if (databaseService == null)
			{
				return;
			}

			var config = await databaseService.GetCloudConfigAsync();

			if (config.GetValueOrDefault("direct_db_enabled", "False") == "True")
			{
				var host = config.GetValueOrDefault("db_host", "");
				var database = config.GetValueOrDefault("db_database", "");
				var username = config.GetValueOrDefault("db_username", "");
				var password = config.GetValueOrDefault("db_password", "");
				var portText = config.GetValueOrDefault("db_port", "3306");

				if (!string.IsNullOrEmpty(host) && !string.IsNullOrEmpty(database) &&
				    !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password) &&
				    int.TryParse(portText, out int port))
				{
					AppDiagnostics.Log("Configuring direct database connection...");

					var success = await _directDatabaseService!.ConfigureDatabaseConnectionAsync(host, database, username, password, port);

					if (success)
					{
						await _directDatabaseService.StartRealTimeMonitoringAsync();
						AppDiagnostics.Log("Direct database connection established");
					}
					else
					{
						AppDiagnostics.Log("Direct database connection failed");
					}
				}
				else
				{
					AppDiagnostics.Log("Direct database enabled but configuration incomplete");
				}
			}
			else
			{
				AppDiagnostics.Log("Direct database connection disabled");
			}
		}
		catch (Exception ex)
		{
			AppDiagnostics.LogFatal("InitializeDirectDatabase", ex);
		}
	}

	protected override void CleanUp()
	{
		_cloudOrderService?.StopPolling();
		_directDatabaseService?.StopRealTimeMonitoring();
		base.CleanUp();
	}
}
