using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Publishing;

namespace FluentCMS.Content;

/// <summary>
/// The forms content takes on the REST APIs, for a surface that answers with the same JSON and takes the same query in
/// another form, such as the tools of the MCP server. It reads no content and checks no permission.
/// </summary>
public interface IContentApiFormat
{
    /// <summary>Writes an entry as the Management API answers it: system and content fields side by side.</summary>
    /// <param name="entry">The entry, as the content service returned it.</param>
    /// <returns>The JSON object.</returns>
    JsonObject Entry(ContentDocument entry);

    /// <summary>Writes a page of entries as the Management API answers a list: <c>items</c> with the paging.</summary>
    /// <param name="result">The page, as the content service returned it.</param>
    /// <returns>The JSON object.</returns>
    JsonObject List(PagedResult<ContentDocument> result);

    /// <summary>Writes the result of a publish as the Management API answers it: <c>entry</c> and <c>warnings</c>.</summary>
    /// <param name="result">The result, as the content service returned it.</param>
    /// <returns>The JSON object.</returns>
    JsonObject Published(PublishResult<ContentDocument> result);

    /// <summary>Writes a schedule as the Management API answers it.</summary>
    /// <param name="schedule">The schedule, as the content service returned it.</param>
    /// <returns>The JSON object.</returns>
    JsonObject Schedule(ScheduleInfo schedule);

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
