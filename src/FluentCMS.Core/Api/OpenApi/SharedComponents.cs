using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Idempotency;
using FluentCMS.Publishing;

namespace FluentCMS.Core.Api.OpenApi;

// The components every deployment has: the problem body, the shapes of the content routes that are not an entry, and the
// query parameters of the grammar of spec 4.4 that do not depend on the type.
internal static class SharedComponents
{
    public const string Problem = "problem";
    public const string ProblemResponse = "problem";
    public const string ProblemMediaType = "application/problem+json";
    public const string PublishWarning = "publish-warning";
    public const string BridgeLocale = "bridge-locale";

    public static readonly string[] SingleParameters = ["status", "fallback", "fields", "populate", "depth", "richText"];

    public static readonly string[] PageParameters = ["page", "pageSize", "offset", "limit", "cursor", "withTotal"];

    public static IEnumerable<(string Name, JsonObject Schema)> Schemas()
    {
        yield return (Problem, new JsonObject
        {
            ["type"] = "object",
            ["description"] = "An RFC 9457 problem. code is the FCMS code, errors lists validation errors with the path of each, and current is the entry as it is now on a 412.",
            ["properties"] = new JsonObject
            {
                ["type"] = String("urn:fluentcms:error:{code}"),
                ["title"] = String(),
                ["status"] = new JsonObject { ["type"] = "integer", ["format"] = "int32" },
                ["detail"] = String(),
                ["instance"] = String(),
                ["code"] = String("The FCMS code, such as FCMS3104."),
                ["errors"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject { ["path"] = String(), ["code"] = String(), ["message"] = String() },
                    },
                },
                ["current"] = new JsonObject { ["type"] = "object" },
                ["traceId"] = String(),
            },
        });

        yield return (PublishWarning, new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["path"] = String(),
                ["code"] = String(),
                ["message"] = String(),
                ["targetType"] = String(),
                ["targetId"] = Uuid(),
            },
            ["required"] = new JsonArray("path", "code", "message"),
        });

        yield return ("schedule", new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["action"] = Action(),
                ["locale"] = String(),
                ["runAt"] = DateTime(),
                ["timeZone"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                ["version"] = new JsonObject { ["type"] = "integer", ["format"] = "int32" },
            },
        });

        yield return ("schedule-request", new JsonObject
        {
            ["type"] = "object",
            ["description"] = "at has an offset, or is the wall-clock time in timeZone without one.",
            ["properties"] = new JsonObject
            {
                ["action"] = Action(),
                ["at"] = String("An ISO 8601 date and time, such as 2027-03-01T09:00:00Z."),
                ["timeZone"] = String("An IANA time zone, such as Europe/Berlin."),
            },
            ["required"] = new JsonArray("action", "at"),
            ["additionalProperties"] = false,
        });

        yield return ("versions", new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["items"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["number"] = new JsonObject { ["type"] = "integer", ["format"] = "int32" },
                            ["locale"] = String(),
                            ["status"] = Enum<EntryStatus>(),
                            ["authorId"] = Uuid(),
                            ["origin"] = Enum<Origin>(),
                            ["clientId"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                            ["message"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                            ["createdAt"] = DateTime(),
                        },
                    },
                },
            },
            ["required"] = new JsonArray("items"),
        });

        yield return ("diff", new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["from"] = new JsonObject { ["type"] = "integer", ["format"] = "int32" },
                ["to"] = new JsonObject { ["type"] = "integer", ["format"] = "int32" },
                ["changes"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["path"] = String(),
                            ["kind"] = Enum<VersionChangeKind>(),
                            ["before"] = new JsonObject(),
                            ["after"] = new JsonObject(),
                        },
                    },
                },
            },
        });

        yield return ("bulk-result", new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["items"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["index"] = new JsonObject { ["type"] = "integer", ["format"] = "int32" },
                            ["id"] = new JsonObject { ["type"] = new JsonArray("string", "null"), ["format"] = "uuid" },
                            ["status"] = Enum<BulkItemStatus>(),
                            ["version"] = new JsonObject { ["type"] = "integer", ["format"] = "int32" },
                            ["warnings"] = new JsonObject { ["type"] = "array", ["items"] = Ref(PublishWarning) },
                            ["problem"] = Ref(Problem),
                        },
                    },
                },
                ["cancelled"] = new JsonObject { ["type"] = "boolean" },
            },
        });
    }

    public static IEnumerable<(string Name, JsonObject Parameter)> Parameters()
    {
        yield return ("site", Query("site", String("A site id or slug, as the X-Cms-Site header. The default site when neither is given.")));
        yield return ("locale", Query("locale", String("A BCP 47 tag, as the X-Cms-Locale header, or all for every locale of an entry as { id, locales }. The site's default locale when neither is given.")));
        yield return (BridgeLocale, Query("locale", String("A locale of the site, as the X-Cms-Locale header. A route beside content refuses one the site does not have and reads nothing by it.")));
        yield return ("status", Query("status", Enum(["published", "draft"]), "Delivery reads published entries and the Management API drafts unless this says otherwise. Delivery reads drafts only with a preview key or a token with the draft scope."));
        yield return ("fallback", Query("fallback", Enum(["none"]), "none reads only what the locale holds; otherwise empty fields come through the site's fallback chain."));
        yield return ("q", Query("q", String(), "Text to search for. The search index finds the matching entries, and the filter, sort and page apply to them. Refused with FCMS2211 when the instance has no search index."));
        yield return ("fields", Query("fields", String(), "Comma-separated fields to return; a component's field is component.field."));
        yield return ("populate", Query("populate", String(), "Comma-separated relations to return as entries instead of ids, or * for all of them."));
        yield return ("depth", Query("depth", new JsonObject { ["type"] = "integer", ["minimum"] = 1 }, "How deep populate goes. Needs populate."));
        yield return ("richText", Query("richText", Enum(["json", "html", "markdown"]), "The form of rich text fields; json by default."));
        yield return ("page", Query("page", new JsonObject { ["type"] = "integer", ["minimum"] = 1 }, "A page number, with pageSize. Not with offset, limit or cursor."));
        yield return ("pageSize", Query("pageSize", new JsonObject { ["type"] = "integer", ["minimum"] = 1 }));
        yield return ("offset", Query("offset", new JsonObject { ["type"] = "integer", ["minimum"] = 0 }, "How many entries to skip, with limit."));
        yield return ("limit", Query("limit", new JsonObject { ["type"] = "integer", ["minimum"] = 1 }));
        yield return ("cursor", Query("cursor", String(), "The nextCursor of the previous page, sent back as it came. A cursor is neither opaque nor signed, and its form may change, so do not read or build one."));
        yield return ("withTotal", Query("withTotal", new JsonObject { ["type"] = "boolean" }, "Whether the list carries its total, which costs a count. Delivery counts only with true, the Management API unless it is false."));
        yield return ("if-match", Header("If-Match", required: true, "The version read, as its ETag, such as \"7\"; \"0\" adds a locale the entry does not have."));
        yield return ("if-match-optional", Header("If-Match", required: false, "The version read, as its ETag, such as \"7\". Without it, or with *, the current version is used."));
        yield return ("idempotency-key", IdempotencyKey());
    }

    public static JsonObject ProblemResponseBody() => new()
    {
        ["description"] = "A problem.",
        ["content"] = new JsonObject { [ProblemMediaType] = new JsonObject { ["schema"] = Ref(Problem) } },
    };

    public static JsonObject Ref(string schema) => new() { ["$ref"] = $"#/components/schemas/{schema}" };

    public static JsonObject ParameterRef(string parameter) => new() { ["$ref"] = $"#/components/parameters/{parameter}" };

    public static JsonObject String(string? description = null)
    {
        JsonObject json = new() { ["type"] = "string" };
        if (description is not null)
        {
            json["description"] = description;
        }

        return json;
    }

    public static JsonObject Enum(IEnumerable<string> values) =>
        new() { ["type"] = "string", ["enum"] = new JsonArray([.. values.Select(value => (JsonNode)value)]) };

    public static JsonObject Enum<T>()
        where T : struct, System.Enum => Enum(System.Enum.GetNames<T>().Select(JsonNamingPolicy.CamelCase.ConvertName));

    private static JsonObject Uuid() => new() { ["type"] = "string", ["format"] = "uuid" };

    private static JsonObject DateTime() => new() { ["type"] = "string", ["format"] = "date-time" };

    private static JsonObject Action() => Enum<ScheduleAction>();

    public static JsonObject Query(string name, JsonObject schema, string? description = null)
    {
        JsonObject json = new() { ["name"] = name, ["in"] = "query" };
        if ((description ?? schema["description"]?.GetValue<string>()) is { } text)
        {
            json["description"] = text;
            schema.Remove("description");
        }

        json["schema"] = schema;
        return json;
    }

    private static JsonObject IdempotencyKey()
    {
        var header = Header(
            IdempotencyStore.Header,
            required: false,
            "A key of the client's own, such as a UUID, that makes a repeat of the request answer what the first answered instead of running again. It is kept for the principal and site for FluentCms:Idempotency:Window (24 hours by default). The same key with another request is FCMS3141 (422), and a repeat while the first still runs is FCMS3142 (409).");
        header["schema"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = IdempotencyStore.MaxKeyLength, ["pattern"] = "^[!-~]+$" };
        return header;
    }

    private static JsonObject Header(string name, bool required, string description) => new()
    {
        ["name"] = name,
        ["in"] = "header",
        ["required"] = required,
        ["description"] = description,
        ["schema"] = String(),
    };
}
