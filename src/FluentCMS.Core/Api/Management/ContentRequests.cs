using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Content.Query;
using FluentCMS.Publishing;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Core.Api.Management;

// The bodies of the schedule and bulk routes as the engine's requests. A key the request does not use is refused rather
// than ignored, as the engine refuses a value its operation does not use.
internal static class ContentRequests
{
    private static readonly string[] ScheduleKeys = ["action", "at", "timeZone"];
    private static readonly string[] BulkKeys = ["operation", "entries", "items", "cap", "patch"];
    private static readonly string[] ItemKeys = ["id", "patch", "expectedVersion"];

    // With a time zone, at is the wall-clock time there and has no offset; without one, it is an instant and has one.
    public static ScheduleRequest Schedule(JsonElement body)
    {
        List<ValidationError> errors = [];
        Unknown(body, ScheduleKeys, "", errors);

        var action = body.TryGetProperty("action", out var actionValue) ? ActionOf(actionValue, "action", errors) : Missing<ScheduleAction>("action", errors);
        var zone = body.TryGetProperty("timeZone", out var zoneValue) && zoneValue.ValueKind != JsonValueKind.Null
            ? zoneValue.ValueKind == JsonValueKind.String ? zoneValue.GetString() : Invalid<string>("timeZone", "The time zone is a string, such as Europe/Berlin.", errors)
            : null;
        var at = body.TryGetProperty("at", out var atValue) ? Moment(atValue, zone is not null, errors) : Missing<DateTime>("at", errors);

        return errors.Count > 0 ? throw new ValidationException(errors) : new ScheduleRequest(action, at, zone);
    }

    public static ScheduleAction Action(string action) =>
        Parse(action) ?? throw new ValidationException([new ValidationError("action", QueryErrorCodes.InvalidValue, $"'{action}' is not publish or unpublish.")]);

    // A bulk request takes the filter parameters of a list and nothing else of its grammar.
    public static ContentFilter? BulkFilter(HttpRequest request, ApiRequest api)
    {
        OnlyParameters(request, filter: true);
        return RequestQuery.Parse(request, api, single: false).Filter;
    }

    // What the bridge reads, the site and the locale, and for a bulk request its filter.
    public static void OnlyParameters(HttpRequest request, bool filter)
    {
        var refused = request.Query.Keys
            .Where(key => key is not (RequestBridge.SiteParameter or RequestBridge.LocaleParameter)
                && !(filter && (key == "filter" || key.StartsWith("filter[", StringComparison.Ordinal))))
            .Select(key => new ValidationError(key, QueryErrorCodes.InvalidParameter, $"'{key}' does not apply to this request."))
            .ToList();
        if (refused.Count > 0)
        {
            throw new ValidationException(refused);
        }
    }

    public static BulkRequest Bulk(JsonElement body, ContentFilter? filter)
    {
        List<ValidationError> errors = [];
        Unknown(body, BulkKeys, "", errors);

        var operation = body.TryGetProperty("operation", out var operationValue) ? OperationOf(operationValue, errors) : Missing<BulkOperation>("operation", errors);
        var request = new BulkRequest
        {
            Operation = operation,
            Entries = body.TryGetProperty("entries", out var entries) ? Array(entries, "entries", errors, (item, path) => Entry(item, path, errors)) : null,
            Items = body.TryGetProperty("items", out var items) ? Array(items, "items", errors, (item, path) => Item(item, path, errors)) : null,
            Filter = filter,
            Cap = body.TryGetProperty("cap", out var cap) ? Integer(cap, "cap", errors) : null,
            Patch = body.TryGetProperty("patch", out var patch) ? Document(patch, "patch", errors) : null,
        };

        return errors.Count > 0 ? throw new ValidationException(errors) : request;
    }

    private static BulkItem Item(JsonElement item, string path, List<ValidationError> errors)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return Invalid<BulkItem>(path, "An item is an object with an id.", errors) ?? new BulkItem(Guid.Empty);
        }

        Unknown(item, ItemKeys, path + ".", errors);
        var id = item.TryGetProperty("id", out var idValue) && idValue.ValueKind == JsonValueKind.String && Guid.TryParseExact(idValue.GetString(), "D", out var parsed)
            ? parsed
            : Invalid<Guid>(path + ".id", "The id of an item is an entry id.", errors);

        return new BulkItem(id)
        {
            Patch = item.TryGetProperty("patch", out var patch) ? Document(patch, path + ".patch", errors) : null,
            ExpectedVersion = item.TryGetProperty("expectedVersion", out var expected) ? Integer(expected, path + ".expectedVersion", errors) : null,
        };
    }

    private static IReadOnlyList<T>? Array<T>(JsonElement value, string path, List<ValidationError> errors, Func<JsonElement, string, T> read)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return Invalid<IReadOnlyList<T>>(path, $"'{path}' is an array.", errors);
        }

        return [.. value.EnumerateArray().Select((item, index) => read(item, $"{path}[{index}]"))];
    }

    private static ContentDocument Entry(JsonElement value, string path, List<ValidationError> errors) =>
        value.ValueKind == JsonValueKind.Object ? RequestBody.Document(value) : Invalid<ContentDocument>(path, $"'{path}' is an object.", errors) ?? [];

    private static ContentDocument? Document(JsonElement value, string path, List<ValidationError> errors) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.Object => RequestBody.Document(value),
        _ => Invalid<ContentDocument>(path, $"'{path}' is an object.", errors),
    };

    private static int? Integer(JsonElement value, string path, List<ValidationError> errors) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.Number when value.TryGetInt32(out var number) => number,
        _ => Invalid<int?>(path, $"'{path}' is a whole number.", errors),
    };

    private static DateTime Moment(JsonElement value, bool inZone, List<ValidationError> errors)
    {
        if (value.ValueKind != JsonValueKind.String || !value.TryGetDateTime(out var parsed))
        {
            return Invalid<DateTime>("at", "The time is an ISO 8601 date and time, such as 2027-03-01T09:00:00Z.", errors);
        }

        var hasOffset = parsed.Kind != DateTimeKind.Unspecified;
        if (inZone)
        {
            return hasOffset ? Invalid<DateTime>("at", "A time in a time zone is the wall-clock time there, without an offset.", errors) : parsed;
        }

        return hasOffset && value.TryGetDateTimeOffset(out var instant)
            ? instant.UtcDateTime
            : Invalid<DateTime>("at", "A time without a time zone needs an offset, such as Z.", errors);
    }

    private static ScheduleAction ActionOf(JsonElement value, string path, List<ValidationError> errors) =>
        (value.ValueKind == JsonValueKind.String ? Parse(value.GetString()) : null) ?? Invalid<ScheduleAction>(path, "The action is publish or unpublish.", errors);

    private static ScheduleAction? Parse(string? action) => action switch
    {
        "publish" => ScheduleAction.Publish,
        "unpublish" => ScheduleAction.Unpublish,
        _ => null,
    };

    private static BulkOperation OperationOf(JsonElement value, List<ValidationError> errors) =>
        (value.ValueKind == JsonValueKind.String ? value.GetString() : null) switch
        {
            "create" => BulkOperation.Create,
            "update" => BulkOperation.Update,
            "publish" => BulkOperation.Publish,
            "unpublish" => BulkOperation.Unpublish,
            "delete" => BulkOperation.Delete,
            _ => Invalid<BulkOperation>("operation", "The operation is create, update, publish, unpublish or delete.", errors),
        };

    private static void Unknown(JsonElement body, string[] known, string prefix, List<ValidationError> errors) =>
        errors.AddRange(body.EnumerateObject()
            .Where(property => !known.Contains(property.Name, StringComparer.Ordinal))
            .Select(property => new ValidationError(prefix + property.Name, ValidationErrorCodes.InvalidValue, $"'{property.Name}' is not part of this request.")));

    private static T Missing<T>(string path, List<ValidationError> errors)
        where T : struct
    {
        errors.Add(new ValidationError(path, ValidationErrorCodes.Required, $"'{path}' is required."));
        return default;
    }

    private static T? Invalid<T>(string path, string message, List<ValidationError> errors)
    {
        errors.Add(new ValidationError(path, ValidationErrorCodes.InvalidValue, message));
        return default;
    }
}
