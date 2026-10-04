using NotificationHistory.Core.Models;
using NotificationHistory.Services;
namespace NotificationHistory.ViewModels;

public sealed class NotificationDetailViewModel(AppServices services, NotificationEntry entry) : ObservableViewModel
{
    public NotificationEntry Entry { get; } = entry;
    private bool favorite = entry.IsFavorite;
    public bool Favorite { get => favorite; private set => Set(ref favorite, value); }
    public async Task ToggleFavoriteAsync() { await services.Database.SetFavoriteAsync(Entry.Id, !Favorite); Favorite = !Favorite; Entry.IsFavorite = Favorite; services.NotifyChanged(); }
    public async Task DeleteAsync() { await services.Database.DeleteAsync(Entry.Id); services.NotifyChanged(); }
    public string CopyText => string.Join(Environment.NewLine, new[] { Entry.SourceApp, Entry.Title, Entry.Subtitle, Entry.Body, Entry.ReceivedDate.ToString("F") }.Where(x => !string.IsNullOrEmpty(x)));
}
