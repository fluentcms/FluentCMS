using System.Text.Json.Nodes;

namespace FluentCMS.Content;

/// <summary>
/// What the REST APIs settle around <see cref="IContentService"/>, for another surface over content, such as GraphQL, to
/// answer as they do: their limits, the JSON of an entry and what the principal may read. Every answer is about the
/// principal of the current <see cref="OperationContext"/>. The problem of an exception is <c>IProblemDescriber</c>'s.
/// </summary>
public interface IContentSurface
{
    /// <summary>The size of a page that names none.</summary>
    int DefaultPageSize { get; }

    /// <summary>The largest page a query may ask for; the content service refuses a larger one.</summary>
    int MaxPageSize { get; }

    /// <summary>The relation hops a query may populate; the content service refuses a deeper path.</summary>
    int MaxPopulateDepth { get; }

    /// <summary>
    /// The fields of a content type that the principal may read in the site of the operation: its system fields and its own
    /// fields, by name.
    /// </summary>
    /// <param name="type">The name of the content type.</param>
    /// <param name="locale">The locale of the read, or <see langword="null"/> for the locale of the operation.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>The names, or <see langword="null"/> when there is no such type or the principal may not read it.</returns>
    ValueTask<IReadOnlySet<string>?> ReadableFieldsAsync(string type, string? locale = null, CancellationToken cancellationToken = default);

    /// <summary>Writes an entry as the REST APIs do: system and content fields side by side, with relations as they were populated.</summary>
    /// <param name="entry">The entry, as the content service returned it.</param>
    /// <returns>The JSON object.</returns>
    JsonObject ToJson(ContentDocument entry);
}
