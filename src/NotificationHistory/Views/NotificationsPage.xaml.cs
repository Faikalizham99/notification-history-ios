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
    private bool deletingNotification;
    private SwipeView? openSwipe;
    public NotificationsPage(AppServices services) { InitializeComponent(); this.services = services; vm = new(services); BindingContext = vm; }
    protected override async void OnAppearing()
    {
        base.OnAppearing(); services.Changed += OnChanged;
        try
        {
            await services.ApplyAppearanceAsync(); await services.Database.CleanupAsync(); await ReloadAsync();
            if (await services.Database.SettingAsync("onboarded") != "yes") await Navigation.PushAsync(new OnboardingPage(services));
            else { services.MarkNavigationReady(); await services.ConsumeLinkAsync(); }
        }
        catch (Exception error) { await AppServices.StorageAlertAsync(error, "Storage unavailable", "Check App Group signing and free space on the device."); }
    }
    protected override void OnDisappearing() { HistorySearch.Unfocus(); CloseSwipe(); services.Changed -= OnChanged; base.OnDisappearing(); }
    private async Task ReloadAsync(bool debounce = false) { CloseSwipe(); await vm.ReloadAsync(debounce); }
    private void OnChanged(object? sender, EventArgs e) => MainThread.BeginInvokeOnMainThread(async () => await ReloadAsync());
    private async void OnSearch(object? sender, TextChangedEventArgs e)
    { if (vm is not null) { UpdateSearchControls(); vm.Search = e.NewTextValue; await ReloadAsync(true); } }
    private void OnSearchFieldTapped(object? sender, TappedEventArgs e) => HistorySearch.Focus();
    private void UpdateSearchControls() => SearchCancel.IsVisible = HistorySearch.IsFocused || !string.IsNullOrEmpty(HistorySearch.Text);
    private void OnSearchFocusChanged(object? sender, FocusEventArgs e) => UpdateSearchControls();
    private void OnSearchCompleted(object? sender, EventArgs e) => HistorySearch.Unfocus();
    private void OnCancelSearch(object? sender, EventArgs e) { HistorySearch.Text = ""; HistorySearch.Unfocus(); UpdateSearchControls(); }
    private async void OnRefresh(object? sender, EventArgs e)
    {
        try { await services.Database.CleanupAsync(); await ReloadAsync(); }
        catch { await AppServices.AlertAsync("Unable to refresh", "Please try again."); }
        finally { Refresh.IsRefreshing = false; }
    }
    private async void OnLoadMore(object? sender, EventArgs e) { if (vm is not null) await vm.LoadMoreAsync(); }
    private async void OnCardTapped(object? sender, TappedEventArgs e)
    {
        if (openingNotification || deletingNotification || sender is not View { BindingContext: NotificationCardModel card }) return;
        if (openSwipe is not null) { CloseSwipe(); return; }
        openingNotification = true;
        try { await services.OpenDetailAsync(card.Entry.Id); }
        finally { openingNotification = false; }
    }
    private void CloseSwipe() { var swipe = openSwipe; openSwipe = null; swipe?.Close(); }
    private void OnSwipeStarted(object? sender, SwipeStartedEventArgs e)
    { if (openSwipe is not null && !ReferenceEquals(openSwipe, sender)) CloseSwipe(); }
    private void OnSwipeEnded(object? sender, SwipeEndedEventArgs e)
    {
        if (sender is not SwipeView swipe) return;
        if (e.IsOpen && !openingNotification && !deletingNotification) openSwipe = swipe;
        else { if (ReferenceEquals(openSwipe, swipe)) openSwipe = null; if (e.IsOpen) swipe.Close(); }
    }
    private async void OnSwipeDelete(object? sender, EventArgs e)
    {
        if (openingNotification || deletingNotification || sender is not SwipeItemView { CommandParameter: long id } || id <= 0) return;
        deletingNotification = true; CloseSwipe();
        try
        {
            if (!await AppServices.ConfirmAsync("Delete notification?", "This removes it from history and widgets.")) return;
            await services.Database.DeleteAsync(id); services.NotifyChanged();
        }
        catch (Exception error) { await AppServices.StorageAlertAsync(error, "Unable to delete notification", "Could not complete the deletion. Please refresh history and try again."); }
        finally { deletingNotification = false; }
    }
    private async void OnFavoriteFilter(object? sender, EventArgs e) { vm.Favorites = !vm.Favorites; FavoriteButton.Text = vm.Favorites ? "★ Favorites" : "All"; await ReloadAsync(); }
    private async void OnBadgeTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not View { BindingContext: AppBadgeModel badge } || badge.IsSelected) return;
        vm.SelectApp(badge.SourceKey); await ReloadAsync();
    }
    private async void OnSource(object? sender, EventArgs e)
    {
        try
        {
            // Numbered labels distinguish equal display names and reserved action-sheet labels.
            var choices = vm.AppBadges.Select((badge, index) => new { Label = $"{index + 1}. {badge.DisplayName}", Badge = badge }).ToList();
            var choice = await DisplayActionSheetAsync("Source app", "Cancel", null, ["All apps", .. choices.Select(c => c.Label)]);
            if (choice is null or "Cancel") return;
            vm.SelectApp(choice == "All apps" ? null : choices.First(c => c.Label == choice).Badge.SourceKey);
            await ReloadAsync();
            var selected = AppBadgeRow.Children.OfType<View>().FirstOrDefault(v => v.BindingContext is AppBadgeModel { IsSelected: true });
            if (selected is not null) await AppBadgeScroll.ScrollToAsync(selected, ScrollToPosition.MakeVisible, true);
        }
        catch { await AppServices.AlertAsync("Unable to load apps", "Please try again."); }
    }
    private async void OnDate(object? sender, EventArgs e)
    {
        if (FilterDateField.IsVisible) { FilterDateField.IsVisible = false; vm.Date = null; DateButton.Text = "Any date"; await ReloadAsync(); }
        else { initializingDate = true; FilterDate.Date = DateTime.Today; initializingDate = false; FilterDateField.IsVisible = true; vm.Date = DateTime.Today; DateButton.Text = "Clear date"; await ReloadAsync(); }
    }
    private void OnDateFieldTapped(object? sender, TappedEventArgs e) => FilterDate.Focus();
    private async void OnDateSelected(object? sender, DateChangedEventArgs e) { if (vm is not null && !initializingDate) { vm.Date = e.NewDate; await ReloadAsync(); } }
    private async void OnSetup(object? sender, EventArgs e) => await Navigation.PushAsync(new OnboardingPage(services));
    private async void OnApps(object? sender, EventArgs e) => await Navigation.PushAsync(new AppsPage(services));
    private async void OnSettings(object? sender, EventArgs e) => await Navigation.PushAsync(new SettingsPage(services));
}
