using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Api.Management;
using FluentCMS.Core.Content;
using FluentCMS.Core.Content.Query;
using FluentCMS.Publishing;
using FluentCMS.Schema;

namespace FluentCMS.Core.Api.Common;

// The SDK's seam for a surface over content that is not REST, such as the GraphQL plugin and the MCP tools: it answers
// with the JSON, the limits, the query grammar and the access questions the REST APIs use, through the code of the
// routes, so that the two cannot drift apart.
internal sealed class ContentSurface(
    SchemaModel schema,
    ContentQueryOptions options,
    IContentQueryAccess access,
    IOperationContextAccessor operations,
    ManagementShape shape,
    ErrorCodeCatalogue codes) : IContentSurface
{
    public int DefaultPageSize => options.DefaultPageSize;

    public int MaxPageSize => options.MaxPageSize;

    public int MaxPopulateDepth => options.MaxPopulateDepth;

    public async ValueTask<IReadOnlySet<string>?> ReadableFieldsAsync(string type, string? locale = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);

        var operation = operations.Current ?? throw new InvalidOperationException("What a principal may read is asked inside an operation.");
        if (schema.FindContentType(type) is not { } found)
        {
            return null;
        }

        var query = await access.ForQueryAsync(operation.Site, locale ?? operation.Locale, cancellationToken);
        if (!query.CanReadType(found))
        {
            return null;
        }

        return SystemField.Fields.Where(field => shape.Applies || !field.Management).Select(field => field.Name)
            .Concat(found.Fields.Where(field => !field.Hidden).Select(field => field.Name))
            .Where(name => query.CanReadField(found, name))
            .ToHashSet(StringComparer.Ordinal);
    }

    public JsonObject ToJson(ContentDocument entry) => ContentJson.Entry(entry);

    public JsonObject ToJson(PagedResult<ContentDocument> page) => ContentJson.List(page);

    public JsonObject ToJson(PublishResult<ContentDocument> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return ManagementJson.Published(result);
    }

    public JsonObject ToJson(ScheduleInfo schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        return ManagementJson.Schedule(schedule);
    }

    // A principal that is not signed in is told to sign in, as the bulk route tells it.
    public JsonObject ToJson(BulkResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return ManagementJson.Bulk(result, operations.Current?.Principal.IsAnonymous == false, codes);
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
