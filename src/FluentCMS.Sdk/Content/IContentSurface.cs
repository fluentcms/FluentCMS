using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Publishing;

namespace FluentCMS.Content;

/// <summary>
/// What the REST APIs settle around <see cref="IContentService"/>, for another surface over content, such as GraphQL, to
/// answer as they do: their limits, their JSON, their query grammar and what the principal may read. Every answer about
/// access is about the principal of the current <see cref="OperationContext"/>. The problem of an exception is <c>IProblemDescriber</c>'s.
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

    /// <summary>Writes a page of entries as the REST APIs answer a list: <c>items</c> with the paging.</summary>
    /// <param name="page">The page, as the content service returned it.</param>
    /// <returns>The JSON object.</returns>
    JsonObject ToJson(PagedResult<ContentDocument> page);

    /// <summary>Writes the result of a publish as the Management API answers it: <c>entry</c> and <c>warnings</c>.</summary>
    /// <param name="result">The result, as the content service returned it.</param>
    /// <returns>The JSON object.</returns>
    JsonObject ToJson(PublishResult<ContentDocument> result);

    /// <summary>Writes a schedule as the Management API answers it.</summary>
    /// <param name="schedule">The schedule, as the content service returned it.</param>
    /// <returns>The JSON object.</returns>
    JsonObject ToJson(ScheduleInfo schedule);

    /// <summary>
    /// Reads the query parameters of a list of the REST APIs, such as <c>filter[title][eq]</c>, <c>sort</c> and
    /// <c>pageSize</c>, as the query the content service takes.
    /// </summary>
    /// <param name="parameters">The parameters by name, each name once.</param>
    /// <returns>The query. Its locale is the one the parameters name, or none.</returns>
    /// <exception cref="ValidationException">A parameter is not part of the grammar, or its value is not one it takes.</exception>
    ContentQuery ParseQuery(IEnumerable<KeyValuePair<string, string>> parameters);

    /// <summary>
    /// Reads the body of a schedule request of the Management API: <c>action</c>, <c>at</c> and an optional
    /// <c>timeZone</c>.
    /// </summary>
    /// <param name="body">The JSON object.</param>
    /// <returns>The request the content service takes.</returns>
    /// <exception cref="ValidationException">A key is missing, unknown or holds a value it does not take.</exception>
    ScheduleRequest ParseSchedule(JsonElement body);
}
