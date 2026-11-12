namespace FluentCMS.Client;

// The field types the generated builders return. Each has the members of the SDK's type of the same name, so that
// ArticleFields.Title.Contains("x") reads the same in a host and in a client, and gives the same filter.
public abstract class QueryField
{
    protected QueryField(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Path = path;
    }

    public string Path { get; }

    public override string ToString() => Path;

    private protected FieldFilter Filter(FilterOperator @operator, object? value = null) => new(Path, @operator, value);

    private protected static string Text(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value;
    }

    private protected static T[] List<T>(IReadOnlyList<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return [.. values];
    }
}

public sealed class TextField(string path) : QueryField(path)
{
    public FieldFilter Eq(string value) => Filter(FilterOperator.Equal, Text(value));

    public FieldFilter Ne(string value) => Filter(FilterOperator.NotEqual, Text(value));

    public FieldFilter In(params IReadOnlyList<string> values) => Filter(FilterOperator.In, List(values));

    public FieldFilter NotIn(params IReadOnlyList<string> values) => Filter(FilterOperator.NotIn, List(values));

    public FieldFilter Contains(string value) => Filter(FilterOperator.Contains, Text(value));

    public FieldFilter StartsWith(string value) => Filter(FilterOperator.StartsWith, Text(value));

    public FieldFilter IsNull() => Filter(FilterOperator.IsNull);

    public FieldFilter IsNotNull() => Filter(FilterOperator.IsNotNull);

    public SortField Ascending() => new(Path);

    public SortField Descending() => new(Path, Descending: true);
}

public sealed class LongTextField(string path) : QueryField(path)
{
    public FieldFilter Contains(string value) => Filter(FilterOperator.Contains, Text(value));

    public FieldFilter StartsWith(string value) => Filter(FilterOperator.StartsWith, Text(value));

    public FieldFilter IsNull() => Filter(FilterOperator.IsNull);

    public FieldFilter IsNotNull() => Filter(FilterOperator.IsNotNull);
}

public sealed class RichTextField(string path) : QueryField(path)
{
    public FieldFilter IsNull() => Filter(FilterOperator.IsNull);

    public FieldFilter IsNotNull() => Filter(FilterOperator.IsNotNull);
}

public sealed class NumberField<T>(string path) : QueryField(path)
    where T : struct
{
    public FieldFilter Eq(T value) => Filter(FilterOperator.Equal, value);

    public FieldFilter Ne(T value) => Filter(FilterOperator.NotEqual, value);

    public FieldFilter In(params IReadOnlyList<T> values) => Filter(FilterOperator.In, List(values));

    public FieldFilter NotIn(params IReadOnlyList<T> values) => Filter(FilterOperator.NotIn, List(values));

    public FieldFilter Lt(T value) => Filter(FilterOperator.LessThan, value);

    public FieldFilter Lte(T value) => Filter(FilterOperator.LessThanOrEqual, value);

    public FieldFilter Gt(T value) => Filter(FilterOperator.GreaterThan, value);

    public FieldFilter Gte(T value) => Filter(FilterOperator.GreaterThanOrEqual, value);

    public FieldFilter Between(T from, T to) => Filter(FilterOperator.Between, new[] { from, to });

    public FieldFilter IsNull() => Filter(FilterOperator.IsNull);

    public FieldFilter IsNotNull() => Filter(FilterOperator.IsNotNull);

    public SortField Ascending() => new(Path);

    public SortField Descending() => new(Path, Descending: true);
}

public sealed class DateField<T>(string path) : QueryField(path)
    where T : struct
{
    public FieldFilter Eq(T value) => Filter(FilterOperator.Equal, value);

    public FieldFilter Ne(T value) => Filter(FilterOperator.NotEqual, value);

    public FieldFilter In(params IReadOnlyList<T> values) => Filter(FilterOperator.In, List(values));

    public FieldFilter NotIn(params IReadOnlyList<T> values) => Filter(FilterOperator.NotIn, List(values));

    public FieldFilter Lt(T value) => Filter(FilterOperator.LessThan, value);

    public FieldFilter Lte(T value) => Filter(FilterOperator.LessThanOrEqual, value);

    public FieldFilter Gt(T value) => Filter(FilterOperator.GreaterThan, value);

    public FieldFilter Gte(T value) => Filter(FilterOperator.GreaterThanOrEqual, value);

    public FieldFilter Between(T from, T to) => Filter(FilterOperator.Between, new[] { from, to });

    public FieldFilter IsNull() => Filter(FilterOperator.IsNull);

    public FieldFilter IsNotNull() => Filter(FilterOperator.IsNotNull);

    public SortField Ascending() => new(Path);

    public SortField Descending() => new(Path, Descending: true);
}

public sealed class BooleanField(string path) : QueryField(path)
{
    public FieldFilter Eq(bool value) => Filter(FilterOperator.Equal, value);

    public FieldFilter Ne(bool value) => Filter(FilterOperator.NotEqual, value);

    public FieldFilter IsNull() => Filter(FilterOperator.IsNull);

    public FieldFilter IsNotNull() => Filter(FilterOperator.IsNotNull);

    public SortField Ascending() => new(Path);

    public SortField Descending() => new(Path, Descending: true);
}

public sealed class SelectField(string path) : QueryField(path)
{
    public FieldFilter Eq(string option) => Filter(FilterOperator.Equal, Text(option));

    public FieldFilter Ne(string option) => Filter(FilterOperator.NotEqual, Text(option));

    public FieldFilter In(params IReadOnlyList<string> options) => Filter(FilterOperator.In, List(options));

    public FieldFilter NotIn(params IReadOnlyList<string> options) => Filter(FilterOperator.NotIn, List(options));

    public FieldFilter Contains(string value) => Filter(FilterOperator.Contains, Text(value));

    public FieldFilter IsNull() => Filter(FilterOperator.IsNull);

    public FieldFilter IsNotNull() => Filter(FilterOperator.IsNotNull);

    public SortField Ascending() => new(Path);

    public SortField Descending() => new(Path, Descending: true);
}

public sealed class SelectListField(string path) : QueryField(path)
{
    public FieldFilter Contains(string option) => Filter(FilterOperator.Contains, Text(option));

    public FieldFilter In(params IReadOnlyList<string> options) => Filter(FilterOperator.In, List(options));

    public FieldFilter NotIn(params IReadOnlyList<string> options) => Filter(FilterOperator.NotIn, List(options));

    public FieldFilter IsNull() => Filter(FilterOperator.IsNull);

    public FieldFilter IsNotNull() => Filter(FilterOperator.IsNotNull);
}

public sealed class RelationField(string path) : QueryField(path)
{
    public FieldFilter Eq(Guid id) => Filter(FilterOperator.Equal, id);

    public FieldFilter In(params IReadOnlyList<Guid> ids) => Filter(FilterOperator.In, List(ids));

    public FieldFilter IsNull() => Filter(FilterOperator.IsNull);

    public FieldFilter IsNotNull() => Filter(FilterOperator.IsNotNull);
}

public sealed class MediaField(string path) : QueryField(path)
{
    public FieldFilter Eq(Guid assetId) => Filter(FilterOperator.Equal, assetId);

    public FieldFilter In(params IReadOnlyList<Guid> assetIds) => Filter(FilterOperator.In, List(assetIds));
}

public sealed class JsonField(string path) : QueryField(path)
{
    public JsonKeyField Key(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_')
            ? new JsonKeyField($"{Path}.{name}")
            : throw new ArgumentException("A filter reaches one key inside JSON, named with ASCII letters, digits and underscores.", nameof(name));
    }

    public FieldFilter IsNull() => Filter(FilterOperator.IsNull);

    public FieldFilter IsNotNull() => Filter(FilterOperator.IsNotNull);
}

public sealed class JsonKeyField(string path) : QueryField(path)
{
    public FieldFilter Eq(string value) => Filter(FilterOperator.Equal, Text(value));

    public FieldFilter Eq(long value) => Filter(FilterOperator.Equal, value);

    public FieldFilter Eq(decimal value) => Filter(FilterOperator.Equal, value);

    public FieldFilter Eq(bool value) => Filter(FilterOperator.Equal, value);

    public FieldFilter Ne(string value) => Filter(FilterOperator.NotEqual, Text(value));

    public FieldFilter Ne(long value) => Filter(FilterOperator.NotEqual, value);

    public FieldFilter Ne(decimal value) => Filter(FilterOperator.NotEqual, value);

    public FieldFilter Ne(bool value) => Filter(FilterOperator.NotEqual, value);

    public FieldFilter Contains(string value) => Filter(FilterOperator.Contains, Text(value));
}

// The generated builder of a blocks field derives from it, with a member per block type of the field.
public class BlocksField(string path) : QueryField(path)
{
    public FieldFilter HasBlock(string blockType)
    {
        ArgumentException.ThrowIfNullOrEmpty(blockType);
        return Filter(FilterOperator.HasBlock, blockType);
    }

    public FieldFilter IsNull() => Filter(FilterOperator.IsNull);

    public FieldFilter IsNotNull() => Filter(FilterOperator.IsNotNull);
}

public sealed class BlockTextField(string path) : QueryField(path)
{
    public FieldFilter Eq(string value) => Filter(FilterOperator.Equal, Text(value));

    public FieldFilter Ne(string value) => Filter(FilterOperator.NotEqual, Text(value));

    public FieldFilter Contains(string value) => Filter(FilterOperator.Contains, Text(value));
}

public sealed class BlockValueField<T>(string path) : QueryField(path)
    where T : struct
{
    public FieldFilter Eq(T value) => Filter(FilterOperator.Equal, value);

    public FieldFilter Ne(T value) => Filter(FilterOperator.NotEqual, value);
}

public sealed class BlockContainsField(string path) : QueryField(path)
{
    public FieldFilter Contains(string value) => Filter(FilterOperator.Contains, Text(value));
}
