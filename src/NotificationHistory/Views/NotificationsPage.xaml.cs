using NotificationHistory.Core.Models;
using NotificationHistory.Services;
using NotificationHistory.ViewModels;
namespace NotificationHistory.Views;

public partial class NotificationsPage : ContentPage
{
    private readonly AppServices services;
    private readonly NotificationsViewModel vm;
    private bool initializingDate;
    private bool openingNotification;
    public NotificationsPage(AppServices services) { InitializeComponent(); this.services = services; vm = new(services); BindingContext = vm; }
    protected override async void OnAppearing()
    {
        base.OnAppearing(); services.Changed += OnChanged;
        try
        {
            await services.ApplyAppearanceAsync(); await services.Database.CleanupAsync(); await vm.ReloadAsync();
            if (await services.Database.SettingAsync("onboarded") != "yes") await Navigation.PushAsync(new OnboardingPage(services));
            else { services.MarkNavigationReady(); await services.ConsumeLinkAsync(); }
        }
        catch (Exception error) { await AppServices.StorageAlertAsync(error, "Storage unavailable", "Check App Group signing and free space on the device."); }
    }
    protected override void OnDisappearing() { services.Changed -= OnChanged; base.OnDisappearing(); }
    private void OnChanged(object? sender, EventArgs e) => MainThread.BeginInvokeOnMainThread(async () => await vm.ReloadAsync());
    private async void OnSearch(object? sender, TextChangedEventArgs e) { if (vm is not null) { vm.Search = e.NewTextValue; await vm.ReloadAsync(true); } }
    private async void OnRefresh(object? sender, EventArgs e)
    {
        try { await services.Database.CleanupAsync(); await vm.ReloadAsync(); }
        catch { await AppServices.AlertAsync("Unable to refresh", "Please try again."); }
        finally { Refresh.IsRefreshing = false; }
    }
    private async void OnLoadMore(object? sender, EventArgs e) { if (vm is not null) await vm.LoadMoreAsync(); }
    private async void OnCardTapped(object? sender, TappedEventArgs e)
    {
        if (openingNotification || sender is not View { BindingContext: NotificationCardModel card }) return;
        openingNotification = true;
        try { await services.OpenDetailAsync(card.Entry.Id); }
        finally { openingNotification = false; }
    }
    private async void OnFavoriteFilter(object? sender, EventArgs e) { vm.Favorites = !vm.Favorites; FavoriteButton.Text = vm.Favorites ? "★ Favorites" : "All"; await vm.ReloadAsync(); }
    private async void OnSource(object? sender, EventArgs e)
    {
        try
        {
            var sources = await services.Database.SourcesAsync();
            var choice = await DisplayActionSheetAsync("Source app", "Cancel", null, ["All apps", .. sources]);
            if (choice is null or "Cancel") return;
            vm.Source = choice == "All apps" ? null : choice; SourceButton.Text = choice; await vm.ReloadAsync();
        }
        catch { await AppServices.AlertAsync("Unable to load apps", "Please try again."); }
    }
    private async void OnDate(object? sender, EventArgs e)
    {
        if (FilterDateField.IsVisible) { FilterDateField.IsVisible = false; vm.Date = null; DateButton.Text = "Any date"; await vm.ReloadAsync(); }
        else { initializingDate = true; FilterDate.Date = DateTime.Today; initializingDate = false; FilterDateField.IsVisible = true; vm.Date = DateTime.Today; DateButton.Text = "Clear date"; await vm.ReloadAsync(); }
    }
    private void OnDateFieldTapped(object? sender, TappedEventArgs e) => FilterDate.Focus();
    private async void OnDateSelected(object? sender, DateChangedEventArgs e) { if (vm is not null && !initializingDate) { vm.Date = e.NewDate; await vm.ReloadAsync(); } }
    private async void OnSetup(object? sender, EventArgs e) => await Navigation.PushAsync(new OnboardingPage(services));
    private async void OnApps(object? sender, EventArgs e) => await Navigation.PushAsync(new AppsPage(services));
    private async void OnSettings(object? sender, EventArgs e) => await Navigation.PushAsync(new SettingsPage(services));
}
