using Microsoft.Extensions.Logging;

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
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
