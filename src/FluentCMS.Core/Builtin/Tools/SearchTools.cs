using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Content;
using FluentCMS.Core.Results;
using FluentCMS.Core.Search;
using FluentCMS.Core.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Builtin.Tools;

// Finding entries and entities by their words, field values and references (SRC-01, MCP-01), within the types the agent may read.
public sealed class SearchTools : IToolProvider
{
    private const string FieldsForm = "[{\"field\": \"price\", \"operator\": \"less_than\", \"value\": 20}]";

    private static readonly string StateNames = string.Join(", ", Enum.GetValues<EntryState>().Select(state => ToolJson.Name(state)));

    private static readonly string OperatorNames = string.Join(", ", Enum.GetValues<FieldOperator>().Select(FieldFilters.Name));

    private static readonly JsonObject FieldsSchema = new()
    {
        ["type"] = "array",
        ["minItems"] = 1,
        ["items"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["field"] = new JsonObject { ["type"] = "string", ["minLength"] = 1 },
                ["operator"] = new JsonObject { ["enum"] = new JsonArray([.. Enum.GetValues<FieldOperator>().Select(op => (JsonNode)FieldFilters.Name(op))]) },
                ["value"] = new JsonObject(),
            },
            ["required"] = new JsonArray("field", "operator", "value"),
            ["additionalProperties"] = false,
        },
    };

    public IReadOnlyList<AgentTool> Tools { get; } =
    [
        new AgentTool(
            "search_entries",
            "Search entries and entities",
            "Finds entries and entities whose latest version has every word of the query, each matched as a prefix, best first: "
            + "'4411' finds a SKU and '441' does too. An entry that references a match is found as well, with the match in 'via', "
            + "such as the page that shows a matching entity. Narrow the search with types, states or published_only, with fields "
            + "on one type's field values, such as price less_than 20, or with references to keep entries that reference an id. "
            + "With one type and fields or references, the query can be left out. Read a hit with get_entry, or an entity's facts "
            + "with get_entity_facts.",
            ToolSchemas.Object(
                ToolSchemas.String("query", "The words to find, such as a name, a SKU or part of a sentence; optional with one type and fields or references.", "4411", required: false),
                ToolSchemas.Strings("types", "Content type slugs to search in; leave out for every type you may read.", "product", required: false),
                ToolSchemas.Strings("states", $"Entry states to keep: {StateNames}.", "published", required: false),
                ToolSchemas.Flag("published_only", "Only entries that have a published version."),
                ToolSchemas.Json(
                    "fields",
                    $"Filters on the latest version's field values, all of which must hold; they need exactly one type in types. Operators: "
                    + $"{OperatorNames}. Text, rich text and enum fields take equals and contains, numbers and date-times the comparisons, "
                    + "booleans and references equals. An entity's field is compared by its fact's value.",
                    FieldsSchema,
                    JsonNode.Parse(FieldsForm)!,
                    required: false),
                ToolSchemas.Uuid("references", "An entry or entity id; only entries whose latest version references it are kept.", required: false),
                ToolSchemas.PositiveInteger("limit", $"At most this many hits, up to {SearchQuery.MaxLimit}; {SearchQuery.DefaultLimit} if left out.", 20, required: false)),
            readOnly: true,
            SearchAsync),
    ];

    private static async Task<ToolResult> SearchAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        var text = arguments.OptionalString("query", "4411");
        var types = arguments.OptionalStrings("types", "product");
        var states = arguments.OptionalStrings("states", "published");
        var publishedOnly = arguments.Flag("published_only");
        var limit = arguments.OptionalPositiveInteger("limit") ?? SearchQuery.DefaultLimit;
        var fields = arguments.OptionalArray("fields", FieldsForm);
        var references = arguments.OptionalGuid("references");
        var errors = new List<Error>(arguments.Errors());
        var filters = fields is { } items ? FieldFiltersOf(items, errors) : null;

        var parsedStates = new List<EntryState>();
        foreach (var name in states ?? [])
        {
            var state = Enum.GetValues<EntryState>().Where(candidate => ToolJson.Name(candidate) == name).ToList();
            if (state.Count == 1)
            {
                parsedStates.Add(state[0]);
            }
            else
            {
                errors.Add(new Error(ToolErrorCodes.ArgumentInvalid, $"'{name}' is not an entry state; use {StateNames}."));
            }
        }

        if (errors.Count > 0)
        {
            return ToolResult.Failure(errors);
        }

        var results = await call.Services.GetRequiredService<ISearch>().SearchAsync(
            new SearchQuery(text ?? string.Empty)
            {
                Fields = filters,
                References = references,
                ContentTypes = types,
                States = states is null ? null : parsedStates,
                PublishedOnly = publishedOnly,
                Limit = limit,
            },
            cancellationToken);

        return ToolResult.From(results, found => new JsonObject
        {
            ["hits"] = new JsonArray([.. found.Hits.Select(hit => new JsonObject
            {
                ["id"] = hit.EntryId.ToString(),
                ["contentType"] = hit.ContentType,
                ["state"] = ToolJson.Name(hit.State),
                ["latestVersion"] = hit.LatestVersion,
                ["publishedVersion"] = hit.PublishedVersion,
                ["via"] = hit.Via?.ToString(),
            })]),
            ["complete"] = found.Complete,
            ["next"] = !found.Complete
                ? "The search stopped after reading the most matches it reads, so hits may be missing. Add a word to narrow it."
                : found.Hits.Count == 0
                    ? "Nothing matched. Search for fewer or shorter words, or leave out a filter."
                    : "Read a hit with get_entry, or an entity's facts with get_entity_facts. A hit with 'via' references that match.",
        });
    }

    // Each item names a field, an operator by its tool name and a value. Whether the field exists and the value fits its type is
    // checked against the type by the search.
    private static List<FieldFilter> FieldFiltersOf(JsonElement items, List<Error> errors)
    {
        var filters = new List<FieldFilter>();
        var position = 0;
        foreach (var item in items.EnumerateArray())
        {
            var at = $"fields[{position++}]";
            if (item.ValueKind != JsonValueKind.Object
                || item.EnumerateObject().Any(property => property.Name is not ("field" or "operator" or "value"))
                || !item.TryGetProperty("field", out var field) || field.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(field.GetString())
                || !item.TryGetProperty("operator", out var name) || name.ValueKind != JsonValueKind.String
                || !item.TryGetProperty("value", out var value) || value.ValueKind == JsonValueKind.Null)
            {
                errors.Add(new Error(ToolErrorCodes.ArgumentInvalid, $"{at} must be an object with a field, an operator and a value, and nothing else, such as {FieldsForm[1..^1]}."));
                continue;
            }

            var operators = Enum.GetValues<FieldOperator>().Where(candidate => FieldFilters.Name(candidate) == name.GetString()).ToList();
            if (operators.Count != 1)
            {
                errors.Add(new Error(ToolErrorCodes.ArgumentInvalid, $"{at}: '{name.GetString()}' is not an operator; use {OperatorNames}."));
                continue;
            }

            filters.Add(new FieldFilter(field.GetString()!, operators[0], value.Clone()));
        }

        return filters;
    }
}
