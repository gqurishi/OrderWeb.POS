using Microsoft.Extensions.Logging;
using POS_in_NET.Services;
using POS_in_NET.Pages;
using MyFirstMauiApp.Services;
using CommunityToolkit.Maui;
using Syncfusion.Maui.Core.Hosting;
using Microsoft.Maui.LifecycleEvents;

namespace POS_in_NET;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		try
		{
			AppDiagnostics.Log("=== MAUI PROGRAM START ===");

			var builder = MauiApp.CreateBuilder();
			AppDiagnostics.Log("MauiApp.CreateBuilder() completed");
		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit() // Add Community Toolkit support
			.ConfigureSyncfusionCore() // Add Syncfusion support (DataGrid, Charts, PDF, etc.)
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
				// Legacy alias — some screens historically referenced OpenSansBold
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansBold");
			})
			.ConfigureLifecycleEvents(events =>
			{
#if WINDOWS
				events.AddWindows(windows =>
					windows.OnWindowCreated(window =>
						PosWindowService.ApplyLockedFullscreen(window)));
#endif
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
		builder.Services.AddSingleton<InactivityService>();
		builder.Services.AddSingleton<BusinessSettingsService>();
		builder.Services.AddSingleton<TerminalHealthService>();
		builder.Services.AddSingleton<DatabaseBackupService>();
		
		// Note: OrderService registered after PrintService for dependency injection
		
		// Register Restaurant Management Services
		builder.Services.AddSingleton<FloorService>();
		builder.Services.AddSingleton<RestaurantTableService>();
		builder.Services.AddSingleton<TableSessionService>();
		builder.Services.AddSingleton<ReservationSyncService>();
		builder.Services.AddSingleton<OrderWebWebhookRouterService>();
		builder.Services.AddSingleton<OrderWebWebhookListenerService>();
		// MenuService and OrderTakingService removed - using FoodMenu system instead
		
		// Register Cloud Services (Lazy loaded)
		builder.Services.AddSingleton<OnlineOrderApiService>();
		builder.Services.AddSingleton<BackgroundSyncService>();
		builder.Services.AddSingleton<OfflineQueueService>();
		builder.Services.AddSingleton<OrderWebApiClient>();
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
		builder.Services.AddSingleton<OrderWebConnectionKeeperService>();
		
		// Register Loyalty & Gift Card Services
		builder.Services.AddSingleton<OrderWebGiftCardApiService>();
		builder.Services.AddSingleton<GiftCardActivationQueueService>();
		builder.Services.AddSingleton<LoyaltyService>();
		
		// Register PDF Receipt Service (Syncfusion PDF Library)
		builder.Services.AddSingleton<PdfReceiptService>();
		builder.Services.AddSingleton<DailyReportService>();
		builder.Services.AddSingleton<DiscountAuditService>();
		builder.Services.AddSingleton<ZReportService>();
		builder.Services.AddSingleton<TimeClockService>();
		builder.Services.AddSingleton<OrderWebDailyReportSyncService>();
		builder.Services.AddSingleton<ZReportPrintService>();
		
		// Register Report Generation & Scheduling Services
		builder.Services.AddSingleton<ReportGenerationService>();
		builder.Services.AddSingleton<ReportSchedulerService>();
		builder.Services.AddSingleton<ReportHistoryService>();
		
		// Register Network Printer Services
		builder.Services.AddSingleton<NetworkPrinterDatabaseService>();
		builder.Services.AddSingleton<NetworkPrinterService>();
		builder.Services.AddSingleton<CashDrawerService>();
		builder.Services.AddSingleton<TillExpenseService>();
		builder.Services.AddSingleton<CashDrawerFlowService>();
		builder.Services.AddSingleton<EscPosBuilder>();
		builder.Services.AddSingleton<PrinterHealthService>();
		builder.Services.AddSingleton<NetworkPrintQueueService>();
		builder.Services.AddSingleton<OnlineOrderAutoPrintService>();
		builder.Services.AddSingleton<PrintGroupService>();
		builder.Services.AddSingleton<PrintingPolicyService>();
		builder.Services.AddSingleton<KitchenTemplateSettingsService>();
		builder.Services.AddSingleton<CollectionReceiptTemplateSettingsService>();
		builder.Services.AddSingleton<DeliveryReceiptTemplateSettingsService>();
		builder.Services.AddSingleton<TableBillReceiptTemplateSettingsService>();
		builder.Services.AddSingleton<TablePaymentReceiptTemplateSettingsService>();
		
		// Register OrderService
		builder.Services.AddSingleton<OrderService>();
		
		// Register OrderWeb UK address lookup service
		builder.Services.AddSingleton<PostcodeLookupService>();
		builder.Services.AddSingleton<OrderWebCustomerCloudService>();
		builder.Services.AddSingleton<CustomerDataService>();
		builder.Services.AddSingleton<DeliveryZoneService>();
		
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
		builder.Services.AddTransient<CloudSettingsPage>();
		builder.Services.AddTransient<PostcodeLookupPage>();
		builder.Services.AddTransient<WebOrdersPage>();
		builder.Services.AddTransient<GiftCardPage>();
		builder.Services.AddTransient<LoyaltyPointsPage>();
		builder.Services.AddTransient<ReportPage>();
		builder.Services.AddTransient<InventoryPage>();
		builder.Services.AddTransient<TerminalHealthPage>();
		builder.Services.AddTransient<CustomerDataPage>();
		builder.Services.AddTransient<TerminalSetupPage>();
		builder.Services.AddTransient<InitialAdminSetupPage>();
		builder.Services.AddTransient<PrintTemplatesPage>();

		var app = builder.Build();

		Task.Run(async () =>
		{
			try
			{
				await Task.Delay(3000);
				TerminalPowerSafetyService.Apply();

				var terminalHealthService = app.Services.GetRequiredService<TerminalHealthService>();
				terminalHealthService.Start();
				System.Diagnostics.Debug.WriteLine("Terminal health heartbeat started");

				await Task.Delay(2000);
				DatabaseChangeMonitorService.Start();
				System.Diagnostics.Debug.WriteLine("Live database change monitor started");
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($" Live database change monitor warning: {ex.Message}");
			}
		});

		Task.Run(async () =>
		{
			try
			{
				if (!TerminalRoleService.CanRunMotherJobs)
				{
					System.Diagnostics.Debug.WriteLine("Database backup scheduler skipped on unconfigured/child terminal");
					return;
				}

				await Task.Delay(3000);
				var backupService = app.Services.GetRequiredService<DatabaseBackupService>();
				backupService.Start();
				System.Diagnostics.Debug.WriteLine(" Database backup scheduler started");
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($" Database backup scheduler warning: {ex.Message}");
			}
		});

		//  Initialize Database Cleanup System
		Task.Run(async () =>
		{
			try
			{
				if (!TerminalRoleService.CanRunMotherJobs)
				{
					System.Diagnostics.Debug.WriteLine("Cleanup scheduler skipped on unconfigured/child terminal");
					return;
				}

				await Task.Delay(8000);
				var cleanupScheduler = app.Services.GetRequiredService<CleanupSchedulerService>();
				
				// Create database indexes for faster queries (first run only)
				await cleanupScheduler.InitializeDatabaseAsync();
				
				// Start automatic cleanup scheduler (checks every hour, cleans every 24h)
				cleanupScheduler.Start();
				
				System.Diagnostics.Debug.WriteLine(" Database cleanup system initialized");
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($" Cleanup initialization warning: {ex.Message}");
			}
		});

		//  Initialize Report Generation Scheduler
		Task.Run(async () =>
		{
			try
			{
				if (!TerminalRoleService.CanRunMotherJobs)
				{
					System.Diagnostics.Debug.WriteLine("Report scheduler skipped on unconfigured/child terminal");
					return;
				}

				// Small delay to let database initialize
				await Task.Delay(2000);
				
				var reportScheduler = app.Services.GetRequiredService<ReportSchedulerService>();
				
				// Start automatic report generation scheduler (runs nightly at 2:00 AM)
				reportScheduler.Start();
				
				System.Diagnostics.Debug.WriteLine(" Report generation scheduler initialized");
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($" Report scheduler initialization warning: {ex.Message}");
			}
		});

		//  Initialize Network Printer Services
		Task.Run(async () =>
		{
			try
			{
				if (!TerminalConfigurationService.IsConfigured)
				{
					System.Diagnostics.Debug.WriteLine("Printer services skipped until terminal setup is complete");
					return;
				}

				// Small delay to let database initialize
				await Task.Delay(1500);
				
				var printerDbService = app.Services.GetRequiredService<NetworkPrinterDatabaseService>();
				var cashDrawerService = app.Services.GetRequiredService<CashDrawerService>();
				var healthService = app.Services.GetRequiredService<PrinterHealthService>();
				var queueService = app.Services.GetRequiredService<NetworkPrintQueueService>();
				var printingPolicyService = app.Services.GetRequiredService<PrintingPolicyService>();
				
				// Ensure printer tables exist
				await printerDbService.EnsureTablesExistAsync();
				await cashDrawerService.EnsureTableExistsAsync();
				await printingPolicyService.EnsureDefaultsAsync();
				
				// Start health monitoring (checks every 30 seconds)
				healthService.Start();
				System.Diagnostics.Debug.WriteLine(" Printer health monitoring started");
				
				var queueOwnership = await printingPolicyService.CanProcessSharedQueueAsync();
				if (queueOwnership.Allowed)
				{
					await queueService.StartAsync();
					System.Diagnostics.Debug.WriteLine($" Print queue processor started: {queueOwnership.Reason}");
				}
				else
				{
					System.Diagnostics.Debug.WriteLine($"Info: Print queue processor skipped: {queueOwnership.Reason}");
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($" Printer services initialization warning: {ex.Message}");
			}
		});

		//  Auto-connect OrderWeb.net services on app startup
		Task.Run(async () =>
		{
			try
			{
				await Task.Delay(500);
				System.Diagnostics.Debug.WriteLine("OrderWeb.net startup is handled by App.InitializeCloudServicesAsync to prevent duplicate background jobs.");
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"OrderWeb.net auto-connect error: {ex.Message}");
			}
		});
		
		AppDiagnostics.Log("MauiProgram.CreateMauiApp completed successfully");

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

			AppDiagnostics.Log("Returning MauiApp instance");
			return app;
		}
		catch (Exception ex)
		{
			AppDiagnostics.LogFatal("CreateMauiApp", ex);
			throw;
		}
	}
}
