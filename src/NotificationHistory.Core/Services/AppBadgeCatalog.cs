using NotificationHistory.Core.Models;
namespace NotificationHistory.Core.Services;

public static class AppBadgeCatalog
{
    // Match the same Unicode/case/whitespace keys used by app appearance profiles.
    // Keep the original source values so SQLite can filter them without ASCII-only normalization.
    public static List<AppBadgeData> Build(IEnumerable<AppProfile> profiles, IEnumerable<NotificationSourceCount> counts)
    {
        var apps = profiles.ToDictionary(p => p.SourceKey, StringComparer.Ordinal);
        var groups = counts.GroupBy(c => AppProfile.Key(c.SourceApp), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        foreach (var (key, rows) in groups)
            apps.TryAdd(key, AppProfile.Default(rows.OrderBy(r => r.SourceApp, StringComparer.Ordinal).First().SourceApp));
        return apps.Values.OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(p => p.SourceKey, StringComparer.Ordinal)
            .Select(p =>
            {
                var rows = groups.GetValueOrDefault(p.SourceKey);
                return new AppBadgeData(p, rows?.Sum(r => r.Count) ?? 0,
                    rows?.Select(r => r.SourceApp).Distinct().ToArray() ?? []);
            })
            .ToList();
    }
}
