using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Schema;
using static FluentCMS.Core.Content.Query.QueryExpressions;

namespace FluentCMS.Core.Content.Query;

// One key of the order of a query. Token is the key as the grammar writes it, so a cursor names the order it belongs to.
internal sealed record SortKey(string Token, Expression Value, bool Descending, bool Nullable);

// The order of a query and the keyset predicate that continues it. A missing value sorts lowest on every provider:
// SQLite and SQL Server already do, but PostgreSQL does not, so a nullable key is ranked explicitly.
internal static class SortKeys
{
    private const string Id = "id";

    public static List<SortKey> Build(QueryScope query, SchemaType type, IReadOnlyList<SortField> sort, Expression row)
    {
        List<SortKey> keys = [];
        foreach (var field in sort)
        {
            var token = (field.Descending ? "-" : "") + field.Path;
            if (keys.Any(key => key.Token.TrimStart('-') == field.Path))
            {
                query.Report("sort", QueryErrorCodes.NotSortable, $"'{field.Path}' is named more than once.");
            }
            else if (Value(query, type, field.Path, row) is { } value)
            {
                keys.Add(new(token, value.Expression, field.Descending, value.Nullable));
            }
            else if (query.Paths.Reverse(type, field.Path) is { } reverse)
            {
                query.Report("sort", QueryErrorCodes.ReverseRelation, QueryPaths.ReverseMessage(type, field.Path, reverse));
            }
            else
            {
                query.Report("sort", QueryErrorCodes.NotSortable, $"'{field.Path}' is not a field of {type.Name} that can be sorted.");
            }
        }

        if (!keys.Any(key => key.Token.TrimStart('-') == Id))
        {
            keys.Add(new(Id, DocumentId(row), Descending: false, Nullable: false));
        }

        return keys;
    }

    public static IQueryable<QueryRow> Apply(IQueryable<QueryRow> rows, IEnumerable<SortKey> keys, ParameterExpression row)
    {
        var first = true;
        foreach (var key in keys)
        {
            if (key.Nullable)
            {
                var rank = Expression.Condition(Expression.Equal(key.Value, Expression.Constant(null, key.Value.Type)), Expression.Constant(0), Expression.Constant(1));
                rows = Order(rows, rank, key.Descending, first, row);
                first = false;
            }

            rows = Order(rows, key.Value, key.Descending, first, row);
            first = false;
        }

        return rows;
    }

    // key1 after v1, or key1 = v1 and (key2 after v2, or key2 = v2 and ...).
    public static Expression After(IReadOnlyList<SortKey> keys, IReadOnlyList<object?> values, int index = 0)
    {
        var strict = Strict(keys[index], values[index]);
        if (index == keys.Count - 1)
        {
            return strict;
        }

        return Expression.OrElse(strict, Expression.AndAlso(Tie(keys[index], values[index]), After(keys, values, index + 1)));
    }

    // Base64url of the sort values as JSON, neither opaque nor signed. That leaks nothing, since a value the cursor holds was
    // in the page that gave it and decoding binds every value as a parameter, but clients are told not to read or build one.
    public static string Encode(IReadOnlyList<SortKey> keys, IReadOnlyList<object?> values)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            for (var index = 0; index < keys.Count; index++)
            {
                writer.WritePropertyName(keys[index].Token);
                WriteValue(writer, values[index]);
            }

            writer.WriteEndObject();
        }

        return Base64Url.EncodeToString(buffer.WrittenSpan);
    }

    // Null when the cursor is not one this query produced: bad text, or another order.
    public static object?[]? Decode(string cursor, IReadOnlyList<SortKey> keys)
    {
        try
        {
            using var document = JsonDocument.Parse(Base64Url.DecodeFromChars(cursor));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var properties = document.RootElement.EnumerateObject().ToList();
            if (properties.Count != keys.Count)
            {
                return null;
            }

            var values = new object?[keys.Count];
            for (var index = 0; index < keys.Count; index++)
            {
                if (properties[index].Name != keys[index].Token || !TryReadValue(properties[index].Value, keys[index], out values[index]))
                {
                    return null;
                }
            }

            return values;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or OverflowException or InvalidOperationException)
        {
            return null;
        }
    }

    private static (Expression Expression, bool Nullable)? Value(QueryScope query, SchemaType type, string path, Expression row)
    {
        switch (query.Paths.Resolve(type, path))
        {
            case SystemPath { Field.Sortable: true } system:
                var read = system.Field.ReadFrom(row, query.State);
                return (read, Nullable.GetUnderlyingType(read.Type) is not null);
            case FieldPath { Rest.Length: 0, Stored.Kind: FieldStorageKind.Column } column when query.Schema.FieldTypes.Find(column.Stored.Field.FieldType)!.Sortable(column.Stored.Field):
                var owner = Expression.Property(row, column.Stored.Localized ? nameof(QueryRow.Localized) : nameof(QueryRow.Shared));
                return (Column(owner, column.Stored.Column, column.Stored.ClrType!), IsNullable(column.Stored));
            default:
                return null;
        }
    }

    private static IQueryable<QueryRow> Order(IQueryable<QueryRow> rows, Expression key, bool descending, bool first, ParameterExpression row)
    {
        var method = (first ? "OrderBy" : "ThenBy") + (descending ? "Descending" : "");
        var call = Expression.Call(typeof(Queryable), method, [typeof(QueryRow), key.Type], rows.Expression, Expression.Quote(Expression.Lambda(key, row)));
        return rows.Provider.CreateQuery<QueryRow>(call);
    }

    private static Expression Strict(SortKey key, object? value)
    {
        var column = key.Value;
        if (value is null)
        {
            return key.Descending ? Expression.Constant(false) : Expression.NotEqual(column, Expression.Constant(null, column.Type));
        }

        var after = Beyond(column, value, key.Descending ? ExpressionType.LessThan : ExpressionType.GreaterThan);

        // Missing values come last in a descending order, so they are still ahead of any value.
        return key.Descending && key.Nullable
            ? Expression.OrElse(after, Expression.Equal(column, Expression.Constant(null, column.Type)))
            : after;
    }

    private static Expression Beyond(Expression column, object value, ExpressionType comparison)
    {
        // bool has no ordering operators: false sorts below true.
        if (value is bool flag)
        {
            var wanted = comparison == ExpressionType.GreaterThan;
            return flag == wanted ? Expression.Constant(false) : Expression.Equal(column, Bind(wanted, column.Type));
        }

        return Compare(comparison, column, Bind(value, column.Type));
    }

    private static BinaryExpression Tie(SortKey key, object? value) => value is null
        ? Expression.Equal(key.Value, Expression.Constant(null, key.Value.Type))
        : Expression.Equal(key.Value, Bind(value, key.Value.Type));

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case bool flag:
                writer.WriteBooleanValue(flag);
                break;
            case DateTimeOffset moment:
                writer.WriteStringValue(moment.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                break;
            case DateOnly day:
                writer.WriteStringValue(day.ToString("O", CultureInfo.InvariantCulture));
                break;
            case IFormattable formattable:
                writer.WriteStringValue(formattable.ToString(null, CultureInfo.InvariantCulture));
                break;
            default:
                writer.WriteStringValue(value.ToString());
                break;
        }
    }

    private static bool TryReadValue(JsonElement element, SortKey key, out object? value)
    {
        value = null;
        var type = Nullable.GetUnderlyingType(key.Value.Type) ?? key.Value.Type;
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
                return key.Nullable;
            case JsonValueKind.True or JsonValueKind.False when type == typeof(bool):
                value = element.GetBoolean();
                return true;
            case JsonValueKind.String:
                var text = element.GetString()!;
                return TryReadText(text, type, out value);
            default:
                return false;
        }
    }

    private static bool TryReadText(string text, Type type, out object? value)
    {
        var parsed = true;
        if (type == typeof(string))
        {
            value = text;
        }
        else if (type == typeof(long))
        {
            parsed = long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole);
            value = whole;
        }
        else if (type == typeof(decimal))
        {
            parsed = decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var number);
            value = number;
        }
        else if (type == typeof(DateOnly))
        {
            parsed = DateOnly.TryParse(text, CultureInfo.InvariantCulture, out var day);
            value = day;
        }
        else
        {
            return SystemField.TryRead(text, type, out value);
        }

        return parsed;
    }
}
