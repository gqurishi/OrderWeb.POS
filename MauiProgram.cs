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
			Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense("Ngo9BigBOggjGyl/VkJ+XU9Gf1RLVGpAY1J0WGBYb1xzflBPallYT3RfQFtjTn9Td0RnUHtbcH1XQmtfVQ==");

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

				// Keep older page aliases on the same real font files so every screen renders consistently.
				fonts.AddFont("OpenSans-Regular.ttf", "InterRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "InterMedium");
				fonts.AddFont("OpenSans-Semibold.ttf", "InterBold");
				fonts.AddFont("OpenSans-Regular.ttf", "AlegreyaRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "AlegreyaBold");
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
		builder.Services.AddSingleton(_ => NavigationCoordinator.Shared);
		builder.Services.AddSingleton<BackgroundSyncManager>();
		builder.Services.AddSingleton<BusinessSettingsService>();
		builder.Services.AddSingleton<TableServiceChargeSettingsService>();
		builder.Services.AddSingleton<TableServiceChargeOrderAuditService>();
		builder.Services.AddSingleton<OrderServiceAvailabilityService>();
		builder.Services.AddSingleton<MenuItemService>();
		builder.Services.AddSingleton<MenuCategoryService>();
		builder.Services.AddSingleton<MealDealService>();
		builder.Services.AddSingleton<TastingMenuService>();
		builder.Services.AddSingleton<OrderRoutingPrintService>();
		builder.Services.AddSingleton<TerminalHealthService>();
		builder.Services.AddSingleton<ClientWebSocketBroadcastService>();
		builder.Services.AddSingleton<MotherConnectionStartupService>();
		builder.Services.AddSingleton<DatabaseBackupService>();
		
		// Note: OrderService registered after PrintService for dependency injection
		
		// Register Restaurant Management Services
		builder.Services.AddSingleton<FloorService>();
		builder.Services.AddSingleton<RestaurantTableService>();
		builder.Services.AddSingleton<TableSessionService>();
		builder.Services.AddSingleton<ReservationSyncService>();
		builder.Services.AddSingleton<OrderWebWebhookRouterService>();
		// Inbound HTTP webhook is intentionally disabled in production.
		// Web orders and reservations use authenticated outbound WebSocket/polling services.
		// MenuService and OrderTakingService removed - using FoodMenu system instead
		
		// Register Cloud Services (Lazy loaded)
		builder.Services.AddSingleton<OnlineOrderApiService>();
		builder.Services.AddSingleton<BackgroundSyncService>();
		builder.Services.AddSingleton<OfflineQueueService>();
		builder.Services.AddSingleton<OrderWebApiClient>();
		builder.Services.AddSingleton<CloudOrderService>();
		builder.Services.AddSingleton<ReceiptService>();
		builder.Services.AddSingleton<CloudSyncService>();
		// Legacy restaurant_local runtime migration is not registered. Production schema
		// changes are applied only by OrderWeb.DatabaseSetup.exe on the Mother terminal.
		
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
		builder.Services.AddSingleton<PrinterRoutingService>();
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
		builder.Services.AddSingleton<KitchenOrderRevisionService>();
		builder.Services.AddSingleton<CollectionReceiptTemplateSettingsService>();
		builder.Services.AddSingleton<DeliveryReceiptTemplateSettingsService>();
		builder.Services.AddSingleton<TableBillReceiptTemplateSettingsService>();
		builder.Services.AddSingleton<TablePaymentReceiptTemplateSettingsService>();
		builder.Services.AddSingleton<ReceiptLogoSettingsService>();
		
		// Register OrderService
		builder.Services.AddSingleton<OrderService>();
		
		// Register OrderWeb UK address lookup service
		builder.Services.AddSingleton<PostcodeLookupService>();
		builder.Services.AddSingleton<OrderWebCustomerCloudService>();
		builder.Services.AddSingleton<CustomerDataService>();
		builder.Services.AddSingleton<DeliveryZoneService>();
		builder.Services.AddSingleton<RiderOperationsService>();
		
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
		builder.Services.AddTransient<WebOrderHistoryPage>();
		builder.Services.AddTransient<RiderPage>();
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

				var backgroundSyncManager = app.Services.GetRequiredService<BackgroundSyncManager>();
				await BackgroundSyncJobRegistrar.RegisterDefaultJobsAsync(app.Services);
				backgroundSyncManager.Start();
				System.Diagnostics.Debug.WriteLine("Background sync manager started");

				var motherConnectionStartup = app.Services.GetRequiredService<MotherConnectionStartupService>();
				await motherConnectionStartup.StartAsync();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($" Background sync manager warning: {ex.Message}");
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
