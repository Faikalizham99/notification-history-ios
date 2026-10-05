using NotificationHistory.Core.Models;
using NotificationHistory.Core.Services;
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
        stack.Add(AppearanceUI.Caption("Saved values · Read only · Tap a value to copy"));
        AddField(stack, "SOURCE APP", entry.SourceApp); AddField(stack, "TITLE", entry.Title);
        AddField(stack, "SUBTITLE", entry.Subtitle); AddField(stack, "MESSAGE", entry.Body);
        var localTime = entry.ReceivedDate;
        AddField(stack, "DATE", localTime.ToString("D")); AddField(stack, "TIME", NotificationTimes.Detail(localTime));
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
        var field = new VerticalStackLayout { Spacing = 7 };
        var caption = AppearanceUI.Caption(name); caption.FontAttributes = FontAttributes.Bold; field.Add(caption);
        var provided = !string.IsNullOrWhiteSpace(value);
        var label = AppearanceUI.Text(provided ? value! : "Not provided", 17);
        label.LineBreakMode = LineBreakMode.WordWrap; label.VerticalOptions = LayoutOptions.Center;
        if (!provided) label.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#62626B"), Color.FromArgb("#A4A4AD"));
        var row = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 12 };
        row.Add(label); var box = AppearanceUI.ValueBox(row);
        if (provided)
        {
            var copy = AppearanceUI.Text("Copy", 12, true); copy.VerticalOptions = LayoutOptions.Center;
            copy.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#087E8B"), Color.FromArgb("#5EDCE1")); row.Add(copy, 1);
            var tap = new TapGestureRecognizer(); tap.Tapped += async (_, _) => { try { await Clipboard.SetTextAsync(value!); SemanticScreenReader.Announce("Copied " + name.ToLowerInvariant()); } catch { await AppServices.AlertAsync("Unable to copy", "Please try again."); } };
            box.GestureRecognizers.Add(tap); SemanticProperties.SetHint(box, "Read only. Tap to copy " + name.ToLowerInvariant());
        }
        field.Add(box); stack.Add(field);
    }
    private static async Task Run(Func<Task> action) { try { await action(); } catch { await AppServices.AlertAsync("Action failed", "The notification could not be updated. Please try again."); } }
}
