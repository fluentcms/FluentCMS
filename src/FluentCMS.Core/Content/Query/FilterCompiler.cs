using System.Collections;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using FluentCMS.Content;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Schema;
using static FluentCMS.Core.Content.Query.QueryExpressions;

namespace FluentCMS.Core.Content.Query;

// Compiles the filter AST to a bool expression over a QueryRow. A problem is reported on the scope and compiles to
// false, so that one pass finds every error; the caller throws before anything runs.
internal sealed class FilterCompiler(QueryScope query)
{
    private static readonly MethodInfo StringContains = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;
    private static readonly MethodInfo StringStartsWith = typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!;
    private static readonly MethodInfo StringIndexOf = typeof(string).GetMethod(nameof(string.IndexOf), [typeof(string)])!;

    // Asked is what the client wrote where a relation rewrites a negative operator, which the field type must allow.
    private sealed record Scope(SchemaType Type, Expression Row, ContentFilter? Rule, bool Trusted, string Prefix, int Hops, FilterOperator? Asked = null)
    {
        public string PathOf(FieldFilter filter) => Prefix + filter.Path;
    }

    // Returns null when neither the client nor a row rule filters. The row rule joins the filter in an And node, and
    // is the one part of it compiled without the access checks. A rule is trusted all the way down, so a relation path
    // in it does not pick up the row rule of the target, or a rule that goes through a type the principal cannot read
    // would stop working.
    public Expression? Where(SchemaType type, ContentFilter? filter, Expression row)
    {
        if (filter is not null && !FilterTree.IsWithin(filter, query.Options.MaxFilterNodes))
        {
            query.Report("filter", QueryErrorCodes.LimitExceeded, $"The filter has more than the {query.Options.MaxFilterNodes} nodes or the {FilterTree.MaxDepth} levels allowed.");
            return Fail();
        }

        return Where(type, filter, row, trusted: false, prefix: "", hops: 0, asked: null);
    }

    // Another rule on the queried type alone, compiled as trusted as the read rule.
    public Expression Rule(SchemaType type, ContentFilter rule, Expression row) =>
        Where(type, rule, row, trusted: true, prefix: "", hops: 0, asked: null)!;

    private Expression? Where(SchemaType type, ContentFilter? filter, Expression row, bool trusted, string prefix, int hops, FilterOperator? asked)
    {
        var rule = trusted ? null : query.Access.RowRule(type);
        ContentFilter? combined = (rule, filter) switch
        {
            (null, null) => null,
            (null, not null) => filter,
            (not null, null) => rule,
            _ => new And(rule!, filter!),
        };

        return combined is null ? null : Compile(combined, new Scope(type, row, rule, trusted, prefix, hops, asked));
    }

    private Expression Compile(ContentFilter filter, Scope scope)
    {
        if (ReferenceEquals(filter, scope.Rule))
        {
            scope = scope with { Trusted = true, Asked = null };
        }

        return filter switch
        {
            And { Filters.Count: 0 } or Or { Filters.Count: 0 } => Empty(),
            And every => every.Filters.Select(item => Compile(item, scope)).Aggregate((Expression)Expression.Constant(true), Expression.AndAlso),
            Or any => any.Filters.Select(item => Compile(item, scope)).Aggregate((Expression)Expression.Constant(false), Expression.OrElse),
            Not negated => Expression.Not(Compile(negated.Filter, scope)),
            FieldFilter field => Field(field, scope),
            _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, null),
        };
    }

    private ConstantExpression Empty()
    {
        query.Report("filter", QueryErrorCodes.InvalidValue, "An And or Or node needs at least one condition.");
        return Fail();
    }

    private Expression Field(FieldFilter filter, Scope scope)
    {
        switch (query.Paths.Resolve(scope.Type, filter.Path, scope.Trusted))
        {
            case SystemPath system:
                var read = system.Field.ReadFrom(scope.Row, query.State);
                return Allowed(filter, system.Field.Operators, scope)
                    ? Scalar(filter, read, scope, value => SystemField.TryRead(value, read.Type, out var result) ? (result, null) : (null, "is not a value of this system field"))
                    : Fail();
            case GroupPath:
                return NotAllowed(filter, scope);
            case FieldPath { Stored.Kind: FieldStorageKind.Reference } reference:
                return ReferenceFilter(filter, reference, scope);
            case FieldPath { Stored.Kind: FieldStorageKind.Json } json:
                return Json(filter, json, scope);
            case FieldPath { Rest.Length: 0 } column:
                return Column(filter, column.Stored, scope);
            default:
                query.Report(scope.PathOf(filter), QueryErrorCodes.UnknownField, $"'{scope.PathOf(filter)}' is not a field of {scope.Type.Name} that can be read.");
                return Fail();
        }
    }

    private Expression Column(FieldFilter filter, StoredField stored, Scope scope)
    {
        var type = TypeOf(stored);
        if (!Allowed(filter, type.FilterOperators, scope))
        {
            return Fail();
        }

        var column = ColumnOf(scope.Row, stored);
        return filter.Operator switch
        {
            FilterOperator.IsNull => NoValue(stored, column),
            FilterOperator.IsNotNull => Expression.Not(NoValue(stored, column)),
            _ => Scalar(filter, column, scope, value => Coerce(value, type, stored.Field, column.Type)),
        };
    }

    // The operators of a value with a type of its own: a column or a system field.
    private Expression Scalar(FieldFilter filter, Expression column, Scope scope, Func<object?, (object? Value, string? Problem)> coerce)
    {
        switch (filter.Operator)
        {
            case FilterOperator.Equal:
                return One(filter, scope, coerce) is { } equal ? Expression.Equal(column, Bind(equal, column.Type)) : Fail();
            case FilterOperator.NotEqual:
                return One(filter, scope, coerce) is { } other ? Expression.NotEqual(column, Bind(other, column.Type)) : Fail();
            case FilterOperator.LessThan:
                return Comparison(ExpressionType.LessThan);
            case FilterOperator.LessThanOrEqual:
                return Comparison(ExpressionType.LessThanOrEqual);
            case FilterOperator.GreaterThan:
                return Comparison(ExpressionType.GreaterThan);
            case FilterOperator.GreaterThanOrEqual:
                return Comparison(ExpressionType.GreaterThanOrEqual);
            case FilterOperator.In:
                return Many(filter, scope, coerce) is { } items ? InList(column, items) : Fail();
            case FilterOperator.NotIn:
                return Many(filter, scope, coerce) is { } excluded ? Expression.Not(InList(column, excluded)) : Fail();
            case FilterOperator.Between:
                if (Many(filter, scope, coerce) is not { } range)
                {
                    return Fail();
                }

                if (range.Count != 2)
                {
                    return Invalid(filter, scope, "needs two values");
                }

                return Expression.AndAlso(
                    Compare(ExpressionType.GreaterThanOrEqual, column, Bind(range[0], column.Type)),
                    Compare(ExpressionType.LessThanOrEqual, column, Bind(range[1], column.Type)));
            case FilterOperator.Contains:
                return Text(fragment => Expression.Call(column, StringContains, fragment));
            case FilterOperator.StartsWith:
                // LIKE is what a provider can serve from an index, but it ignores case on SQLite, unlike contains.
                return Text(fragment => Expression.AndAlso(
                    Expression.Call(column, StringStartsWith, fragment),
                    Expression.Equal(Expression.Call(column, StringIndexOf, fragment), Expression.Constant(0))));
            case FilterOperator.IsNull:
                return column.Type.IsValueType && Nullable.GetUnderlyingType(column.Type) is null
                    ? Expression.Constant(false)
                    : Expression.Equal(column, Expression.Constant(null, column.Type));
            case FilterOperator.IsNotNull:
                return column.Type.IsValueType && Nullable.GetUnderlyingType(column.Type) is null
                    ? Expression.Constant(true)
                    : Expression.NotEqual(column, Expression.Constant(null, column.Type));
            default:
                return NotAllowed(filter, scope);
        }

        Expression Comparison(ExpressionType comparison) =>
            One(filter, scope, coerce) is { } value ? Compare(comparison, column, Bind(value, column.Type)) : Fail();

        // The operand is a fragment of a value, so the field type's format check does not apply to it.
        Expression Text(Func<Expression, Expression> compare)
        {
            if (column.Type != typeof(string))
            {
                return NotAllowed(filter, scope);
            }

            return filter.Value is string text ? compare(Bind(text)) : Invalid(filter, scope, "needs text");
        }
    }

    // A column with a placeholder for a missing value holds that placeholder and never null (spec 4.3).
    private static BinaryExpression NoValue(StoredField stored, Expression column)
    {
        if (stored.Kind == FieldStorageKind.Json && (stored.Field.Multiple || stored.Field.FieldType == FieldTypeNames.Blocks))
        {
            return Expression.Equal(column, Expression.Constant("[]"));
        }

        if (stored.Field.FieldType == FieldTypeNames.RichText && stored.Required)
        {
            return Expression.Equal(column, Expression.Constant(RichText.Empty.Json));
        }

        return stored.Required
            ? Expression.Equal(column, Expression.Constant(""))
            : Expression.Equal(column, Expression.Constant(null, column.Type));
    }

    private Expression Json(FieldFilter filter, FieldPath path, Scope scope)
    {
        var stored = path.Stored;
        var type = TypeOf(stored);
        var json = ColumnOf(scope.Row, stored);

        if (path.Rest.Length == 0)
        {
            return filter.Operator switch
            {
                _ when !Allowed(filter, type.FilterOperators, scope) => Fail(),
                FilterOperator.IsNull => NoValue(stored, json),
                FilterOperator.IsNotNull => Expression.Not(NoValue(stored, json)),
                FilterOperator.HasBlock => HasBlock(filter, stored, json, scope),
                FilterOperator.Contains when stored.Field.Multiple => ListMember(filter, json, scope),
                FilterOperator.In when stored.Field.Multiple => AnyOf(filter, json, scope),
                FilterOperator.NotIn when stored.Field.Multiple => Expression.Not(AnyOf(filter, json, scope)),
                _ => Invalid(filter, scope, stored.Field.FieldType == FieldTypeNames.Json ? "needs a key, such as metadata.audience" : "cannot use this operator on a value stored as JSON"),
            };
        }

        return stored.Field.FieldType switch
        {
            FieldTypeNames.Json when path.Rest.Length == 1 => Allowed(filter, type.FilterOperators, scope) ? JsonKey(filter, json, path.Rest[0], scope) : Fail(),
            FieldTypeNames.Blocks when path.Rest.Length == 2 => BlockField(filter, stored, json, path.Rest[0], path.Rest[1], scope),
            FieldTypeNames.Blocks => Invalid(filter, scope, "must name a block and one of its fields, such as sections.hero.heading"),
            _ => Invalid(filter, scope, "goes deeper than the one level a filter can reach inside JSON"),
        };
    }

    // json_extract throws on a bare string element, so only a blocks field may reach the dialect's block filters.
    private Expression HasBlock(FieldFilter filter, StoredField stored, Expression json, Scope scope)
    {
        if (stored.Field.FieldType != FieldTypeNames.Blocks)
        {
            return NotAllowed(filter, scope);
        }

        if (filter.Value is not string block)
        {
            return Invalid(filter, scope, "needs the name of a block");
        }

        return SettingValues.List(stored.Field, FieldSettings.Blocks).Contains(block)
            ? query.Dialect.HasBlock(json, Bind(block))
            : Invalid(filter, scope, $"names '{block}', which is not a block of this field");
    }

    private Expression ListMember(FieldFilter filter, Expression json, Scope scope) => filter.Value is string item
        ? query.Dialect.ListContains(json, Bind(item))
        : Invalid(filter, scope, "needs text");

    private Expression AnyOf(FieldFilter filter, Expression json, Scope scope)
    {
        if (Many(filter, scope, value => value is string ? (value, null) : (null, "is not text")) is not { } items)
        {
            return Fail();
        }

        return items.Aggregate((Expression)Expression.Constant(false), (any, item) => Expression.OrElse(any, query.Dialect.ListContains(json, Bind((string)item!))));
    }

    private Expression JsonKey(FieldFilter filter, Expression json, string key, Scope scope)
    {
        switch (filter.Operator)
        {
            case FilterOperator.Equal:
                return JsonEquals(filter, json, key, scope);
            case FilterOperator.NotEqual:
                return Expression.Not(JsonEquals(filter, json, key, scope));
            case FilterOperator.Contains when filter.Value is string text:
                return Expression.OrElse(query.Dialect.ScalarContains(json, key, Bind(text)), query.Dialect.ArrayContains(json, key, Bind(text)));
            case FilterOperator.Contains:
                return Invalid(filter, scope, "needs text");
            default:
                return Invalid(filter, scope, "cannot use this operator on a key inside JSON");
        }
    }

    // Nothing says what type the key holds, and the bracket grammar has only text. Text that also reads as a number
    // or a boolean therefore matches either form.
    private Expression JsonEquals(FieldFilter filter, Expression json, string key, Scope scope)
    {
        var dialect = query.Dialect;
        switch (filter.Value)
        {
            case string text:
                Expression any = dialect.ScalarEquals(json, key, JsonScalarType.Text, Bind(text));
                if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole))
                {
                    any = Expression.OrElse(any, dialect.ScalarEquals(json, key, JsonScalarType.WholeNumber, Bind(whole)));
                }
                else if (decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
                {
                    any = Expression.OrElse(any, dialect.ScalarEquals(json, key, JsonScalarType.DecimalNumber, Bind(number)));
                }
                else if (bool.TryParse(text, out var flag))
                {
                    any = Expression.OrElse(any, dialect.ScalarEquals(json, key, JsonScalarType.Boolean, Bind(flag)));
                }

                return any;
            case bool flag:
                return dialect.ScalarEquals(json, key, JsonScalarType.Boolean, Bind(flag));
            case int or long:
                return dialect.ScalarEquals(json, key, JsonScalarType.WholeNumber, Bind(Convert.ToInt64(filter.Value, CultureInfo.InvariantCulture)));
            case decimal or double or float:
                return dialect.ScalarEquals(json, key, JsonScalarType.DecimalNumber, Bind(Convert.ToDecimal(filter.Value, CultureInfo.InvariantCulture)));
            default:
                return Invalid(filter, scope, "needs text, a number or a boolean");
        }
    }

    private Expression BlockField(FieldFilter filter, StoredField stored, Expression json, string blockName, string key, Scope scope)
    {
        var block = SettingValues.List(stored.Field, FieldSettings.Blocks).Contains(blockName) ? query.Schema.FindBlock(blockName) : null;
        var field = block?.FindField(key);
        if (field is null || (field.Hidden && !scope.Trusted))
        {
            query.Report(scope.PathOf(filter), QueryErrorCodes.UnknownField, $"'{scope.PathOf(filter)}' is not a field of a block of this field.");
            return Fail();
        }

        var type = query.Schema.FieldTypes.Find(field.FieldType)!;
        if (JsonScalar.TypeOf(field.FieldType) is not { } scalar)
        {
            return Invalid(filter, scope, "is not a scalar, so a filter cannot reach it inside a block");
        }

        if (!Allowed(filter, type.FilterOperators, scope))
        {
            return Fail();
        }

        var dialect = query.Dialect;
        var blockType = Bind(blockName);
        switch (filter.Operator)
        {
            case FilterOperator.Equal when !field.Multiple:
                return BlockScalar(filter, field, type, scalar, scope) is { } equal
                    ? dialect.AnyBlock(json, blockType, value => dialect.ScalarEquals(value, key, scalar, equal))
                    : Fail();
            case FilterOperator.NotEqual when !field.Multiple:
                return BlockScalar(filter, field, type, scalar, scope) is { } different
                    ? Expression.Not(dialect.AnyBlock(json, blockType, value => dialect.ScalarEquals(value, key, scalar, different)))
                    : Fail();
            case FilterOperator.Contains when filter.Value is string text:
                return dialect.AnyBlock(json, blockType, value => field.Multiple
                    ? dialect.ArrayContains(value, key, Bind(text))
                    : dialect.ScalarContains(value, key, Bind(text)));
            case FilterOperator.Contains:
                return Invalid(filter, scope, "needs text");
            default:
                return Invalid(filter, scope, "cannot use this operator inside a block");
        }
    }

    private MemberExpression? BlockScalar(FieldFilter filter, SchemaField field, IFieldType type, JsonScalarType scalar, Scope scope)
    {
        var clrType = JsonScalar.ClrType(scalar);
        if (!type.TryCoerce(filter.Value, field, out var value, out var message) || value is null)
        {
            Invalid(filter, scope, message ?? "needs a value");
            return null;
        }

        return Bind(scalar == JsonScalarType.Text ? StoredText(value) : value, clrType);
    }

    // A date compares as the text that the JSON holds. A date has one form, the only one its field type reads. Nothing
    // writes a date and time into JSON yet, so this is the UTC form of the response shape, which #2693 must store.
    private static string StoredText(object value) => value switch
    {
        DateOnly day => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTimeOffset moment => moment.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };

    private Expression ReferenceFilter(FieldFilter filter, FieldPath path, Scope scope)
    {
        var reference = Expression.Parameter(typeof(Reference), "reference");
        var targetId = Expression.Property(reference, nameof(Reference.TargetId));
        Expression condition = new[]
        {
            Expression.Equal(Expression.Property(reference, nameof(Reference.SourceType)), Expression.Constant(scope.Type.Name)),
            Expression.Equal(Expression.Property(reference, nameof(Reference.SourceDocumentId)), DocumentId(scope.Row)),
            Expression.Equal(Expression.Property(reference, nameof(Reference.State)), Expression.Constant(query.State)),
            Expression.Equal(Expression.Property(reference, nameof(Reference.Locale)), path.Stored.Localized ? Bind(query.Locale) : Expression.Constant("")),
            Expression.Equal(Expression.Property(reference, nameof(Reference.Field)), Expression.Constant(path.ApiPath)),
        }.Aggregate<Expression>(Expression.AndAlso);

        Expression References(Expression? also) =>
            Any(query.Context.References, reference, also is null ? condition : Expression.AndAlso(condition, also));

        if (path.Rest.Length > 0)
        {
            // A negative condition holds when no target matches, as on a block, so an entry with no target counts as
            // a missing value, as a null column does.
            var positive = filter.Operator switch
            {
                FilterOperator.NotEqual => FilterOperator.Equal,
                FilterOperator.NotIn => FilterOperator.In,
                FilterOperator.IsNull => FilterOperator.IsNotNull,
                _ => filter.Operator,
            };

            if (Hop(filter with { Operator = positive }, path, scope, targetId, scope.Asked ?? filter.Operator) is not { } target)
            {
                return Fail();
            }

            return positive == filter.Operator ? References(target) : Expression.Not(References(target));
        }

        var type = TypeOf(path.Stored);
        if (!Allowed(filter, type.FilterOperators, scope))
        {
            return Fail();
        }

        // A target the client cannot read must not match, or the filter would tell that it exists. Hop does this for a
        // path that goes on through the relation.
        var readable = scope.Trusted ? null : ReadableTarget(path, targetId, scope);

        Expression Targets(Expression? also) => (readable, also) switch
        {
            (null, _) => References(also),
            (_, null) => References(readable),
            _ => References(Expression.AndAlso(readable, also)),
        };

        (object? Value, string? Problem) ReadId(object? value) =>
            SystemField.TryRead(value, typeof(Guid), out var id) ? (id, null) : (null, "is not an entry id: expected a GUID");

        switch (filter.Operator)
        {
            case FilterOperator.Equal:
                return One(filter, scope, ReadId) is { } id ? Targets(Expression.Equal(targetId, Bind(id, typeof(Guid)))) : Fail();
            case FilterOperator.In:
                return Many(filter, scope, ReadId) is { } ids ? Targets(InList(targetId, ids)) : Fail();
            case FilterOperator.IsNotNull:
                return Targets(null);
            case FilterOperator.IsNull:
                return Expression.Not(Targets(null));
            default:
                return NotAllowed(filter, scope);
        }
    }

    // The condition a reference's target must meet to count, or null when every target does.
    private Expression? ReadableTarget(FieldPath path, Expression targetId, Scope scope)
    {
        var target = path.Stored.Field.Relation is { } relation ? query.Schema.FindContentType(relation.Target) : null;
        if (target is null)
        {
            return null;
        }

        if (!query.Access.CanReadType(target))
        {
            return Expression.Constant(false);
        }

        if (query.Access.RowRule(target) is null)
        {
            return ShownTarget(target, targetId);
        }

        var row = Expression.Parameter(typeof(QueryRow), "target");
        var rule = Where(target, null, row, trusted: false, $"{scope.Prefix}{path.ApiPath}.", scope.Hops + 1, asked: null)!;

        return Any(query.Rows(target), row, Expression.AndAlso(Expression.Equal(DocumentId(row), targetId), rule));
    }

    // A filter on the id of a relation matches only a target the same read would show: one of that type that is not in the
    // trash, in the site of a site-scoped type, and for a published read published. A row rule's own condition goes through
    // the rows of the site already, so this is for a target without one.
    private MethodCallExpression ShownTarget(SchemaType target, Expression targetId)
    {
        var document = Expression.Parameter(typeof(Document), "document");
        Expression shown = Expression.AndAlso(
            Expression.Equal(Expression.Property(document, nameof(Document.DocumentId)), targetId),
            Expression.Equal(Expression.Property(document, nameof(Document.Type)), Expression.Constant(target.Name)));
        shown = Expression.AndAlso(shown, Expression.Equal(Expression.Property(document, nameof(Document.DeletedAt)), Expression.Constant(null, typeof(DateTimeOffset?))));
        if (target.Scope == ContentScope.Site && query.Site is { } site)
        {
            shown = Expression.AndAlso(shown, Expression.Equal(Expression.Property(document, nameof(Document.SiteId)), Bind<Guid?>(site.Value)));
        }

        if (query.State == ContentState.Published)
        {
            shown = Expression.AndAlso(shown, Expression.NotEqual(Expression.Property(document, nameof(Document.PublishedVersion)), Expression.Constant(null, typeof(int?))));
        }

        return Any(query.Context.Documents, document, shown);
    }

    // The target's row in the same state and locale; decision 44 rules out fallback-aware filtering.
    private MethodCallExpression? Hop(FieldFilter filter, FieldPath path, Scope scope, Expression targetId, FilterOperator asked)
    {
        var target = path.Stored.Field.Relation is { } relation ? query.Schema.FindContentType(relation.Target) : null;
        if (target is null)
        {
            Invalid(filter, scope, "goes through a field that is not a relation");
            return null;
        }

        if (scope.Hops >= query.Options.MaxPopulateDepth)
        {
            query.Report(scope.PathOf(filter), QueryErrorCodes.LimitExceeded, $"'{scope.PathOf(filter)}' goes through more than the {query.Options.MaxPopulateDepth} relations allowed.");
            return null;
        }

        if (!scope.Trusted && !query.Access.CanReadType(target))
        {
            query.Report(scope.PathOf(filter), QueryErrorCodes.UnknownField, $"'{scope.PathOf(filter)}' is not a field of {scope.Type.Name} that can be read.");
            return null;
        }

        if (!query.CanEnter(target, scope.Trusted))
        {
            return null;
        }

        var row = Expression.Parameter(typeof(QueryRow), "target");
        var rest = new FieldFilter(string.Join('.', path.Rest), filter.Operator, filter.Value);
        var inner = Where(target, rest, row, scope.Trusted, $"{scope.Prefix}{path.ApiPath}.", scope.Hops + 1, asked)!;

        return Any(query.Rows(target), row, Expression.AndAlso(Expression.Equal(DocumentId(row), targetId), inner));
    }

    private object? One(FieldFilter filter, Scope scope, Func<object?, (object? Value, string? Problem)> coerce)
    {
        if (filter.Value is null or (IEnumerable and not string))
        {
            Invalid(filter, scope, "needs one value");
            return null;
        }

        var (value, problem) = coerce(filter.Value);
        if (problem is not null)
        {
            Invalid(filter, scope, problem);
            return null;
        }

        return value;
    }

    private List<object?>? Many(FieldFilter filter, Scope scope, Func<object?, (object? Value, string? Problem)> coerce)
    {
        List<object?> raw = filter.Value is IEnumerable items and not string ? [.. items.Cast<object?>()] : filter.Value is null ? [] : [filter.Value];
        if (raw.Count == 0)
        {
            Invalid(filter, scope, "needs at least one value");
            return null;
        }

        List<object?> values = [];
        foreach (var item in raw)
        {
            var (value, problem) = item is null ? (null, "needs a value") : coerce(item);
            if (problem is not null)
            {
                Invalid(filter, scope, problem);
                return null;
            }

            values.Add(value);
        }

        return values;
    }

    private static (object? Value, string? Problem) Coerce(object? value, IFieldType type, SchemaField field, Type columnType)
    {
        if (!type.TryCoerce(value, field, out var result, out var message))
        {
            return (null, message ?? "is not a value of this field");
        }

        var target = Nullable.GetUnderlyingType(columnType) ?? columnType;
        return result?.GetType() == target ? (result, null) : (null, "cannot be compared with this field");
    }

    private IFieldType TypeOf(StoredField stored) => query.Schema.FieldTypes.Find(stored.Field.FieldType)!;

    private static MethodCallExpression ColumnOf(Expression row, StoredField stored) =>
        QueryExpressions.Column(Expression.Property(row, stored.Localized ? nameof(QueryRow.Localized) : nameof(QueryRow.Shared)), stored.Column, stored.ClrType!);

    private bool Allowed(FieldFilter filter, IEnumerable<FilterOperator> operators, Scope scope)
    {
        if (operators.Contains(scope.Asked ?? filter.Operator))
        {
            return true;
        }

        NotAllowed(filter, scope);
        return false;
    }

    private ConstantExpression NotAllowed(FieldFilter filter, Scope scope)
    {
        query.Report(scope.PathOf(filter), QueryErrorCodes.OperatorNotAllowed, $"'{scope.PathOf(filter)}' does not allow the operator {scope.Asked ?? filter.Operator}.");
        return Fail();
    }

    private ConstantExpression Invalid(FieldFilter filter, Scope scope, string problem)
    {
        query.Report(scope.PathOf(filter), QueryErrorCodes.InvalidValue, $"The filter on '{scope.PathOf(filter)}' {problem}.");
        return Fail();
    }

    private static ConstantExpression Fail() => Expression.Constant(false);
}
