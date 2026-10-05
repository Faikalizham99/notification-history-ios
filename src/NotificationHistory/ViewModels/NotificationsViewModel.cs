using System.Collections.ObjectModel;
using NotificationHistory.Core.Models;
using NotificationHistory.Core.Services;
using NotificationHistory.Services;
namespace NotificationHistory.ViewModels;

public sealed class NotificationGroup(string title) : ObservableCollection<NotificationCardModel> { public string Title { get; } = title; }
public sealed class NotificationsViewModel(AppServices services) : ObservableViewModel
{
    public ObservableCollection<NotificationGroup> Groups { get; } = [];
    public ObservableCollection<AppBadgeModel> AppBadges { get; } = [];
    private CancellationTokenSource? searchCancellation;
    private readonly SemaphoreSlim loading = new(1, 1);
    private PageCursor? cursor;
    private bool more = true, busy;
    private int generation;
    private bool appSelectionInitialized;
    private string? selectedAppKey;
    private Dictionary<string, AppProfile> profiles = new(StringComparer.Ordinal);
    public bool Busy { get => busy; private set => Set(ref busy, value); }
    public string Search { get; set; } = "";
    public bool Favorites { get; set; }
    public string SelectedAppName => AppBadges.FirstOrDefault(b => b.SourceKey == selectedAppKey)?.DisplayName ?? "All apps";
    public bool HasAppBadges => AppBadges.Count > 0;
    public string EmptyMessage => !HasAppBadges
        ? "Saved notifications will appear here. Set up a Shortcut to start capturing."
        : selectedAppKey is null ? "No notifications match these filters. Try changing your search, favorites, or date."
        : $"No notifications match for {SelectedAppName}. Try changing your filters, or capture a notification through your Shortcut.";
    public DateTime? Date { get; set; }
    public async Task ReloadAsync(bool debounce = false)
    {
        var current = ++generation;
        searchCancellation?.Cancel(); searchCancellation?.Dispose();
        searchCancellation = new CancellationTokenSource(); var token = searchCancellation.Token;
        try
        {
            if (debounce) await Task.Delay(250, token);
            await loading.WaitAsync(token);
            try
            {
                Busy = true;
                var savedProfiles = await services.Appearance.LoadAsync();
                var counts = await services.Database.SourceCountsAsync(token);
                if (current != generation) return;
                profiles = savedProfiles.ToDictionary(p => p.SourceKey, StringComparer.Ordinal);
                UpdateBadges(AppBadgeCatalog.Build(savedProfiles, counts));
                var rows = await services.Database.QueryAsync(Filter(), token: token);
                if (current != generation) return;
                Groups.Clear(); cursor = null; more = true; Append(rows);
            }
            finally { Busy = false; loading.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { await AppServices.StorageAlertAsync(error, "History unavailable", "Could not read history. Check device storage and try again."); }
    }
    public async Task LoadMoreAsync()
    {
        if (!more || !await loading.WaitAsync(0)) return;
        var current = generation;
        try { var rows = await services.Database.QueryAsync(Filter(), cursor); if (current == generation) Append(rows); }
        catch { await AppServices.AlertAsync("Unable to load more", "Try scrolling again in a moment."); }
        finally { loading.Release(); }
    }
    private NotificationFilter Filter()
    {
        long? from = null, to = null;
        if (Date is DateTime date)
        {
            from = new DateTimeOffset(DateTime.SpecifyKind(date.Date, DateTimeKind.Local)).ToUnixTimeMilliseconds();
            to = new DateTimeOffset(DateTime.SpecifyKind(date.Date.AddDays(1), DateTimeKind.Local)).ToUnixTimeMilliseconds();
        }
        var selected = AppBadges.FirstOrDefault(b => b.SourceKey == selectedAppKey);
        return new(Search, Favorites, From: from, To: to, Sources: selected?.Data.Sources);
    }
    public void SelectApp(string? key)
    {
        selectedAppKey = key; appSelectionInitialized = true;
        foreach (var badge in AppBadges) badge.IsSelected = badge.SourceKey == selectedAppKey;
        Raise(nameof(SelectedAppName)); Raise(nameof(EmptyMessage));
    }
    private void UpdateBadges(List<AppBadgeData> catalog)
    {
        var keys = catalog.Select(b => b.Profile.SourceKey).ToHashSet(StringComparer.Ordinal);
        for (var i = AppBadges.Count - 1; i >= 0; i--)
            if (!keys.Contains(AppBadges[i].SourceKey)) AppBadges.RemoveAt(i);
        for (var i = 0; i < catalog.Count; i++)
        {
            var data = catalog[i]; var image = services.Appearance.ImagePath(data.Profile.ImageFile);
            var icon = image is null ? null : ImageSource.FromFile(image);
            var badge = AppBadges.FirstOrDefault(b => b.SourceKey == data.Profile.SourceKey);
            if (badge is null) AppBadges.Insert(i, new(data, icon));
            else
            {
                badge.Update(data, icon);
                var previous = AppBadges.IndexOf(badge);
                if (previous != i) AppBadges.Move(previous, i);
            }
        }
        if ((!appSelectionInitialized || selectedAppKey is not null && !keys.Contains(selectedAppKey)) && catalog.Count > 0)
            SelectApp(catalog[0].Profile.SourceKey);
        else if (selectedAppKey is not null && catalog.Count == 0)
        { selectedAppKey = null; appSelectionInitialized = false; }
        foreach (var badge in AppBadges) badge.IsSelected = badge.SourceKey == selectedAppKey;
        Raise(nameof(HasAppBadges)); Raise(nameof(SelectedAppName)); Raise(nameof(EmptyMessage));
    }
    private void Append(List<NotificationEntry> rows)
    {
        foreach (var entry in rows)
        {
            var date = entry.ReceivedDate.Date;
            var title = date == DateTime.Today ? "Today" : date == DateTime.Today.AddDays(-1) ? "Yesterday" : date.ToString("ddd, d MMM yyyy");
            var group = Groups.LastOrDefault();
            if (group?.Title != title) { group = new(title); Groups.Add(group); }
            var profile = profiles.GetValueOrDefault(AppProfile.Key(entry.SourceApp)) ?? AppProfile.Default(entry.SourceApp);
            var image = services.Appearance.ImagePath(profile.ImageFile);
            group.Add(new(entry, profile, image is null ? null : ImageSource.FromFile(image)));
        }
        more = rows.Count == 60;
        if (rows.LastOrDefault() is { } last) cursor = new(last.ReceivedAt, last.Id);
    }
}
