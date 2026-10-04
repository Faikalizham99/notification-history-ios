using Foundation;

namespace NotificationHistory;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    public override bool OpenUrl(UIKit.UIApplication application, NSUrl url, NSDictionary options)
    {
        if (url.Scheme != "notificationhistory") return false;
        Microsoft.Maui.IPlatformApplication.Current!.Services.GetRequiredService<Services.AppServices>().OpenLink(url.AbsoluteString!);
        return true;
    }
}
