using NotificationHistory.Core.Models;
namespace NotificationHistory.ViewModels;

public sealed record NotificationCardModel(NotificationEntry Entry, AppProfile Profile, ImageSource? Icon)
{
    // Keep expansion with this row across cell reuse; release the full body on collapse.
    public string? ExpandedBody { get; set; }
    public string Initial => string.IsNullOrWhiteSpace(Profile.DisplayName) ? "?" :
        new System.Globalization.StringInfo(Profile.DisplayName).SubstringByTextElements(0, 1).ToUpperInvariant();
}
