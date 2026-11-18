using System.Text.Json.Nodes;
using FluentCMS.Plugins.Media.Admin.Api;

namespace FluentCMS.Plugins.Media.Admin;

// An entry that uses an asset: its label, its type's label, the fields and locales it uses the asset in, and its form.
internal sealed record MediaUsageRow(string Label, string Type, string Where, string Href);

internal static class MediaUsage
{
    private const int MaxPageSize = 100;

    // One row per entry, labelled with one list read per type (spec 2.7). The schema gives each type's label field; when it
    // cannot be read, or an entry is not in the answer, the entry is named by its id.
    public static async Task<IReadOnlyList<MediaUsageRow>> RowsAsync(MediaApi media, IReadOnlyList<MediaUsageInfo> usage, string? locale, CancellationToken cancellationToken)
    {
        if (usage.Count == 0)
        {
            return [];
        }

        IReadOnlyList<MediaSchemaType> types;
        try
        {
            types = (await media.SchemaAsync(cancellationToken)).Types;
        }
        catch (Exception failure) when (failure is FluentCMS.Admin.Api.ApiProblemException or HttpRequestException or System.Text.Json.JsonException)
        {
            types = [];
        }

        List<MediaUsageRow> rows = [];
        foreach (var byType in usage.GroupBy(item => item.Type, StringComparer.Ordinal))
        {
            var type = types.FirstOrDefault(item => item.Name == byType.Key);
            var field = type is null ? null : LabelField(type);
            Dictionary<Guid, string> labels = [];
            if (field is not null)
            {
                foreach (var chunk in byType.Select(item => item.EntryId).Distinct().Chunk(MaxPageSize))
                {
                    try
                    {
                        foreach (var entry in await media.EntriesAsync(byType.Key, chunk, field, type!.Localized ? locale : null, cancellationToken))
                        {
                            if (Guid.TryParse(entry["id"]?.ToString(), out var id) && entry[field] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
                            {
                                labels[id] = text;
                            }
                        }
                    }
                    catch (Exception failure) when (failure is FluentCMS.Admin.Api.ApiProblemException or HttpRequestException or System.Text.Json.JsonException)
                    {
                    }
                }
            }

            var typeLabel = string.IsNullOrWhiteSpace(type?.DisplayName) ? byType.Key : type.DisplayName;
            foreach (var entry in byType.GroupBy(item => item.EntryId))
            {
                var where = string.Join(", ", entry.Select(item => item.Locale is null ? item.Field : $"{item.Field} ({item.Locale})").Distinct(StringComparer.Ordinal));
                var href = $"content/{Uri.EscapeDataString(byType.Key)}/{entry.Key:D}";
                if (entry.Select(item => item.Locale).FirstOrDefault(code => code is not null) is { } used)
                {
                    href += "?locale=" + Uri.EscapeDataString(used);
                }

                rows.Add(new MediaUsageRow(labels.GetValueOrDefault(entry.Key) ?? entry.Key.ToString("D"), typeLabel, where, href));
            }
        }

        return rows;
    }

    // The type's titleField, else a text field named title or name, as the admin's lists label an entry (spec 4.1).
    private static string? LabelField(MediaSchemaType type)
    {
        if (!string.IsNullOrEmpty(type.TitleField))
        {
            return type.TitleField;
        }

        return type.Fields.FirstOrDefault(field => field is { Type: "text", Multiple: false, Hidden: false, Name: "title" or "name" })?.Name;
    }
}
