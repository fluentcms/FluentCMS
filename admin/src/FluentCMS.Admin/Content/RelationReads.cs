using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Lists;
using FluentCMS.Admin.Shell;

namespace FluentCMS.Admin.Content;

// The reads of a relation's target (spec 4.9): a page of the picker, by cursor, and the labels of chosen entries by their
// ids. Both read the label field of the target (spec 4.1) and nothing else of its fields. A target that is not localized is
// read without a locale.
internal static class RelationReads
{
    public const int PageSize = 25;

    // The ids of a relation's value: an id as a write sends it, or a reference { "id", "type" } or an entry as a read gives
    // it; a list of them for a relation to many.
    public static IReadOnlyList<Guid> Ids(JsonNode? value) => value switch
    {
        JsonArray items => [.. items.Select(Id).OfType<Guid>()],
        _ => Id(value) is { } id ? [id] : [],
    };

    private static Guid? Id(JsonNode? node) =>
        Guid.TryParse(FluentCMS.Admin.Fields.FieldValues.Text(node is JsonObject reference ? reference["id"] : node), out var id) && id != Guid.Empty ? id : null;

    public static string? LabelField(SchemaTypeDescription target) =>
        AdminLabels.LabelField(target) is { } name && target.Fields.Any(field => field.Name == name && !field.Hidden) ? name : null;

    // Without a search index a search is a contains filter on the label field, when its type takes one.
    public static bool FiltersLabel(SchemaTypeDescription target, ListFilterRegistry filters) =>
        LabelField(target) is { } name
        && target.Fields.First(field => field.Name == name) is var field
        && filters.Find(field)?.Operators.Contains(FilterOperator.Contains) == true;

    // By the label when it sorts, else the newest first; the engine keys the cursor with the id after these keys.
    public static ContentQuery Page(SchemaTypeDescription target, IReadOnlyList<FieldFilter> filters, string? search, bool searchIndex, string? cursor, string? locale)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(filters);
        var label = LabelField(target);
        List<ContentFilter> all = [.. filters];
        if (search is not null && !searchIndex && label is not null)
        {
            all.Add(new FieldFilter(label, FilterOperator.Contains, search));
        }

        var sortsByLabel = label is not null && target.Fields.First(field => field.Name == label) is { Multiple: false, Type: "text" or "slug" or "email" or "url" };
        return new ContentQuery
        {
            Filter = all switch { [] => null, [var one] => one, _ => new And(all) },
            Search = searchIndex ? search : null,
            Fields = [.. new[] { "id", "status", label, ListView.UpdatedAt }.OfType<string>()],
            Sort = sortsByLabel ? [new SortField(label!)] : ListView.DefaultSort,
            PageSize = PageSize,
            Cursor = cursor,
            Locale = target.Localized ? locale : null,
            WithTotal = false,
        };
    }

    // The entries of the ids that the caller may read, by id, in reads of the largest page.
    public static async Task<IReadOnlyDictionary<Guid, ContentEntry>> LabelsAsync(ManagementClient client, SchemaTypeDescription target, IReadOnlyCollection<Guid> ids, string? locale, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(ids);
        Dictionary<Guid, ContentEntry> found = [];
        foreach (var chunk in ids.Distinct().Chunk(ListView.MaxPageSize))
        {
            var query = new ContentQuery
            {
                Filter = new FieldFilter("id", FilterOperator.In, chunk.Cast<object>().ToList()),
                Fields = [.. new[] { "id", LabelField(target) }.OfType<string>()],
                PageSize = chunk.Length,
                Locale = target.Localized ? locale : null,
            };
            foreach (var entry in (await client.ListEntriesAsync(target.Name, query, cancellationToken)).Items)
            {
                found[entry.Id] = entry;
            }
        }

        return found;
    }
}
