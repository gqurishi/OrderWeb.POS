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
				// Match Mother: SharedUI "Alegreya*" aliases resolve to OpenSans so titles/labels look the same.
				fonts.AddFont("OpenSans-Regular.ttf", "AlegreyaRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "AlegreyaBold");
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

		// Client POS uses the canonical SharedUI touch keyboard for every Entry/Editor,
		// including controls declared in XAML and controls created dynamically in code.
		Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("OrderWebSharedTouchKeyboard", (_, entry) =>
		{
			if (entry is Entry control)
				OrderWeb.SharedUI.Controls.SharedTouchKeyboard.SetEnabled(control, true);
		});
		Microsoft.Maui.Handlers.EditorHandler.Mapper.AppendToMapping("OrderWebSharedTouchKeyboard", (_, editor) =>
		{
			if (editor is Editor control)
				OrderWeb.SharedUI.Controls.SharedTouchKeyboard.SetEnabled(control, true);
		});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		builder.Services.AddSingleton<MotherAuthClient>();
		builder.Services.AddSingleton<MotherCashierClient>();
		builder.Services.AddSingleton<ClientAuthenticationService>();
		builder.Services.AddSingleton<OrderWeb.Contracts.Services.IAuthenticationService>(sp => sp.GetRequiredService<ClientAuthenticationService>());
		builder.Services.AddSingleton<ClientCacheService>();

		return builder.Build();
	}
}
