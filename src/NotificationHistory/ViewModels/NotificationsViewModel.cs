using System.Collections.ObjectModel;
using NotificationHistory.Core.Models;
using NotificationHistory.Services;
namespace NotificationHistory.ViewModels;

public sealed class NotificationGroup(string title) : ObservableCollection<NotificationEntry> { public string Title { get; } = title; }
public sealed class NotificationsViewModel(AppServices services) : ObservableViewModel
{
    public ObservableCollection<NotificationGroup> Groups { get; } = [];
    private CancellationTokenSource? searchCancellation;
    private readonly SemaphoreSlim loading = new(1, 1);
    private PageCursor? cursor;
    private bool more = true, busy;
    private int generation;
    public bool Busy { get => busy; private set => Set(ref busy, value); }
    public string Search { get; set; } = "";
    public bool Favorites { get; set; }
    public string? Source { get; set; }
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
                Busy = true; var rows = await services.Database.QueryAsync(Filter(), token: token);
                if (current != generation) return;
                Groups.Clear(); cursor = null; more = true; Append(rows);
            }
            finally { Busy = false; loading.Release(); }
        }
        catch (OperationCanceledException) { }
        catch { await AppServices.AlertAsync("History unavailable", "Could not read history. Check device storage and try again."); }
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
        return new(Search, Favorites, Source, from, to);
    }
    private void Append(List<NotificationEntry> rows)
    {
        foreach (var entry in rows)
        {
            var date = entry.ReceivedDate.Date;
            var title = date == DateTime.Today ? "Today" : date == DateTime.Today.AddDays(-1) ? "Yesterday" : date.ToString("ddd, d MMM yyyy");
            var group = Groups.LastOrDefault();
            if (group?.Title != title) { group = new(title); Groups.Add(group); }
            group.Add(entry);
        }
        more = rows.Count == 60;
        if (rows.LastOrDefault() is { } last) cursor = new(last.ReceivedAt, last.Id);
    }
}
