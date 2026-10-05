using NotificationHistory.Core.Data;
using NotificationHistory.Services;
namespace NotificationHistory;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder().UseMauiApp<App>();
#if IOS
        InputPresentation.Configure();
#endif
        builder.Services.AddSingleton(_ => new NotificationDatabase(StorageLocation.DatabasePath));
        builder.Services.AddSingleton<AppServices>();
        return builder.Build();
    }
}
