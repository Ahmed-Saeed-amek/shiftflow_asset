using Microsoft.Extensions.Logging;

namespace ShiftFlow.Mobile;

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
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

#if DEBUG && ANDROID
		Microsoft.Maui.Handlers.WebViewHandler.Mapper.AppendToMapping("DevSslWebViewClient", (handler, _) =>
		{
			if (handler is Microsoft.Maui.Handlers.WebViewHandler concreteHandler)
				concreteHandler.PlatformView.SetWebViewClient(new Platforms.Android.DevSslWebViewClient(concreteHandler));
		});
#endif

		return builder.Build();
	}
}
