#if DEBUG
using NotificationHistory.Core.Models;
using NotificationHistory.Services;
namespace NotificationHistory.Views;

public sealed class DebugPage : ContentPage
{
    public DebugPage(AppServices services)
    {
        Title = "Development tools";
        var source = new Entry { Placeholder = "Source app", Text = "WhatsApp" };
        var title = new Entry { Placeholder = "Title", Text = "Ali" };
        var subtitle = new Entry { Placeholder = "Subtitle (optional)" };
        var body = new Editor { Placeholder = "Message", Text = "Testing notification history 👋\nBro tomorrow jadi? 明天见", HeightRequest = 150 };
        var date = new DatePicker { Date = DateTime.Today };
        var save = new Button { Text = "Insert notification" };
        var samples = new Button { Text = "Insert Maybank and Discord samples" };
        var link = new Button { Text = "Open last inserted deep link" }; long id = 0;
        save.Clicked += async (_, _) =>
        {
            try
            {
                id = await services.Database.SaveAsync(new NotificationEntry
                {
                    SourceApp = source.Text,
                    Title = title.Text,
                    Subtitle = subtitle.Text,
                    Body = body.Text,
                    ReceivedAt = new DateTimeOffset((date.Date ?? DateTime.Today).Date.Add(DateTime.Now.TimeOfDay)).ToUnixTimeMilliseconds()
                });
                services.NotifyChanged(); await AppServices.AlertAsync("Saved", "Test notification added.");
            }
            catch { await AppServices.AlertAsync("Insert failed", "Check shared storage."); }
        };
        samples.Clicked += async (_, _) =>
        {
            try
            {
                await services.Database.SaveAsync(new NotificationEntry { SourceApp = "Maybank", Title = "Transaction Alert", Body = "RM25.00 charged to your card" });
                id = await services.Database.SaveAsync(new NotificationEntry { SourceApp = "Discord", Title = "Monitor ERP", Body = "Robert pushed new changes" }); services.NotifyChanged();
            }
            catch { await AppServices.AlertAsync("Insert failed", "Check shared storage."); }
        };
        link.Clicked += (_, _) => services.OpenLink($"notificationhistory://notification/{id}");
        var stack = new VerticalStackLayout { Padding = 24, Spacing = 12 };
        foreach (var (caption, input) in new (string, InputView)[] { ("SOURCE APP", source), ("TITLE", title), ("SUBTITLE", subtitle), ("MESSAGE", body) })
        { stack.Add(AppearanceUI.Caption(caption)); stack.Add(AppearanceUI.EditField(input)); }
        stack.Add(AppearanceUI.Caption("DATE")); stack.Add(AppearanceUI.DateField(date));
        stack.Add(save); stack.Add(samples); stack.Add(link); Content = new ScrollView { Content = stack };
    }
}
#endif
