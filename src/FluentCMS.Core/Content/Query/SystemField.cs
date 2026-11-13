using System.Collections.Frozen;
using System.Globalization;
using System.Linq.Expressions;
using FluentCMS.Content;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Publishing;

namespace FluentCMS.Core.Content.Query;

// A reserved field of every entry, read from cms_document. Default says whether a query without a selection returns
// it; the response shape of the specification leaves out the two author ids, which a row rule still needs. Management
// marks the pending schedules of the locale (spec 4.5), which only a read in the Management shape has (ManagementShape):
// any other read knows no such field. They are neither filtered nor sorted by.
internal sealed record SystemField(string Name, Func<Expression, short, Expression> Read, FilterOperator[] Operators, bool Sortable, bool Default, bool Management = false)
{
    private static readonly FilterOperator[] Identity = [FilterOperator.Equal, FilterOperator.NotEqual, FilterOperator.In, FilterOperator.NotIn];

    private static readonly SystemField[] Ordered =
    [
        new("id", Member(nameof(Document.DocumentId)), Identity, Sortable: true, Default: true),
        new("status", Member(nameof(Document.Status)), Identity, Sortable: false, Default: true),
        new("locale", Member(nameof(Document.Locale)), [], Sortable: false, Default: true),
        new("version", ReadVersion, FilterSets.Ordered.Where(item => item is not (FilterOperator.IsNull or FilterOperator.IsNotNull)).ToArray(), Sortable: false, Default: true),
        new("createdAt", Member(nameof(Document.CreatedAt)), FilterSets.Ordered, Sortable: true, Default: true),
        new("updatedAt", Member(nameof(Document.UpdatedAt)), FilterSets.Ordered, Sortable: true, Default: true),
        new("publishedAt", Member(nameof(Document.PublishedAt)), FilterSets.Ordered, Sortable: true, Default: true),
        new("scheduledPublishAt", Member(nameof(Document.SchedulePublishAt)), [], Sortable: false, Default: true, Management: true),
        new("scheduledUnpublishAt", Member(nameof(Document.ScheduleUnpublishAt)), [], Sortable: false, Default: true, Management: true),
        new("createdBy", Member(nameof(Document.CreatedBy)), Identity, Sortable: false, Default: false),
        new("updatedBy", Member(nameof(Document.UpdatedBy)), Identity, Sortable: false, Default: false),
    ];

    private static readonly FrozenDictionary<string, SystemField> ByName = Ordered.ToFrozenDictionary(field => field.Name, StringComparer.Ordinal);

    public static IReadOnlyList<SystemField> Fields => Ordered;

    public static SystemField? Find(string name) => ByName.GetValueOrDefault(name);

    public static bool NamesAPerson(string name) => name is "createdBy" or "updatedBy";

    // Outside the Delivery response shape, so only a principal that manages reads it.
    public static bool OnlyForManagers(string name) => NamesAPerson(name) || Find(name) is { Management: true };

    public Expression ReadFrom(Expression row, short state) => Read(Expression.Property(row, nameof(QueryRow.Document)), state);

    // Turns an operand into a value of the type of the expression, which is a system field's or a sort key's.
    public static bool TryRead(object? value, Type type, out object? result)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        result = value?.GetType() == target ? value : null;
        if (result is not null || value is not string text)
        {
            return result is not null;
        }

        if (target == typeof(Guid) && Guid.TryParseExact(text, "D", out var id))
        {
            result = id;
        }
        else if (target == typeof(DateTimeOffset) && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var moment))
        {
            result = moment;
        }
        else if (target == typeof(int) && int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
        {
            result = number;
        }
        else if (target == typeof(EntryStatus) && text.Length > 0 && !char.IsAsciiDigit(text[0]) && Enum.TryParse<EntryStatus>(text, ignoreCase: true, out var status))
        {
            result = status;
        }

        return result is not null;
    }

    private static Func<Expression, short, Expression> Member(string name) => (document, _) => Expression.Property(document, name);

    private static Expression ReadVersion(Expression document, short state) => state == ContentState.Published
        ? Expression.Property(document, nameof(Document.PublishedVersion))
        : Expression.Convert(Expression.Property(document, nameof(Document.DraftVersion)), typeof(int?));
}
