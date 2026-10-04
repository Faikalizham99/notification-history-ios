using NotificationHistory.Services;
namespace NotificationHistory.Views;

public sealed class OnboardingPage : ContentPage
{
    public OnboardingPage(AppServices services)
    {
        Title = "Capture with Shortcuts";
        var stack = new VerticalStackLayout { Padding = 24, Spacing = 20 };
        stack.Add(new Label { Text = "Keep what matters.", FontSize = 32, FontAttributes = FontAttributes.Bold });
        stack.Add(new Label { Text = "Notification History saves the details you pass through Shortcuts. iOS does not let this app read other apps’ notifications directly.", FontSize = 17 });
        string[] steps = [
         "1. Open Shortcuts and edit or create the shortcut that will save notifications. Locate its automation controls; their placement depends on your iOS version.",
   "2. Add the Notification automation trigger and choose a source app. Apple documents Title, Subtitle, and Message filters; leave filters empty to capture all matching arrivals.",
   "3. Add Notification History’s Save Notification action. Set Source App to a fixed name such as WhatsApp unless your trigger provides a usable app value.",
   "4. Inspect the automation’s available input variables. Map Title, Subtitle, and Message only where your iOS build actually provides them. Missing fields can stay empty. The app does not extract hidden fields.",
   "5. Choose automatic execution if the trigger offers it. Run a real notification test with this app closed, then open history to check the saved values.",
   "6. Repeat for each source app. Add Notification History from the Home Screen widget gallery for a recent-history preview." ];
        foreach (var step in steps) stack.Add(new Label { Text = step, FontSize = 16 });
        stack.Add(new Label { Text = "Field mappings and exact automation labels need verification on your iOS 27 device. You can first test Save Notification in an ordinary shortcut with typed values.", FontSize = 14, TextColor = Colors.Gray });
        var done = new Button { Text = "Start using history" };
        done.Clicked += async (_, _) =>
        {
            try { await services.Database.SetSettingAsync("onboarded", "yes"); await Navigation.PopAsync(); }
            catch { await AppServices.AlertAsync("Unable to finish setup", "Please try again."); }
        }; stack.Add(done); Content = new ScrollView { Content = stack };
    }
}
