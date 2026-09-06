using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Microsoft.Maui.LifecycleEvents;
using OrderWeb.Client.Services;

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

		builder.Services.AddSingleton<MotherAuthClient>();
		builder.Services.AddSingleton<ClientAuthenticationService>();
		builder.Services.AddSingleton<OrderWeb.Contracts.Services.IAuthenticationService>(sp => sp.GetRequiredService<ClientAuthenticationService>());
		builder.Services.AddSingleton<ClientCacheService>();

		return builder.Build();
	}
}
