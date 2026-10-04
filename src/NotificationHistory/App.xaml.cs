using NotificationHistory.Services;
using NotificationHistory.Views;
namespace NotificationHistory;

public partial class App : Application
{
    private readonly AppServices services;
    public App(AppServices services) { InitializeComponent(); this.services = services; }
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new NavigationPage(new NotificationsPage(services)));
        window.Resumed += async (_, _) => await services.RefreshAsync();
        return window;
    }
    protected override void OnAppLinkRequestReceived(Uri uri) { base.OnAppLinkRequestReceived(uri); services.OpenLink(uri.ToString()); }
}
