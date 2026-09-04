using Microsoft.Extensions.Logging;

using Microsoft.Maui.LifecycleEvents;
using OrderWeb.Client.Services;
using OrderWeb.Contracts.Orders;
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

#if DEBUG
		builder.Logging.AddDebug();
#endif


		// Phase 12 — Child order-entry against Mother authoritative processor
		builder.Services.AddSingleton<ClientCacheService>();
		builder.Services.AddSingleton<ClientOrderSession>();
		builder.Services.AddSingleton<AuthoritativeOrderService>();
		builder.Services.AddSingleton<ClientOrderService>();
		builder.Services.AddSingleton<IOrderService>(sp => sp.GetRequiredService<ClientOrderService>());
		builder.Services.AddSingleton<ClientMenuCatalogService>(sp =>
			new ClientMenuCatalogService(
				sp.GetRequiredService<ClientCacheService>(),
				sp.GetRequiredService<AuthoritativeOrderService>()));
		builder.Services.AddSingleton<IMenuCatalogService>(sp => sp.GetRequiredService<ClientMenuCatalogService>());

		return builder.Build();
	}
}
