using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Content.Query;
using FluentCMS.Publishing;

namespace FluentCMS.Core.Api.Management;

// The JSON and the query grammar of the REST APIs for another surface, such as the MCP tools, through the code the
// routes use, so that both answer and read alike.
internal sealed class ContentApiFormat : IContentApiFormat
{
    public JsonObject Entry(ContentDocument entry) => ContentJson.Entry(entry);

    public JsonObject List(PagedResult<ContentDocument> result) => ContentJson.List(result);

    public JsonObject Published(PublishResult<ContentDocument> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return ManagementJson.Published(result);
    }

    public JsonObject Schedule(ScheduleInfo schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        return ManagementJson.Schedule(schedule);
    }

    public ContentQuery ParseQuery(IEnumerable<KeyValuePair<string, string>> parameters) => BracketQueryParser.Parse(parameters);

    public ScheduleRequest ParseSchedule(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            throw new ValidationException([new ValidationError("", ValidationErrorCodes.InvalidValue, "A schedule is a JSON object with an action and a time.")]);
        }

        return ContentRequests.Schedule(body);
    }
}
