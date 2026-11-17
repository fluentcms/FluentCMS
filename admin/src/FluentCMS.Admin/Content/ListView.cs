using System.Globalization;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Lists;
using FluentCMS.Admin.Shell;

namespace FluentCMS.Admin.Content;

internal enum ListColumnKind
{
    // The entry's label, which opens it: the type's label field, or the id when it has none.
    Label,
    Field,
    Status,
    Locale,
    UpdatedAt,
    UpdatedBy,
}

// A column of a list. Its name is what the query string and the stored choice keep, and what the API sorts by: a field's
// name, or a system member's.
internal sealed record ListColumn(string Name, ListColumnKind Kind, bool Sortable, SchemaFieldDescription? Field = null, string? ReadPath = null);

// The columns a list of one type may show (spec 4.4): the label first and always, then the system fields and the fields a
// cell can show, in the order the user chose. The list reads only what it shows.
internal sealed class ListView
{
    public const string UpdatedAt = "updatedAt";

    public const string ScheduledPublishAt = "scheduledPublishAt";

    public const string ScheduledUnpublishAt = "scheduledUnpublishAt";

    public const string EveryLocale = "all";

    public static readonly IReadOnlyList<int> PageSizes = [10, 25, 50, 100];

    public const int DefaultPageSize = 25;

    // The core field types the query model sorts by, when a field holds one value (backend 4.4); a list is stored as JSON.
    private static readonly HashSet<string> SortableTypes = new(StringComparer.Ordinal)
    {
        "text", "email", "url", "slug", "integer", "decimal", "boolean", "date", "dateTime", "select",
    };

    private readonly Dictionary<string, ListColumn> _byName;

    private ListView(SchemaTypeDescription type, ListColumn label, IReadOnlyList<ListColumn> available, IReadOnlyList<FilterField> filters)
    {
        Type = type;
        Label = label;
        Available = available;
        Filters = filters;
        _byName = new Dictionary<string, ListColumn>(StringComparer.Ordinal) { [label.Name] = label };
        foreach (var column in available)
        {
            _byName[column.Name] = column;
        }
    }

    public SchemaTypeDescription Type { get; }

    public ListColumn Label { get; }

    // Every column but the label, in the order of the type: the system fields, then the fields in schema order.
    public IReadOnlyList<ListColumn> Available { get; }

    // The fields a filter may be set on.
    public IReadOnlyList<FilterField> Filters { get; }

    // Whether a search box is offered: the type has a field the index holds (FCMS1700 otherwise).
    public bool Searchable => Type.Fields.Any(item => item.Searchable && !item.Hidden);

    public IReadOnlyList<string> DefaultColumns => [.. new[] { "status", UpdatedAt }.Where(_byName.ContainsKey)];

    public static IReadOnlyList<SortField> DefaultSort { get; } = [new SortField(UpdatedAt, Descending: true)];

    public static ListView For(SchemaTypeDescription type, SchemaDescription schema, ListCellRegistry cells, ListFilterRegistry? filters = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(cells);
        filters ??= new ListFilterRegistry();

        var labelName = AdminLabels.LabelField(type);
        var labelField = type.Fields.FirstOrDefault(field => field.Name == labelName && !field.Hidden);
        var label = labelField is null
            ? new ListColumn("id", ListColumnKind.Label, Sortable: false)
            : new ListColumn(labelField.Name, ListColumnKind.Label, Sorts(labelField), labelField, labelField.Name);

        List<ListColumn> available =
        [
            new("status", ListColumnKind.Status, Sortable: false, ReadPath: "status"),
        ];
        if (type.Localized)
        {
            available.Add(new("locale", ListColumnKind.Locale, Sortable: false, ReadPath: "locale"));
        }

        available.Add(new(UpdatedAt, ListColumnKind.UpdatedAt, Sortable: true, ReadPath: UpdatedAt));
        available.Add(new("updatedBy", ListColumnKind.UpdatedBy, Sortable: false, ReadPath: "updatedBy"));
        foreach (var field in type.Fields.Where(field => field.Name != label.Name && cells.Shows(field)))
        {
            available.Add(new(field.Name, ListColumnKind.Field, Sorts(field), field, ReadPath(field, schema)));
        }

        return new ListView(type, label, available, ListFilters.Fields(type, filters));
    }

    // The chosen columns that exist, once each and in the order given; the default when none of them does.
    public IReadOnlyList<ListColumn> Choose(IEnumerable<string>? names)
    {
        var chosen = (names ?? [])
            .Distinct(StringComparer.Ordinal)
            .Select(name => _byName.GetValueOrDefault(name))
            .OfType<ListColumn>()
            .Where(column => column.Kind != ListColumnKind.Label)
            .ToList();
        return chosen.Count > 0 ? chosen : [.. DefaultColumns.Select(name => _byName[name])];
    }

    // The keys of a sort=, such as -updatedAt,title, that name a column that sorts, once each; the default when none does.
    public IReadOnlyList<SortField> ParseSort(string? text)
    {
        List<SortField> keys = [];
        foreach (var part in (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var descending = part.StartsWith('-');
            var name = descending ? part[1..] : part;
            if (_byName.TryGetValue(name, out var column) && column.Sortable && keys.All(key => key.Path != name))
            {
                keys.Add(new SortField(name, descending));
            }
        }

        return keys.Count > 0 ? keys : DefaultSort;
    }

    public static string WriteSort(IEnumerable<SortField> keys) =>
        string.Join(',', keys.Select(key => (key.Descending ? "-" : "") + key.Path));

    // The server's largest page is 100 by default (backend 4.4); the address may ask for any size up to it.
    public const int MaxPageSize = 100;

    public static int ParsePageSize(string? size) => Number(size) is int value and > 0 and <= MaxPageSize ? value : DefaultPageSize;

    public static int ParsePage(string? page) => Number(page) is int value and > 0 ? value : 1;

    private static int? Number(string? text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;

    // fields= names the label and the shown columns, system members included, since a read with fields= has only what it
    // names, and the pending schedules with the status; populate= names the relations among them. A list that shows nothing
    // else still names the id, since a read without fields= has every field. Under locale=all the engine matches, sorts and
    // pages in the site's default locale and answers every locale of each entry.
    public ContentQuery Query(IReadOnlyList<ListColumn> shown, IReadOnlyList<SortField> sort, int page, int pageSize, IReadOnlyList<FieldFilter>? filters = null, string? search = null, bool everyLocale = false)
    {
        ArgumentNullException.ThrowIfNull(shown);
        var paths = new[] { Label }.Concat(shown).Select(column => column.ReadPath).OfType<string>()
            .Concat(shown.Any(column => column.Kind == ListColumnKind.Status) ? [ScheduledPublishAt, ScheduledUnpublishAt] : [])
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var populate = new[] { Label }.Concat(shown)
            .Where(column => column.Field is { Type: "relation" } && column.ReadPath?.Contains('.', StringComparison.Ordinal) == true)
            .Select(column => column.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return new ContentQuery
        {
            Filter = filters switch { null or [] => null, [var one] => one, _ => new And([.. filters]) },
            Search = search,
            Locale = everyLocale ? EveryLocale : null,
            Fields = paths.Count > 0 ? paths : ["id"],
            Populate = populate,
            Sort = sort,
            Page = page,
            PageSize = pageSize,
            WithTotal = true,
        };
    }

    // The ids one status read names: the filter counts each against the server's MaxFilterNodes (100) with the node of the
    // filter itself.
    public const int MaxStatusIds = 99;

    // The status of a page's entries in another locale than the one the page was matched in, which locale=all cannot give:
    // it matches in the site's default locale, which an entry may lack. A read of that locale limited to their ids.
    public static ContentQuery StatusQuery(string locale, IReadOnlyCollection<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return new ContentQuery
        {
            Filter = new FieldFilter("id", FilterOperator.In, ids.ToList()),
            Locale = locale,
            Fields = ["id", "status", ScheduledPublishAt, ScheduledUnpublishAt],
            PageSize = Math.Max(1, ids.Count),
        };
    }

    private static bool Sorts(SchemaFieldDescription field) => !field.Multiple && SortableTypes.Contains(field.Type);

    // A relation is read with its target's label, when the user may read the target and it has one.
    private static string ReadPath(SchemaFieldDescription field, SchemaDescription schema)
    {
        if (field.Type == "relation"
            && field.Relation is { } relation
            && schema.Types.FirstOrDefault(type => type.Name == relation.Target) is { } target
            && AdminLabels.LabelField(target) is { } targetLabel)
        {
            return $"{field.Name}.{targetLabel}";
        }

        return field.Name;
    }
}
