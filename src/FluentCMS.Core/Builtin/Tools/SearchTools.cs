using System.Text.Json.Nodes;
using FluentCMS.Core.Content;
using FluentCMS.Core.Results;
using FluentCMS.Core.Search;
using FluentCMS.Core.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Builtin.Tools;

// Finding entries and entities by their words (SRC-01, MCP-01), within the types the agent may read.
public sealed class SearchTools : IToolProvider
{
    private static readonly string StateNames = string.Join(", ", Enum.GetValues<EntryState>().Select(state => ToolJson.Name(state)));

    public IReadOnlyList<AgentTool> Tools { get; } =
    [
        new AgentTool(
            "search_entries",
            "Search entries and entities",
            "Finds entries and entities whose latest version has every word of the query, each matched as a prefix, best first: "
            + "'4411' finds a SKU and '441' does too. An entry that references a match is found as well, with the match in 'via', "
            + "such as the page that shows a matching entity. Narrow the search with types, states or published_only; read a hit "
            + "with get_entry, or an entity's facts with get_entity_facts.",
            ToolSchemas.Object(
                ToolSchemas.String("query", "The words to find, such as a name, a SKU or part of a sentence.", "4411"),
                ToolSchemas.Strings("types", "Content type slugs to search in; leave out for every type you may read.", "product", required: false),
                ToolSchemas.Strings("states", $"Entry states to keep: {StateNames}.", "published", required: false),
                ToolSchemas.Flag("published_only", "Only entries that have a published version."),
                ToolSchemas.PositiveInteger("limit", $"At most this many hits, up to {SearchQuery.MaxLimit}; {SearchQuery.DefaultLimit} if left out.", 20, required: false)),
            readOnly: true,
            SearchAsync),
    ];

    private static async Task<ToolResult> SearchAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        var text = arguments.RequiredString("query", "4411");
        var types = arguments.OptionalStrings("types", "product");
        var states = arguments.OptionalStrings("states", "published");
        var publishedOnly = arguments.Flag("published_only");
        var limit = arguments.OptionalPositiveInteger("limit") ?? SearchQuery.DefaultLimit;
        var errors = new List<Error>(arguments.Errors());

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
            new SearchQuery(text!)
            {
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
}
