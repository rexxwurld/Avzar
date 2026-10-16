using Microsoft.Extensions.Logging;
namespace Avzar.Mobile;
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
#if DEBUG
        builder.Logging.AddDebug();
#endif
        builder.Services.AddSingleton<AvzarApi>();
        builder.Services.AddSingleton<OfflineSyncQueue>();
        builder.Services.AddSingleton<BusinessEventsClient>();
        return builder.Build();
    }
}
