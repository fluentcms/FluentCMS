namespace FluentCMS.Content;

/// <summary>
/// A field of a content type, a component or a block, named by its path in the API shape, whose members give the
/// filters and sorts its kind supports. The builders that the source generator and <c>fluentcms generate csharp</c>
/// emit return these types, so that <c>ArticleFields.Title.Contains("x")</c> gives the same <see cref="FieldFilter"/>
/// as <c>new FieldFilter("title", FilterOperator.Contains, "x")</c>. A field holds its path only, not the schema.
/// </summary>
public abstract class QueryField
{
    /// <summary>Creates a field at a path.</summary>
    /// <param name="path">The dotted path of the field in the API shape.</param>
    protected QueryField(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Path = path;
    }

    /// <summary>The dotted path of the field in the API shape, such as <c>title</c> or <c>seo.metaTitle</c>.</summary>
    public string Path { get; }

    /// <summary>Gets the path of the field.</summary>
    /// <returns>The path.</returns>
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
