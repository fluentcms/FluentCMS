using FluentCMS.Core;
using FluentCMS.Core.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Tests.Tools;

// MCP-01: every capability is a tool before it is a screen (P1). Each row names the tools that cover one capability, or the
// epic that adds them while it has not landed. A new tool adds its row here in the same pull request.
public sealed class McpCoverageTests
{
    private static readonly Capability[] Capabilities =
    [
        new("SCH", "List content types", ["list_content_types"]),
        new("SCH", "Read a content type at any version", ["get_content_type"]),
        new("SCH", "Read the schema document entries are written from", ["get_schema_document"]),
        new("SCH", "Propose a new content type", ["create_content_type"]),
        new("SCH", "Propose a revision of a content type", ["revise_content_type"]),
        new("CHG", "Open a change set", ["open_change_set"]),
        new("CHG", "Read a change set", ["get_change_set"]),
        new("CHG", "Read a change set's diff", ["get_change_set_diff"]),
        new("CHG", "Submit a change set for checks and review", ["submit_change_set"]),
        new("CHG", "Reopen a change set after failed checks", ["reopen_change_set"]),
        new("CNT", "List entries of a content type", ["list_entries"]),
        new("CNT", "Read an entry at any version, and its history", ["get_entry", "get_entry_history"]),
        new("CNT", "Read an entry with its facts resolved", ["get_resolved_entry"]),
        new("CNT", "Find where an entry, entity field or media asset is used", ["get_where_used"]),
        new("CNT", "Propose a new entry, an update or unpublishing", ["create_entry", "update_entry", "delete_entry"]),
        new("ENT", "Read an entity's facts with their sources and flags", ["get_entity_facts"]),
        new("ENT", "Read an entity's relations in both directions", ["get_entity_relations"]),
        new("ENT", "List unsourced facts", ["list_unsourced_facts"]),
        new("ENT", "Propose a fact with its source, or a relation", ["set_entity_fact", "relate_entities"]),
        new("ENT", "Read the pages a change set's entity changes affect", ["get_blast_radius"]),
        new("ENT", "List stale facts", ["list_stale_facts"]),
        Missing("RUL", "Read and propose rules", "#2419"),
        new("CHK", "Run the checks on a draft before proposing it", ["check_draft"]),
        new("CHK", "Read the check results of a change set", ["get_change_set_checks"]),
        new("DSK", "Read the review desk's queues and counts, read-only", ["get_desk_counts", "list_desk_queue"]),
        new("DSK", "Read a change set's review status and decisions, read-only", ["get_change_set_review"]),
        new("MED", "Upload and describe media", ["upload_media", "update_media"]),
        new("MED", "Read and list media", ["get_media", "list_media"]),
        new("MED", "Attach media to a field", ["attach_media"]),
        new("Search", "Search entries and entities by their words", ["search_entries"]),
        new("MCP-08", "Bulk updates of entries and entities that return a change set id at once", ["bulk_update_entries"]),
        new("MCP-08", "Read the progress of background work", ["get_job"]),
    ];

    // A person approves, rejects, publishes and reverts; no tool may (MCP-06, SEC-02). Tool names start with their verb.
    private static readonly string[] ReviewVerbs = ["approve_", "reject_", "publish_", "revert_"];

    [Fact]
    public void Every_built_capability_has_its_tools_registered()
    {
        var registered = RegisteredTools();

        var missing = Capabilities
            .SelectMany(capability => capability.Tools.Where(tool => !registered.Contains(tool)).Select(tool => $"{capability.Group}: {capability.Name} needs '{tool}'"))
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void Every_registered_tool_belongs_to_a_listed_capability()
    {
        var listed = Capabilities.SelectMany(capability => capability.Tools).ToHashSet(StringComparer.Ordinal);

        var unlisted = RegisteredTools().Where(tool => !listed.Contains(tool)).Select(tool => $"'{tool}' is in no row of McpCoverageTests").ToList();

        Assert.Empty(unlisted);
    }

    [Fact]
    public void A_capability_still_to_come_names_the_epic_that_adds_it()
    {
        Assert.All(
            Capabilities.Where(capability => capability.Tools.Count == 0),
            capability => Assert.StartsWith("#", capability.Epic));
    }

    [Fact]
    public void No_tool_approves_rejects_or_publishes()
    {
        var review = RegisteredTools().Where(tool => ReviewVerbs.Any(verb => tool.StartsWith(verb, StringComparison.Ordinal))).ToList();

        Assert.Empty(review);
    }

    // Resolved the way the host resolves them, so built-ins discovered in Core count and nothing else does.
    private static HashSet<string> RegisteredTools()
    {
        using var services = new ServiceCollection().AddFluentCmsCore().BuildServiceProvider();

        return services.GetServices<IToolProvider>().SelectMany(provider => provider.Tools).Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);
    }

    private static Capability Missing(string group, string name, string epic)
    {
        return new Capability(group, name, [], epic);
    }

    private sealed record Capability(string Group, string Name, IReadOnlyList<string> Tools, string? Epic = null);
}
