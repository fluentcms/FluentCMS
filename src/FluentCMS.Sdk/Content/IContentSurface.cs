using System.Text.Json.Nodes;

namespace FluentCMS.Content;

/// <summary>
/// What the REST APIs settle around <see cref="IContentService"/>, for another surface over content, such as GraphQL, to
/// answer as they do: their limits, the JSON of an entry, the problem of an exception and what the principal may read.
/// Every answer is about the principal of the current <see cref="OperationContext"/>.
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

    /// <summary>Describes an exception as the problem the REST APIs answer it with.</summary>
    /// <param name="exception">The exception, usually one the content service threw.</param>
    /// <returns>The problem. An exception that is none of the engine's refusals is a 500 with a detail that names no cause.</returns>
    ContentProblem Describe(Exception exception);
}

/// <summary>The problem the REST APIs answer an exception with.</summary>
/// <param name="Code">The <c>FCMS</c> code.</param>
/// <param name="Status">The HTTP status.</param>
/// <param name="Title">The title of the code.</param>
/// <param name="Detail">What went wrong, for the client.</param>
public sealed record ContentProblem(string Code, int Status, string Title, string Detail)
{
    /// <summary>The validation errors, each with the path of its field; empty for a problem that has none.</summary>
    public IReadOnlyList<ValidationError> Errors { get; init; } = [];

    /// <summary>The current entry of a version conflict, as JSON, or <see langword="null"/>.</summary>
    public JsonObject? Current { get; init; }
}
