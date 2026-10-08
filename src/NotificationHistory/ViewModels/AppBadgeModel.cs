using NotificationHistory.Core.Models;
namespace NotificationHistory.ViewModels;

public sealed class AppBadgeModel(AppBadgeData data, ImageSource? icon) : ObservableViewModel
{
    public AppBadgeData Data { get; private set; } = data;
    public ImageSource? Icon { get; private set; } = icon;
    public string SourceKey => Data.Profile.SourceKey;
    public string DisplayName => Data.Profile.DisplayName;
    public string Initial => new System.Globalization.StringInfo(DisplayName).SubstringByTextElements(0, 1).ToUpperInvariant();
    private bool selected;
    public bool IsSelected { get => selected; set => Set(ref selected, value); }
    private bool clearArmed;
    public bool IsClearArmed { get => clearArmed; set => Set(ref clearArmed, value); }

    public void Update(AppBadgeData data, ImageSource? icon)
    {
        Data = data; Icon = icon; Raise(null);
    }
}
