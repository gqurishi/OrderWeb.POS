using Microsoft.Extensions.Logging;
using POS_in_NET.Services;
using POS_in_NET.Pages;
using CommunityToolkit.Maui;
using Syncfusion.Maui.Core.Hosting;

namespace POS_in_NET;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		// Register Syncfusion license - Essential Studio® UI Edition v31.x Binary License
		Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense("Ngo9BigBOggjGyl/Vkd+XU9FcVRDX3xKf0x/TGpQb19xflBPallYVBYiSV9jS3tSdEVkWHZddHdUQmFfU091Xg==");
		
		try
		{
			var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "pos-debug.log");
			File.AppendAllText(logPath, $"\n\n=== MAUI PROGRAM START {DateTime.Now} ===\n");
			
			var builder = MauiApp.CreateBuilder();
			File.AppendAllText(logPath, "✅ MauiApp.CreateBuilder() completed\n");
		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit() // Add Community Toolkit support
			.ConfigureSyncfusionCore() // Add Syncfusion support (DataGrid, Charts, PDF, etc.)
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
				
				// OrderWeb.net Design System - Alegreya Font
				fonts.AddFont("Alegreya-Regular.ttf", "AlegreyaRegular");
				fonts.AddFont("Alegreya-Bold.ttf", "AlegreyaBold");
				fonts.AddFont("Alegreya-Italic.ttf", "AlegreyaItalic");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		// Register Core Services (Lazy initialization for faster startup)
		builder.Services.AddSingleton<DatabaseService>();
		builder.Services.AddSingleton<OrderLifecycleRolloutService>();
		builder.Services.AddSingleton(_ => AuthenticationService.Instance);
		builder.Services.AddSingleton<RoleAccessService>();
		builder.Services.AddSingleton<PermissionService>();
		builder.Services.AddSingleton<BusinessSettingsService>();
		
		// Note: OrderService registered after PrintService for dependency injection
		
		// Register Restaurant Management Services
		builder.Services.AddSingleton<FloorService>();
		builder.Services.AddSingleton<RestaurantTableService>();
		builder.Services.AddSingleton<TableSessionService>();
		// MenuService and OrderTakingService removed - using FoodMenu system instead
		
		// Register Cloud Services (Lazy loaded)
		builder.Services.AddSingleton<OnlineOrderApiService>();
		builder.Services.AddSingleton<BackgroundSyncService>();
		builder.Services.AddSingleton<CloudOrderService>();
		builder.Services.AddSingleton<ReceiptService>();
		builder.Services.AddSingleton<CloudSyncService>();
		builder.Services.AddSingleton<DatabaseMigrationService>();
		
		// Register Direct Database & Connection Services
		builder.Services.AddSingleton<OrderWebDirectDatabaseService>();
		builder.Services.AddSingleton<ConnectionManager>();
		builder.Services.AddSingleton<PaymentFixService>();
		
		// Register OrderWeb.net Integration Services
		builder.Services.AddSingleton<OrderWebWebSocketService>();
		builder.Services.AddSingleton<OrderWebRestApiService>();
		builder.Services.AddSingleton<HeartbeatService>();
		
		// Register Loyalty & Gift Card Services
		builder.Services.AddSingleton<LoyaltyService>();
		
		// Register PDF Receipt Service (Syncfusion PDF Library)
		builder.Services.AddSingleton<PdfReceiptService>();
		builder.Services.AddSingleton<DailyReportService>();
		
		// Register Report Generation & Scheduling Services
		builder.Services.AddSingleton<ReportGenerationService>();
		builder.Services.AddSingleton<ReportSchedulerService>();
		builder.Services.AddSingleton<ReportHistoryService>();
		
		// Register Network Printer Services
		builder.Services.AddSingleton<NetworkPrinterDatabaseService>();
		builder.Services.AddSingleton<NetworkPrinterService>();
		builder.Services.AddSingleton<EscPosBuilder>();
		builder.Services.AddSingleton<PrinterHealthService>();
		builder.Services.AddSingleton<NetworkPrintQueueService>();
		builder.Services.AddSingleton<OnlineOrderAutoPrintService>();
		
		// Register OrderService
		builder.Services.AddSingleton<OrderService>();
		
		// Register Postcode Lookup Service (Mapbox/Custom PAF)
		builder.Services.AddSingleton<PostcodeLookupService>();
		
		// Register Database Cleanup Services (3-month rolling data)
		builder.Services.AddSingleton<DatabaseCleanupService>();
		builder.Services.AddSingleton<CleanupSchedulerService>();

		// Register Simplified Web Order Service (removed for clean restart)
		// builder.Services.AddSingleton<SimpleWebOrderService>();

		// Register Advanced Web Order Services (disabled for now)
		// builder.Services.AddSingleton<WebOrderSyncService>();
		// builder.Services.AddSingleton<PrintJobService>();

		// Register Web Order Services (temporarily disabled for initial setup)
		// builder.Services.AddSingleton<WebOrderAccessService>();
		// builder.Services.AddSingleton<DatabaseSchemaService>();

		// Register Printer Services (temporarily disabled due to cross-platform issues)
		// builder.Services.AddSingleton<PrinterDiscoveryService>();
		// builder.Services.AddSingleton<PrinterManagementService>();

		// Register Pages
		builder.Services.AddTransient<LoginPage>();
		builder.Services.AddTransient<DashboardPage>();
		builder.Services.AddTransient<ManagerDashboardPage>();
		// OrderTakingPage removed - using FoodMenuPage instead
		builder.Services.AddTransient<RestaurantPage>();
		builder.Services.AddTransient<ReservationPage>();
		builder.Services.AddTransient<VisualTablePage>();
		builder.Services.AddTransient<FloorPage>();
		builder.Services.AddTransient<TablePage>();
		builder.Services.AddTransient<OrderManagementPage>();
		// MenuManagementPage removed - using FoodMenuPage instead
		builder.Services.AddTransient<UserManagementPage>();
		builder.Services.AddTransient<BusinessSettingsPage>();
		builder.Services.AddTransient<CloudSettingsPage>();
		builder.Services.AddTransient<PostcodeLookupPage>();
		builder.Services.AddTransient<WebOrdersPage>();
		builder.Services.AddTransient<GiftCardPage>();
		builder.Services.AddTransient<LoyaltyPointsPage>();
		builder.Services.AddTransient<ReportPage>();
		builder.Services.AddTransient<InventoryPage>();

		var app = builder.Build();

		// ✅ Initialize Database Cleanup System
		Task.Run(async () =>
		{
			try
			{
				var cleanupScheduler = app.Services.GetRequiredService<CleanupSchedulerService>();
				
				// Create database indexes for faster queries (first run only)
				await cleanupScheduler.InitializeDatabaseAsync();
				
				// Start automatic cleanup scheduler (checks every hour, cleans every 24h)
				cleanupScheduler.Start();
				
				System.Diagnostics.Debug.WriteLine("✅ Database cleanup system initialized");
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"⚠️ Cleanup initialization warning: {ex.Message}");
			}
		});

		// ✅ Initialize Report Generation Scheduler
		Task.Run(async () =>
		{
			try
			{
				// Small delay to let database initialize
				await Task.Delay(2000);
				
				var reportScheduler = app.Services.GetRequiredService<ReportSchedulerService>();
				
				// Start automatic report generation scheduler (runs nightly at 11:50 PM)
				reportScheduler.Start();
				
				System.Diagnostics.Debug.WriteLine("✅ Report generation scheduler initialized");
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"⚠️ Report scheduler initialization warning: {ex.Message}");
			}
		});

		// ✅ Initialize Network Printer Services
		Task.Run(async () =>
		{
			try
			{
				// Small delay to let database initialize
				await Task.Delay(1500);
				
				var printerDbService = app.Services.GetRequiredService<NetworkPrinterDatabaseService>();
				var healthService = app.Services.GetRequiredService<PrinterHealthService>();
				var queueService = app.Services.GetRequiredService<NetworkPrintQueueService>();
				
				// Ensure printer tables exist
				await printerDbService.EnsureTablesExistAsync();
				
				// Start health monitoring (checks every 30 seconds)
				healthService.Start();
				System.Diagnostics.Debug.WriteLine("✅ Printer health monitoring started");
				
				// Start print queue processor (processes every 5 seconds)
				await queueService.StartAsync();
				System.Diagnostics.Debug.WriteLine("✅ Print queue processor started");
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"⚠️ Printer services initialization warning: {ex.Message}");
			}
		});

		// ✅ Auto-connect OrderWeb.net services on app startup
		Task.Run(async () =>
		{
			try
			{
				// Small delay to let app fully initialize
				await Task.Delay(2000);
				
				var dbService = app.Services.GetRequiredService<DatabaseService>();
				var wsService = app.Services.GetRequiredService<OrderWebWebSocketService>();
				var cloudService = app.Services.GetRequiredService<CloudOrderService>();
				var heartbeatService = app.Services.GetRequiredService<HeartbeatService>();
				var autoPrintService = app.Services.GetRequiredService<OnlineOrderAutoPrintService>();
				
				// Link auto-print service to cloud service
				cloudService.SetAutoPrintService(autoPrintService);
				System.Diagnostics.Debug.WriteLine("OnlineOrderAutoPrintService linked to CloudOrderService");
				
				// Get configuration from database
				var config = await dbService.GetCloudConfigAsync();
				var isEnabled = config.GetValueOrDefault("is_enabled") == "True";
				var tenantSlug = config.GetValueOrDefault("tenant_slug", "");
				var apiKey = config.GetValueOrDefault("api_key", "");
				var wsUrl = config.GetValueOrDefault("websocket_url", "wss://orderweb.net/ws/pos");
				var restApiUrl = config.GetValueOrDefault("rest_api_url", "https://orderweb.net/api");
				
				if (isEnabled && !string.IsNullOrEmpty(tenantSlug) && !string.IsNullOrEmpty(apiKey))
				{
					System.Diagnostics.Debug.WriteLine("Auto-connecting OrderWeb.net services...");
					
					// Configure and connect WebSocket
					wsService.Configure(wsUrl, tenantSlug, apiKey);
					bool connected = await wsService.ConnectAsync();
					
					if (connected)
					{
						System.Diagnostics.Debug.WriteLine("WebSocket auto-connected successfully!");
					}
					else
					{
						System.Diagnostics.Debug.WriteLine("WebSocket auto-connect failed");
					}
					
					// Start background polling
					await cloudService.StartPollingAsync();
					System.Diagnostics.Debug.WriteLine("Cloud order polling started");
					
					// Start heartbeat service
					await heartbeatService.StartAsync();
					System.Diagnostics.Debug.WriteLine("Heartbeat service started");
				}
				else
				{
					System.Diagnostics.Debug.WriteLine("OrderWeb.net not configured or disabled");
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"OrderWeb.net auto-connect error: {ex.Message}");
			}
		});
		
		File.AppendAllText(logPath, "✅ MauiProgram.CreateMauiApp completed successfully\n");

		// Database initialization can be added later when web order services are fully integrated
		// Task.Run(async () =>
		// {
		//     try
		//     {
		//         var schemaService = app.Services.GetRequiredService<DatabaseSchemaService>();
		//         await schemaService.InitializeWebOrderTablesAsync();
		//         await schemaService.CreateIndexesForPerformanceAsync();
		//         await schemaService.VerifySchemaIntegrityAsync();
		//     }
		//     catch (Exception ex)
		//     {
		//         System.Diagnostics.Debug.WriteLine($"Database initialization error: {ex.Message}");
		//     }
		// });

			File.AppendAllText(logPath, "✅ Returning MauiApp instance\n");
			return app;
		}
		catch (Exception ex)
		{
			var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "pos-debug.log");
			File.AppendAllText(logPath, $"❌ FATAL ERROR in CreateMauiApp: {ex.Message}\n");
			File.AppendAllText(logPath, $"Stack Trace: {ex.StackTrace}\n");
			
			// Show error dialog to user
			if (ex.InnerException != null)
			{
				File.AppendAllText(logPath, $"Inner Exception: {ex.InnerException.Message}\n");
			}
			
			// Re-throw to see in crash logs
			throw;
		}
	}
}
