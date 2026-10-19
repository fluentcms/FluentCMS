using System.Globalization;
using System.Text.Json;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Search;

// Field filters checked against one content type's definition, then applied to its entries' latest fields (SRC-01). Each field type
// takes the operators that mean something for it, and a value of its own kind; an entity's field is compared by its fact's value.
public sealed class FieldFilters
{
    private static readonly FieldOperator[] Ordering =
        [FieldOperator.EqualTo, FieldOperator.LessThan, FieldOperator.AtMost, FieldOperator.GreaterThan, FieldOperator.AtLeast];

    private readonly IReadOnlyList<(FieldFilter Filter, string Type)> _filters;
    private readonly bool _facts;

    private FieldFilters(IReadOnlyList<(FieldFilter Filter, string Type)> filters, bool facts)
    {
        _filters = filters;
        _facts = facts;
    }

    // No field filters, so every entry's fields are accepted.
    public static FieldFilters None { get; } = new([], facts: false);

    public static Result<FieldFilters> Create(IReadOnlyList<FieldFilter> filters, string contentType, ContentTypeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(definition);

        var errors = new List<Error>();
        var checkedFilters = new List<(FieldFilter, string)>();

        foreach (var filter in filters)
        {
            var field = definition.Fields.FirstOrDefault(candidate => candidate.Name == filter.Field);
            if (field is null)
            {
                errors.Add(new Error(SearchErrorCodes.FieldNotFound, $"'{contentType}' has no field '{filter.Field}'; get_content_type lists its fields."));
                continue;
            }

            // A date-time field set to dateOnly holds calendar dates, and its filters take one.
            var kind = field.Type == "dateTime" && field.Settings.TryGetProperty("dateOnly", out var dateOnly) && dateOnly.ValueKind == JsonValueKind.True
                ? "date"
                : field.Type;

            if (Operators(kind) is not { } operators)
            {
                errors.Add(new Error(
                    SearchErrorCodes.FieldNotFilterable,
                    $"The field '{field.Name}' is a {field.Type} field, which search does not filter on; filter on a text, rich text, enum, number, "
                    + "date-time, boolean or reference field."));
                continue;
            }

            if (!operators.Contains(filter.Operator))
            {
                errors.Add(new Error(
                    SearchErrorCodes.OperatorNotSupported,
                    $"'{Name(filter.Operator)}' does not apply to the {field.Type} field '{field.Name}'; use {string.Join(", ", operators.Select(Name))}."));
                continue;
            }

            if (!ValueFits(kind, filter.Value))
            {
                errors.Add(new Error(
                    SearchErrorCodes.FilterValueInvalid,
                    $"The value for the {field.Type} field '{field.Name}' must be {Expected(kind)}."));
                continue;
            }

            checkedFilters.Add((filter, kind));
        }

        return errors.Count == 0
            ? Result<FieldFilters>.Success(new FieldFilters(checkedFilters, definition.IsEntity))
            : Result<FieldFilters>.Failure(errors);
    }

    // Whether every filter holds for the fields; a field that is missing or null holds none.
    public bool Matches(JsonElement fields)
    {
        foreach (var (filter, type) in _filters)
        {
            if (fields.ValueKind != JsonValueKind.Object || !fields.TryGetProperty(filter.Field, out var value))
            {
                return false;
            }

            if (_facts && value.ValueKind == JsonValueKind.Object && value.TryGetProperty("value", out var fact))
            {
                value = fact;
            }

            if (!Holds(type, filter, value))
            {
                return false;
            }
        }

        return true;
    }

    // The operator as tools and messages name it, such as less_than.
    public static string Name(FieldOperator op)
    {
        return op switch
        {
            FieldOperator.EqualTo => "equals",
            FieldOperator.Contains => "contains",
            FieldOperator.LessThan => "less_than",
            FieldOperator.AtMost => "at_most",
            FieldOperator.GreaterThan => "greater_than",
            _ => "at_least",
        };
    }

    private static FieldOperator[]? Operators(string type)
    {
        return type switch
        {
            "text" or "richText" or "enum" => [FieldOperator.EqualTo, FieldOperator.Contains],
            "number" or "dateTime" or "date" => Ordering,
            "boolean" or "reference" => [FieldOperator.EqualTo],
            _ => null,
        };
    }

    private static bool ValueFits(string type, JsonElement value)
    {
        return type switch
        {
            "number" => value.ValueKind == JsonValueKind.Number,
            "dateTime" => value.ValueKind == JsonValueKind.String && IsoDateTimes.IsPointInTime(value.GetString()),
            "date" => value.ValueKind == JsonValueKind.String && IsoDateTimes.IsDate(value.GetString()),
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "reference" => value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out _),
            _ => value.ValueKind == JsonValueKind.String && value.GetString()!.Length > 0,
        };
    }

    private static string Expected(string type)
    {
        return type switch
        {
            "number" => "a number, such as 19.99",
            "dateTime" => "a point in time in ISO 8601 with an offset, such as \"2026-10-19T09:00:00Z\"",
            "date" => "a calendar date as YYYY-MM-DD, such as \"2026-10-19\"",
            "boolean" => "true or false",
            "reference" => "the id of an entry or entity, such as \"0192f3a1-7c2e-7a4b-9d3e-1f2a3b4c5d6e\"",
            _ => "a non-empty string",
        };
    }

    private static bool Holds(string type, FieldFilter filter, JsonElement actual)
    {
        switch (type)
        {
            // Numbers are stored as decimals, so they are compared as decimals, as the number field type does.
            case "number":
                return actual.ValueKind == JsonValueKind.Number
                    && Compare(filter.Operator, actual.TryGetDecimal(out var number) && filter.Value.TryGetDecimal(out var limit)
                        ? number.CompareTo(limit)
                        : actual.GetDouble().CompareTo(filter.Value.GetDouble()));
            case "dateTime":
                return actual.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(actual.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var moment)
                    && Compare(filter.Operator, moment.CompareTo(DateTimeOffset.Parse(filter.Value.GetString()!, CultureInfo.InvariantCulture)));
            case "date":
                return actual.ValueKind == JsonValueKind.String
                    && DateOnly.TryParseExact(actual.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                    && Compare(filter.Operator, day.CompareTo(DateOnly.ParseExact(filter.Value.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture)));
            case "boolean":
                return actual.ValueKind == filter.Value.ValueKind;
            case "reference":
                return actual.ValueKind == JsonValueKind.String
                    && Guid.TryParse(actual.GetString(), out var id)
                    && id == Guid.Parse(filter.Value.GetString()!);
            default:
                if (actual.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                var text = actual.GetString()!;
                var expected = filter.Value.GetString()!;
                return filter.Operator == FieldOperator.Contains
                    ? text.Contains(expected, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(text, expected, StringComparison.Ordinal);
        }
    }

    private static bool Compare(FieldOperator op, int comparison)
    {
        return op switch
        {
            FieldOperator.EqualTo => comparison == 0,
            FieldOperator.LessThan => comparison < 0,
            FieldOperator.AtMost => comparison <= 0,
            FieldOperator.GreaterThan => comparison > 0,
            _ => comparison >= 0,
        };
    }
}
