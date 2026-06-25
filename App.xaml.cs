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
			// Register Syncfusion license key FIRST (before InitializeComponent)
			// Updated: December 9, 2025 - Essential Studio v27.1.48 License
			Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense("NDE1MDY0NUAzMjM3MmUzMDJlMzBQUWtDaHdJdXBlVTM0bmFxUVEveGZ1bkswUGJ6SXN1UExNeWtobERJK2p3PQ==");
			
			var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "pos-debug.log");
			File.AppendAllText(logPath, $"\n\n=== APP CONSTRUCTOR {DateTime.Now} ===\n");
			
			InitializeComponent();
			File.AppendAllText(logPath, " InitializeComponent() completed\n");
			
			// Add global exception handler
			AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
			TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
			
			File.AppendAllText(logPath, " App constructor completed\n");
		}
		catch (Exception ex)
		{
			// Try to log even if logging failed
			try
			{
				var logPath = "/tmp/pos-error.log";
				File.AppendAllText(logPath, $"CONSTRUCTOR ERROR: {ex.Message}\n{ex.StackTrace}\n");
			}
			catch { }
			throw;
		}
	}
	
	private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
	{
		if (e.ExceptionObject is Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($" UNHANDLED EXCEPTION: {ex.Message}");
			System.Diagnostics.Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
			System.Diagnostics.Debug.WriteLine($"Is Terminating: {e.IsTerminating}");

			try
			{
				var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "pos-debug.log");
				File.AppendAllText(logPath,
                    $"\n=== UNHANDLED EXCEPTION {DateTime.Now} ===\n{ex}\nIs Terminating: {e.IsTerminating}\n");
			}
			catch
			{
				// Ignore logging failures during crash handling.
			}
		}
	}
	
	private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
	{
		System.Diagnostics.Debug.WriteLine($" UNOBSERVED TASK EXCEPTION: {e.Exception.Message}");
		foreach (var ex in e.Exception.InnerExceptions)
		{
			System.Diagnostics.Debug.WriteLine($"  - {ex.Message}");
			System.Diagnostics.Debug.WriteLine($"    {ex.StackTrace}");
		}
		e.SetObserved(); // Prevent app from crashing
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "pos-debug.log");
		File.AppendAllText(logPath, $"\n\n=== APP START {DateTime.Now} ===\n");
		File.AppendAllText(logPath, " CreateWindow called - Starting app initialization\n");
		
		try
		{
			var appShell = new AppShell();
			File.AppendAllText(logPath, " AppShell created successfully\n");
			
			// Navigate to terminal setup on first run, otherwise login.
			var window = new Window(appShell);
			File.AppendAllText(logPath, " Window created successfully\n");
			
			// Use dispatcher to navigate immediately - maximum speed
			Dispatcher.Dispatch(async () =>
			{
				try
				{
					var startupRoute = TerminalConfigurationService.IsConfigured ? "//login" : "//terminalsetup";
					File.AppendAllText(logPath, $" Attempting navigation to {startupRoute}\n");
					await appShell.GoToAsync(startupRoute);
					File.AppendAllText(logPath, $" Navigation to {startupRoute} successful\n");
				}
				catch (Exception ex)
				{
					File.AppendAllText(logPath, $" Navigation error: {ex.Message}\n");
					File.AppendAllText(logPath, $" Stack trace: {ex.StackTrace}\n");
					if (ex.InnerException != null)
					{
						File.AppendAllText(logPath, $" Inner exception: {ex.InnerException.Message}\n");
					}
				}
			});
		
		// Initialize everything in background - completely non-blocking
		_ = Task.Run(async () =>
		{
			try
			{
				if (!TerminalConfigurationService.IsConfigured)
				{
					File.AppendAllText(logPath, "Info: Database initialization skipped until terminal setup is complete\n");
					return;
				}

				File.AppendAllText(logPath, " Starting database initialization...\n");
				// Test database connection (non-blocking)
				var authService = AuthenticationService.Instance;
				var connectionTest = await authService.TestDatabaseConnectionAsync();
				
				if (connectionTest.Success)
				{
					File.AppendAllText(logPath, " " + connectionTest.Message + "\n");
					
					// Ensure default admin user exists
					var result = await authService.EnsureDefaultAdminUserAsync();
					
					if (result.Success)
					{
						File.AppendAllText(logPath, " " + result.Message + "\n");
					}
					
					// Create PIN user "0000" for admin login (system initialization - no auth required)
					try
					{
						var pinResult = await authService.EnsureUserExistsAsync("Admin", "0000", "0000", Models.UserRole.Admin);
						if (pinResult.Success)
						{
							File.AppendAllText(logPath, " PIN user '0000' created successfully\n");
						}
						else if (pinResult.Message.Contains("already exists"))
						{
							File.AppendAllText(logPath, "Info: PIN user '0000' already exists\n");
						}
						else
						{
							File.AppendAllText(logPath, $"  PIN user creation: {pinResult.Message}\n");
						}
					}
					catch (Exception pinEx)
					{
						File.AppendAllText(logPath, $"  PIN user creation error: {pinEx.Message}\n");
					}
				}
				else
				{
					File.AppendAllText(logPath, " Database: " + connectionTest.Message + "\n");
				}
			}
			catch (Exception ex)
			{
				File.AppendAllText(logPath, $" Database init: {ex.Message}\n");
			}
		});
		
		// Initialize cloud services in separate background task
		_ = Task.Run(async () =>
		{
			try
			{
				if (!TerminalConfigurationService.IsConfigured)
				{
					File.AppendAllText(logPath, "Info: Cloud services skipped until terminal setup is complete\n");
					return;
				}

				var onlineMasterCheck = await TerminalRoleService.CanRunOnlineOrderMasterJobsAsync();
				if (!onlineMasterCheck.Allowed)
				{
					File.AppendAllText(logPath, $"Info: Cloud services skipped: {onlineMasterCheck.Reason}\n");
					return;
				}

				File.AppendAllText(logPath, "  Starting cloud services initialization...\n");
				// Delay cloud services initialization to prioritize UI
				await Task.Delay(5000);
				await InitializeCloudServicesAsync();
			}
			catch (Exception ex)
			{
				File.AppendAllText(logPath, $" Cloud services: {ex.Message}\n");
			}
		});
		
		File.AppendAllText(logPath, " Window initialization complete, returning window\n");
		return window;
		}
		catch (Exception ex)
		{
			File.AppendAllText(logPath, $" FATAL ERROR in CreateWindow: {ex.Message}\n");
			File.AppendAllText(logPath, $" Stack trace: {ex.StackTrace}\n");
			throw; // Re-throw to see full error
		}
	}

	private async Task InitializeCloudServicesAsync()
	{
		try
		{
			var onlineMasterCheck = await TerminalRoleService.CanRunOnlineOrderMasterJobsAsync();
			if (!onlineMasterCheck.Allowed)
			{
				System.Diagnostics.Debug.WriteLine($"Cloud services disabled: {onlineMasterCheck.Reason}");
				return;
			}

			var serviceProvider = Current?.Handler?.MauiContext?.Services;
			if (serviceProvider == null)
			{
				System.Diagnostics.Debug.WriteLine("ServiceProvider is NULL during cloud init");
				return;
			}

			_cloudOrderService = serviceProvider.GetService<CloudOrderService>();

			var connectionKeeper = serviceProvider.GetService<OrderWebConnectionKeeperService>();
			if (connectionKeeper != null)
			{
				await connectionKeeper.StartAsync();
				System.Diagnostics.Debug.WriteLine("OrderWeb connection keeper started from app startup");
			}

			_directDatabaseService = serviceProvider.GetService<OrderWebDirectDatabaseService>();
			if (_directDatabaseService != null)
			{
				await InitializeDirectDatabaseAsync();
			}
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Failed to initialize cloud services: {ex.Message}");
		}
	}

	private async Task InitializeDirectDatabaseAsync()
	{
		try
		{
			// Get database service to check for saved connection settings
			var databaseService = Current?.Handler?.MauiContext?.Services?.GetService<DatabaseService>();
			if (databaseService == null) return;

			var config = await databaseService.GetCloudConfigAsync();
			
			// Check if direct database is enabled and configured
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
					System.Diagnostics.Debug.WriteLine(" Configuring direct database connection...");
					
					var success = await _directDatabaseService!.ConfigureDatabaseConnectionAsync(host, database, username, password, port);
					
					if (success)
					{
						await _directDatabaseService.StartRealTimeMonitoringAsync();
						System.Diagnostics.Debug.WriteLine(" Direct database connection established - 0.5s order delivery active!");
					}
					else
					{
						System.Diagnostics.Debug.WriteLine(" Direct database connection failed");
					}
				}
				else
				{
					System.Diagnostics.Debug.WriteLine(" Direct database enabled but configuration incomplete");
				}
			}
			else
			{
				System.Diagnostics.Debug.WriteLine("Info: Direct database connection disabled");
			}
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($" Failed to initialize direct database: {ex.Message}");
		}
	}

	protected override void CleanUp()
	{
		// Stop cloud services when app is closing
		_cloudOrderService?.StopPolling();
		_directDatabaseService?.StopRealTimeMonitoring();
		base.CleanUp();
	}
}
