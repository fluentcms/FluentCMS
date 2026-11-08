using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Schema;

namespace FluentCMS.Core.Api.OpenApi;

// A route under {type} as the API explorer found it, with what its ContentOperation says it reads and answers.
internal sealed record ContentRoute(string Path, string Method, string Surface, ContentOperation Operation);

// Writes each content route once per content type, with that type's entry schema, filter and sort, so a client generator
// gets a typed operation per type instead of one that takes any type.
internal sealed class ContentPaths(SchemaModel schema, ContentSchemas schemas)
{
    public const string Delivery = "delivery";
    public const string TypeParameter = "{type}";

    private const string FilterDescription =
        "The filter grammar: filter[field][operator]=value. Conditions are combined with AND; filter[or][0][...], filter[and][0][...] " +
        "and filter[not][...] group them. in, nin and between take a comma-separated list (\\, for a comma), null and notNull take " +
        "true or false, and hasBlock a block name. A field of a single component is component.field.";

    public static string Tag(string surface, string type) => surface == Delivery ? "Delivery" : $"Content: {type}";

    public IEnumerable<(string Name, JsonObject Parameter)> Parameters()
    {
        foreach (var type in schema.ContentTypes)
        {
            yield return ($"filter-{type.Name}", Filter(type));
            yield return ($"sort-{type.Name}", Sort(type));
        }
    }

    public IEnumerable<(string Path, JsonObject Item)> Paths(IEnumerable<ContentRoute> routes)
    {
        var templates = routes.ToList();
        foreach (var type in schema.ContentTypes)
        {
            foreach (var group in templates.GroupBy(route => route.Path, StringComparer.Ordinal))
            {
                JsonObject item = [];
                foreach (var route in group)
                {
                    if (Operation(route, type) is { } operation)
                    {
                        item[route.Method.ToLowerInvariant()] = operation;
                    }
                }

                if (item.Count > 0)
                {
                    yield return (group.Key.Replace(TypeParameter, type.Name, StringComparison.Ordinal), item);
                }
            }
        }
    }

    private static JsonObject? Operation(ContentRoute route, SchemaType type)
    {
        var described = route.Operation;
        JsonArray parameters = [];
        foreach (var name in PathParameters(route.Path))
        {
            if (PathParameter(name, type) is not { } parameter)
            {
                return null;
            }

            parameters.Add(parameter);
        }

        parameters.Add(SharedComponents.ParameterRef("site"));
        parameters.Add(SharedComponents.ParameterRef("locale"));
        var list = described.Query == ContentParameters.List && !(type.Kind == SchemaTypeKind.Singleton && described.Response == ContentPayload.EntryOrList);
        if (described.Query is ContentParameters.Single or ContentParameters.List)
        {
            foreach (var name in SharedComponents.SingleParameters)
            {
                parameters.Add(SharedComponents.ParameterRef(name));
            }
        }

        if (list || described.Query == ContentParameters.Filter)
        {
            parameters.Add(SharedComponents.ParameterRef($"filter-{type.Name}"));
        }

        if (list)
        {
            parameters.Add(SharedComponents.ParameterRef("q"));
            parameters.Add(SharedComponents.ParameterRef($"sort-{type.Name}"));
            foreach (var name in SharedComponents.PageParameters)
            {
                parameters.Add(SharedComponents.ParameterRef(name));
            }
        }

        if (described.IfMatch != ContentPrecondition.None)
        {
            parameters.Add(SharedComponents.ParameterRef(described.IfMatch == ContentPrecondition.Required ? "if-match" : "if-match-optional"));
        }

        JsonObject operation = new()
        {
            ["tags"] = new JsonArray(Tag(route.Surface, type.Name)),
            ["summary"] = described.Summary,
            ["operationId"] = (route.Surface == Delivery ? Delivery + Pascal(described.Name) : described.Name) + Pascal(type.Name),
            ["parameters"] = parameters,
            ["security"] = route.Surface == Delivery
                ? new JsonArray(new JsonObject(), Requirement(FluentCmsDocumentTransformer.OAuth), Requirement(FluentCmsDocumentTransformer.ApiKey))
                : new JsonArray(Requirement(FluentCmsDocumentTransformer.OAuth, TokenScopes.Management), Requirement(FluentCmsDocumentTransformer.ApiKey)),
        };

        if (Body(described.Request, type, request: true) is { } request)
        {
            operation["requestBody"] = new JsonObject
            {
                ["required"] = true,
                ["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = request } },
            };
        }

        JsonObject response = new() { ["description"] = described.Summary + "." };
        var payload = type.Kind == SchemaTypeKind.Singleton && described.Response == ContentPayload.EntryOrList ? ContentPayload.Entry : described.Response;
        if (Body(payload, type, request: false) is { } body)
        {
            response["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = body } };
        }

        if (route.Surface != Delivery && described.ETag && payload is ContentPayload.Entry or ContentPayload.Published)
        {
            var when = described.Query == ContentParameters.Single ? "a read of the published version or " : "";
            response["headers"] = new JsonObject
            {
                ["ETag"] = new JsonObject
                {
                    ["description"] = $"The version of the draft, to send back as If-Match. Left out for {when}a value that came through the fallback chain.",
                    ["schema"] = SharedComponents.String(),
                },
            };
        }

        operation["responses"] = new JsonObject
        {
            [described.Status.ToString(System.Globalization.CultureInfo.InvariantCulture)] = response,
            ["default"] = new JsonObject { ["$ref"] = $"#/components/responses/{SharedComponents.ProblemResponse}" },
        };
        return operation;
    }

    private static JsonObject Requirement(string scheme, params string[] scopes) =>
        new() { [scheme] = new JsonArray([.. scopes.Select(scope => (JsonNode)scope)]) };

    private static IEnumerable<string> PathParameters(string path) =>
        path.Split('/')
            .Where(segment => segment.StartsWith('{') && segment.EndsWith('}') && segment != TypeParameter)
            .Select(segment => segment[1..^1]);

    // The path parameters of the content routes. by/{field} takes the type's unique fields, so a type without one has no
    // such route.
    private static JsonObject? PathParameter(string name, SchemaType type)
    {
        var parameter = new JsonObject { ["name"] = name, ["in"] = "path", ["required"] = true };
        switch (name)
        {
            case "id":
                parameter["schema"] = new JsonObject { ["type"] = "string", ["format"] = "uuid" };
                break;
            case "number" or "from" or "to":
                parameter["schema"] = new JsonObject { ["type"] = "integer", ["format"] = "int32", ["minimum"] = 1 };
                break;
            case "action":
                parameter["schema"] = SharedComponents.Enum<FluentCMS.Publishing.ScheduleAction>();
                break;
            case "field":
                var unique = type.Fields.Where(field => field.Unique && !field.Hidden).Select(field => field.Name).ToList();
                if (unique.Count == 0)
                {
                    return null;
                }

                parameter["schema"] = SharedComponents.Enum(unique);
                break;
            default:
                parameter["schema"] = SharedComponents.String();
                break;
        }

        return parameter;
    }

    private static JsonObject? Body(ContentPayload payload, SchemaType type, bool request)
    {
        var entry = new JsonObject { ["$ref"] = OpenApiNames.EntryRef(type.Name) };
        var input = new JsonObject { ["$ref"] = OpenApiNames.InputRef(type.Name) };
        return payload switch
        {
            ContentPayload.Entry when request => input,
            ContentPayload.Entry => entry,
            ContentPayload.List or ContentPayload.EntryOrList => new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["items"] = new JsonObject { ["type"] = "array", ["items"] = entry },
                    ["total"] = new JsonObject { ["type"] = "integer", ["format"] = "int64", ["description"] = "Left out when not counted (see withTotal) and on a page read by cursor." },
                    ["page"] = new JsonObject { ["type"] = "integer", ["format"] = "int32" },
                    ["pageSize"] = new JsonObject { ["type"] = "integer", ["format"] = "int32" },
                    ["nextCursor"] = new JsonObject { ["type"] = "string", ["description"] = "The cursor of the next page, when there is one. Send it back as it came: it is neither opaque nor signed, and its form may change." },
                },
                ["required"] = new JsonArray("items", "pageSize"),
            },
            ContentPayload.Published => new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["entry"] = entry,
                    ["warnings"] = new JsonObject { ["type"] = "array", ["items"] = SharedComponents.Ref(SharedComponents.PublishWarning) },
                },
                ["required"] = new JsonArray("entry", "warnings"),
            },
            ContentPayload.Schedule => SharedComponents.Ref(request ? "schedule-request" : "schedule"),
            ContentPayload.Versions => SharedComponents.Ref("versions"),
            ContentPayload.Diff => SharedComponents.Ref("diff"),
            ContentPayload.Bulk when request => BulkRequest(input),
            ContentPayload.Bulk => SharedComponents.Ref("bulk-result"),
            _ => null,
        };
    }

    private static JsonObject BulkRequest(JsonObject input) => new()
    {
        ["type"] = "object",
        ["description"] = "entries for create; items, or the filter in the query string with cap, for the others. patch is the merge patch of an update for every entry.",
        ["properties"] = new JsonObject
        {
            ["operation"] = SharedComponents.Enum<BulkOperation>(),
            ["entries"] = new JsonObject { ["type"] = "array", ["items"] = input.DeepClone() },
            ["items"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["id"] = new JsonObject { ["type"] = "string", ["format"] = "uuid" },
                        ["patch"] = input.DeepClone(),
                        ["expectedVersion"] = new JsonObject { ["type"] = "integer", ["format"] = "int32" },
                    },
                    ["required"] = new JsonArray("id"),
                    ["additionalProperties"] = false,
                },
            },
            ["cap"] = new JsonObject { ["type"] = "integer", ["format"] = "int32", ["minimum"] = 1, ["description"] = "The most entries the filter may match." },
            ["patch"] = input.DeepClone(),
        },
        ["required"] = new JsonArray("operation"),
        ["additionalProperties"] = false,
    };

    // The filter of a type as one deepObject parameter: a property per field it can filter, each an object of the
    // operators its field type supports, with the operand typed as the field when one value is taken.
    private JsonObject Filter(SchemaType type)
    {
        JsonObject fields = [];
        foreach (var field in SystemField.Fields.Where(field => field.Operators.Length > 0))
        {
            fields[field.Name] = Operators(field.Operators, SystemOperand(field.Name), []);
        }

        foreach (var field in type.Fields.Where(field => !field.Hidden))
        {
            AddFilter(fields, field.Name, field);
            if (field.FieldType == FieldTypeNames.Component && !field.Multiple
                && field.Settings.TryGetValue(FieldSettings.Component, out var name) && schema.FindComponent((string)name) is { } component)
            {
                foreach (var inner in component.Fields.Where(inner => !inner.Hidden))
                {
                    AddFilter(fields, $"{field.Name}.{inner.Name}", inner);
                }
            }
        }

        return new JsonObject
        {
            ["name"] = "filter",
            ["in"] = "query",
            ["description"] = FilterDescription,
            ["style"] = "deepObject",
            ["explode"] = true,
            ["schema"] = new JsonObject { ["type"] = "object", ["properties"] = fields },
        };
    }

    private void AddFilter(JsonObject fields, string path, SchemaField field)
    {
        if (schemas.FieldType(field) is not { FilterOperators.Count: > 0 } fieldType)
        {
            return;
        }

        var blocks = field.FieldType == FieldTypeNames.Blocks ? SettingValues.List(field, FieldSettings.Blocks) : [];
        fields[path] = Operators(fieldType.FilterOperators, Operand(fieldType.OpenApiSchema(field)), blocks);
    }

    private static JsonObject Operators(IEnumerable<FilterOperator> operators, JsonObject operand, IReadOnlyList<string> blocks)
    {
        JsonObject properties = [];
        foreach (var filterOperator in operators.OrderBy(item => item))
        {
            properties[BracketQueryParser.OperatorName(filterOperator)] = filterOperator switch
            {
                FilterOperator.In or FilterOperator.NotIn => SharedComponents.String("A comma-separated list."),
                FilterOperator.Between => SharedComponents.String("Two values separated by a comma."),
                FilterOperator.IsNull or FilterOperator.IsNotNull => new JsonObject { ["type"] = "boolean" },
                FilterOperator.HasBlock => blocks.Count > 0 ? SharedComponents.Enum(blocks) : SharedComponents.String(),
                _ => operand.DeepClone(),
            };
        }

        return new JsonObject { ["type"] = "object", ["properties"] = properties, ["additionalProperties"] = false };
    }

    // One value of a field in a query string: its scalar type and format, or a string for anything structured.
    private static JsonObject Operand(JsonObject fragment)
    {
        var scalar = fragment["type"]?.GetValue<string>() == "array" && fragment["items"] is JsonObject items ? items : fragment;
        if (scalar["type"] is JsonValue type && type.TryGetValue<string>(out var name) && name is "string" or "integer" or "number" or "boolean")
        {
            JsonObject operand = new() { ["type"] = name };
            foreach (var keyword in new[] { "format", "enum" })
            {
                if (scalar[keyword] is { } value)
                {
                    operand[keyword] = value.DeepClone();
                }
            }

            return operand;
        }

        return SharedComponents.String();
    }

    private static JsonObject SystemOperand(string name) => name switch
    {
        "status" => SharedComponents.Enum<FluentCMS.Publishing.EntryStatus>(),
        "version" => new JsonObject { ["type"] = "integer", ["format"] = "int32" },
        "createdAt" or "updatedAt" or "publishedAt" => new JsonObject { ["type"] = "string", ["format"] = "date-time" },
        _ => new JsonObject { ["type"] = "string", ["format"] = "uuid" },
    };

    private JsonObject Sort(SchemaType type)
    {
        var keys = SystemField.Fields.Where(field => field.Sortable).Select(field => field.Name)
            .Concat(type.Fields.Where(field => !field.Hidden && schemas.FieldType(field)?.Sortable(field) == true).Select(field => field.Name))
            .ToList();
        return new JsonObject
        {
            ["name"] = "sort",
            ["in"] = "query",
            ["description"] = $"Comma-separated sort keys, each with - before it for descending. The keys of {type.Name}: {string.Join(", ", keys)}.",
            ["schema"] = new JsonObject { ["type"] = "string", ["examples"] = new JsonArray($"-{keys[0]}") },
        };
    }

    public static string Pascal(string name) =>
        string.Concat(name.Split(['-', '_', '.'], StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

}
