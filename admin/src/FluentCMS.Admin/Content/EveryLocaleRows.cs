using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;

namespace FluentCMS.Admin.Content;

// A list read with locale=all answers each entry as { "id", "locales": { code: entry } } (backend 4.6). A row shows the entry
// in the content locale, which the engine matched and sorted by, or else in the first locale of the site it has; the badges
// show every locale.
internal static class EveryLocaleRows
{
    public static (IReadOnlyList<ContentEntry> Rows, IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, ContentEntry>> Locales) Read(
        IReadOnlyList<ContentEntry> items,
        string? contentLocale,
        IReadOnlyList<string> siteLocales)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(siteLocales);
        List<ContentEntry> rows = [];
        Dictionary<Guid, IReadOnlyDictionary<string, ContentEntry>> byId = [];
        foreach (var item in items)
        {
            if (item.Fields["locales"] is not JsonObject map)
            {
                rows.Add(item);
                continue;
            }

            var locales = map
                .Where(member => member.Value is JsonObject)
                .ToDictionary(
                    member => member.Key,
                    member => (member.Value.Deserialize(ManagementJsonContext.Default.ContentEntry) ?? new ContentEntry()) with { Id = item.Id, Locale = member.Key },
                    StringComparer.Ordinal);
            var shown = (contentLocale is not null ? locales.GetValueOrDefault(contentLocale) : null)
                ?? siteLocales.Select(locales.GetValueOrDefault).OfType<ContentEntry>().FirstOrDefault()
                ?? locales.Values.FirstOrDefault()
                ?? item;
            rows.Add(shown);
            byId[item.Id] = locales;
        }

        return (rows, byId);
    }
}
