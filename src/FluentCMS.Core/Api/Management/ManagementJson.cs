using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Content;
using FluentCMS.Publishing;

namespace FluentCMS.Core.Api.Management;

// The shapes of the content group that are not an entry or a list of entries. Names are camel case, enum values too.
internal static class ManagementJson
{
    public static JsonObject Published(PublishResult<ContentDocument> result) => new()
    {
        ["entry"] = ContentJson.Entry(result.Entry),
        ["warnings"] = Warnings(result.Warnings),
    };

    public static JsonObject Schedule(ScheduleInfo schedule) => new()
    {
        ["action"] = Name(schedule.Action),
        ["locale"] = schedule.Locale,
        ["runAt"] = EntryJson.Moment(schedule.RunAt),
        ["timeZone"] = schedule.TimeZone,
        ["version"] = schedule.Version,
    };

    public static JsonObject Versions(IReadOnlyList<VersionInfo> versions) => new()
    {
        ["items"] = new JsonArray([.. versions.Select(version => (JsonNode)new JsonObject
        {
            ["number"] = version.Number,
            ["locale"] = version.Locale,
            ["status"] = Name(version.Status),
            ["authorId"] = version.AuthorId,
            ["origin"] = Name(version.Origin),
            ["clientId"] = version.ClientId,
            ["message"] = version.Message,
            ["createdAt"] = EntryJson.Moment(version.CreatedAt),
        })]),
    };

    public static JsonObject Diff(int from, int to, IReadOnlyList<VersionChange> changes) => new()
    {
        ["from"] = from,
        ["to"] = to,
        ["changes"] = new JsonArray([.. changes.Select(change => (JsonNode)new JsonObject
        {
            ["path"] = change.Path,
            ["kind"] = Name(change.Kind),
            ["before"] = change.Before?.DeepClone(),
            ["after"] = change.After?.DeepClone(),
        })]),
    };

    // A failed entry carries the problem the single call would have answered, without the trace id of the request.
    public static JsonObject Bulk(BulkResult result, bool authenticated, ErrorCodeCatalogue? codes = null) => new()
    {
        ["items"] = new JsonArray([.. result.Items.Select(item => (JsonNode)Item(item, authenticated, codes))]),
        ["cancelled"] = result.Cancelled,
    };

    private static JsonObject Item(BulkItemResult item, bool authenticated, ErrorCodeCatalogue? codes)
    {
        JsonObject json = new()
        {
            ["index"] = item.Index,
            ["id"] = item.Id,
            ["status"] = Name(item.Status),
        };
        if (item.Version is { } version)
        {
            json["version"] = version;
        }

        if (item.Warnings.Count > 0)
        {
            json["warnings"] = Warnings(item.Warnings);
        }

        if (item.Problem is { } problem)
        {
            json["problem"] = JsonSerializer.SerializeToNode(ProblemWriter.Details(ProblemMapper.Map(problem, authenticated, codes)), JsonSerializerOptions.Web);
        }

        return json;
    }

    private static JsonArray Warnings(IReadOnlyList<PublishWarning> warnings) =>
        new([.. warnings.Select(warning =>
        {
            JsonObject json = new()
            {
                ["path"] = warning.Path,
                ["code"] = warning.Code,
                ["message"] = warning.Message,
            };
            if (warning.TargetType is { } targetType)
            {
                json["targetType"] = targetType;
            }

            if (warning.TargetId is { } targetId)
            {
                json["targetId"] = targetId;
            }

            return (JsonNode)json;
        })]);

    private static string Name<T>(T value)
        where T : struct, Enum => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
}
