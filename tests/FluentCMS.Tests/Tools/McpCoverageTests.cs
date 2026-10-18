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
        Missing("CNT", "Propose, read and list entries", "#2416"),
        Missing("ENT", "Propose, read and list entities and their facts", "#2417"),
        Missing("RUL", "Read and propose rules", "#2419"),
        Missing("CHK", "Read the check results of a change set", "#2420"),
        Missing("DSK", "Read review decisions, read-only", "#2422"),
        Missing("MED", "Upload and describe media", "#2427"),
        Missing("Search", "Search entries and entities", "#2430"),
        Missing("MCP-08", "Bulk operations that return a change set id and report progress", "#2423"),
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
