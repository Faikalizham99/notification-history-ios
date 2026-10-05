using NotificationHistory.Core.Models;
namespace NotificationHistory.ViewModels;

public sealed record NotificationCardModel(NotificationEntry Entry, AppProfile Profile, ImageSource? Icon)
{
    public string Initial => string.IsNullOrWhiteSpace(Profile.DisplayName) ? "?" :
        new System.Globalization.StringInfo(Profile.DisplayName).SubstringByTextElements(0, 1).ToUpperInvariant();
}
