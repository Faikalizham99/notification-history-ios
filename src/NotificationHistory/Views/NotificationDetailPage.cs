using NotificationHistory.Core.Models;
using NotificationHistory.Services;
using NotificationHistory.ViewModels;
namespace NotificationHistory.Views;

public sealed class NotificationDetailPage : ContentPage
{
    private readonly AppServices services;
    private readonly NotificationDetailViewModel vm;
    private readonly Button favorite;
    public NotificationDetailPage(AppServices services, NotificationEntry entry)
    {
        this.services = services; vm = new(services, entry); BindingContext = vm; Title = "Notification";
        var stack = new VerticalStackLayout { Padding = 24, Spacing = 16 };
        AddField(stack, "SOURCE APP", entry.SourceApp); AddField(stack, "TITLE", entry.Title);
        AddField(stack, "SUBTITLE", entry.Subtitle); AddField(stack, "MESSAGE", entry.Body);
        AddField(stack, "DATE", entry.ReceivedDate.ToString("D")); AddField(stack, "TIME", entry.ReceivedDate.ToString("T zzz"));
        var copy = new Button { Text = "Copy notification" };
        copy.Clicked += async (_, _) => await Run(async () => { await Clipboard.SetTextAsync(vm.CopyText); SemanticScreenReader.Announce("Copied"); });
        favorite = new Button { Text = entry.IsFavorite ? "Unfavorite" : "Favorite" };
        favorite.Clicked += async (_, _) => await Run(async () => { await vm.ToggleFavoriteAsync(); favorite.Text = vm.Favorite ? "Unfavorite" : "Favorite"; });
        var delete = new Button { Text = "Delete notification", BackgroundColor = Color.FromArgb("#C43C35") };
        delete.Clicked += async (_, _) => await Run(async () => { if (await AppServices.ConfirmAsync("Delete notification?", "This removes it from history and widgets.")) { await vm.DeleteAsync(); await Navigation.PopAsync(); } });
        stack.Add(copy); stack.Add(favorite); stack.Add(delete); Content = new ScrollView { Content = stack };
    }
    protected override async void OnAppearing() { base.OnAppearing(); await Run(() => services.Database.MarkReadAsync(vm.Entry.Id)); }
    private static void AddField(VerticalStackLayout stack, string name, string? value)
    {
        stack.Add(new Label { Text = name, FontSize = 12, TextColor = Colors.Gray, FontAttributes = FontAttributes.Bold });
        var label = new Label { Text = value ?? "Not provided", FontSize = 17 };
        if (!string.IsNullOrEmpty(value))
        {
            var tap = new TapGestureRecognizer(); tap.Tapped += async (_, _) => { try { await Clipboard.SetTextAsync(value); SemanticScreenReader.Announce("Copied " + name.ToLowerInvariant()); } catch { await AppServices.AlertAsync("Unable to copy", "Please try again."); } };
            label.GestureRecognizers.Add(tap); SemanticProperties.SetHint(label, "Tap to copy");
        }
        stack.Add(label);
    }
    private static async Task Run(Func<Task> action) { try { await action(); } catch { await AppServices.AlertAsync("Action failed", "The notification could not be updated. Please try again."); } }
}
