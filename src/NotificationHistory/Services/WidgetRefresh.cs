using System.Runtime.InteropServices;
namespace NotificationHistory.Services;

internal static class WidgetRefresh
{
#if IOS
    [DllImport("__Internal", EntryPoint = "nh_reload_widgets")]
    private static extern void ReloadNative();
#endif
    public static void Reload()
    {
#if IOS
        ReloadNative();
#endif
    }
}
