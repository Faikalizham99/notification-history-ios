using NotificationHistory.Core.Services;
namespace NotificationHistory.Services;

internal static class SourceAppLauncher
{
    public static Task<bool> OpenAsync(string? value)
    {
        var url = AppOpeningLinks.Normalize(value);
        if (url is null) return Task.FromResult(false);
        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
#if IOS
            using var nativeUrl = new Foundation.NSUrl(url);
            var options = new UIKit.UIApplicationOpenUrlOptions {
                UniversalLinksOnly = new Uri(url).Scheme == Uri.UriSchemeHttps
            };
            // Opening directly supports user-entered schemes without a fixed
            // canOpenURL allowlist. iOS reports whether the launch succeeded.
            return await UIKit.UIApplication.SharedApplication.OpenUrlAsync(nativeUrl, options);
#else
            return await Launcher.Default.OpenAsync(url);
#endif
        });
    }
}
