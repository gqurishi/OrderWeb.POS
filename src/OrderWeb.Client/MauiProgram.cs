using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using OrderWeb.Client.Services;
using OrderWeb.Client.Services.Customer;
using OrderWeb.Client.Services.Orders;
using OrderWeb.Contracts.Services;

namespace OrderWeb.Client;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansBold");
				fonts.AddFont("OpenSans-Semibold.ttf", "InterMedium");
				fonts.AddFont("OpenSans-Semibold.ttf", "InterBold");
				fonts.AddFont("Alegreya-Regular.ttf", "AlegreyaRegular");
				fonts.AddFont("Alegreya-Bold.ttf", "AlegreyaBold");
				fonts.AddFont("Alegreya-Italic.ttf", "AlegreyaItalic");
			})
			.ConfigureLifecycleEvents(events =>
			{
#if WINDOWS
				events.AddWindows(windows =>
					windows.OnWindowCreated(window =>
						ClientWindowService.ApplyLockedFullscreen(window)));
#endif
			});

		builder.Services.AddSingleton<ClientCacheService>();
		builder.Services.AddSingleton<ClientCustomerFieldPolicyService>();
		builder.Services.AddSingleton<ClientMotherSyncStatusService>();
		builder.Services.AddSingleton<MotherCustomerClient>();
		builder.Services.AddSingleton<MotherOrderQueryClient>();
		builder.Services.AddSingleton<ICustomerDirectoryService, ClientCustomerDirectoryService>();
		builder.Services.AddSingleton<ICollectionDetailsService, ClientCollectionDetailsService>();
		builder.Services.AddSingleton<IDeliveryDetailsService, ClientDeliveryDetailsService>();
		builder.Services.AddSingleton<IOpenOrderListService, ClientOpenOrderListService>();
		builder.Services.AddSingleton<IOrderSearchService, ClientOrderSearchService>();
		builder.Services.AddSingleton<IOrderHistoryService, ClientOrderHistoryService>();
		builder.Services.AddSingleton<ICustomerPreviousOrdersService, ClientCustomerPreviousOrdersService>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
