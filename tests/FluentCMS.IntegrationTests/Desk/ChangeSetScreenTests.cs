using System.Net;
using System.Text.RegularExpressions;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Schema;
using FluentCMS.Infrastructure.Persistence;

namespace FluentCMS.IntegrationTests.Desk;

public sealed class ChangeSetScreenTests
{
    [Fact]
    public async Task An_anonymous_request_for_a_set_is_sent_to_sign_in()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        await host.CreateFirstAdministratorAsync();
        var id = Guid.NewGuid();

        var response = await host.Client.GetAsync($"/sets/{id}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/sign-in?ReturnUrl=%2Fsets%2F", host.LocationOf(response));
    }

    [Fact]
    public async Task An_unknown_set_is_not_found()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        await host.CreateFirstAdministratorAsync();
        await host.SignInAsync(AdminHost.Owner);

        var response = await host.Client.GetAsync($"/sets/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("There is nothing at this address.", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_set_shows_its_rationale_sources_history_and_first_items_and_the_desk_links_to_it(string provider)
    {
        using var install = new TemporaryInstall();
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        await using var host = await AdminHost.StartAsync(install, settings: database.Configuration);
        var ownerId = await host.CreateFirstAdministratorAsync();
        var types = new List<ContentType>();
        for (var index = 0; index < 30; index++)
        {
            types.Add(await CreateTypeAsync(host, ownerId, $"type-{index}"));
        }

        var set = await host.AsUserAsync<IChangeSetDirectory, ChangeSet>(ownerId, async directory =>
        {
            var opened = (await directory.OpenAsync(new NewChangeSet(
                "Add thirty types.",
                ["https://example.com/spec", "javascript:alert(1)", "specs/types.md", "https://\u0430pple.com/prices"]))).Value;
            foreach (var type in types)
            {
                Assert.True((await directory.AddOperationAsync(opened.Id, Create(type))).IsSuccess);
            }

            await directory.TransitionAsync(opened.Id, ChangeSetState.ChecksRunning);
            return (await directory.TransitionAsync(opened.Id, ChangeSetState.AwaitingReview)).Value;
        });
        await host.SignInAsync(AdminHost.Owner);

        Assert.Contains($"href=\"sets/{set.Id}?queue=awaiting-me\"", await host.Client.GetStringAsync("/"));
        var page = await host.Client.GetStringAsync($"/sets/{set.Id}");

        Assert.Contains("<h1 class=\"text-2xl font-semibold tracking-tight whitespace-pre-line\">Add thirty types.</h1>", page);
        Assert.Contains("data-state=\"awaiting-review\"", page);
        Assert.Contains("href=\"https://example.com/spec\" target=\"_blank\" rel=\"noopener noreferrer\"", page);
        Assert.DoesNotContain("href=\"javascript:", page);
        Assert.Contains("specs/types.md", page);
        Assert.Contains("xn--pple-43d.com", page[page.IndexOf("data-testid=\"source-host\"", StringComparison.Ordinal)..]);
        Assert.Single(Regex.Matches(page, "data-testid=\"source-host\""));
        Assert.Equal(
            ["change_set.opened", "change_set.submitted", "change_set.awaiting_review"],
            Regex.Matches(page[page.IndexOf("data-testid=\"change-set-history\"", StringComparison.Ordinal)..], "data-operation=\"([a-z_.]+)\"").Select(match => match.Groups[1].Value));

        // The island arrives with the page holding the first batch; the circuit adds the rest.
        Assert.Equal(25, Regex.Count(page, "data-slot=\"diff-view-item\""));
        Assert.Contains("aria-busy=\"true\"", page);
        Assert.Contains("<!--Blazor:", page);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_rejected_revert_set_shows_its_reason_and_links_to_the_set_it_reverts(string provider)
    {
        using var install = new TemporaryInstall();
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        await using var host = await AdminHost.StartAsync(install, settings: database.Configuration);
        var ownerId = await host.CreateFirstAdministratorAsync();
        var type = await CreateTypeAsync(host, ownerId, "product");
        var published = await host.AsUserAsync<IChangeSetDirectory, ChangeSet>(ownerId, async directory =>
        {
            var opened = (await directory.OpenAsync(new NewChangeSet("Add products.", []))).Value;
            await directory.AddOperationAsync(opened.Id, Create(type));
            await directory.TransitionAsync(opened.Id, ChangeSetState.ChecksRunning);
            await directory.TransitionAsync(opened.Id, ChangeSetState.Approved);
            return (await directory.PublishAsync(opened.Id)).Value;
        });
        var revert = await host.AsUserAsync<IChangeSetDirectory, ChangeSet>(ownerId, async directory =>
        {
            var opened = (await directory.RevertAsync(published.Id, "Products were added too early.")).Value;
            await directory.TransitionAsync(opened.Id, ChangeSetState.ChecksRunning);
            await directory.TransitionAsync(opened.Id, ChangeSetState.AwaitingReview);
            return (await directory.RejectAsync(opened.Id, "Keep the products; the launch moved up.")).Value;
        });
        await host.SignInAsync(AdminHost.Owner);

        var page = await host.Client.GetStringAsync($"/sets/{revert.Id}");

        Assert.Contains("data-state=\"rejected\"", page);
        Assert.Contains("Keep the products; the launch moved up.", page);
        Assert.Contains($"href=\"sets/{published.Id}\"", page);
    }

    // A set an agent opened reads like any other, with the agent named as its opener, and the view renders right to left.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_set_an_agent_opened_shows_its_opener_and_renders_right_to_left(string provider)
    {
        using var install = new TemporaryInstall();
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        await using var host = await AdminHost.StartAsync(install, settings: database.Configuration);
        var ownerId = await host.CreateFirstAdministratorAsync();
        var issued = await Mcp.McpAgents.IssueAsync(host, ownerId, Mcp.McpAgents.Credential(DateTimeOffset.UtcNow, "catalog-agent") with
        {
            Scope = FluentCMS.Core.Agents.AgentScope.Create(
                [FluentCMS.Core.Sites.Site.Default.Id], null, null, [FluentCMS.Core.Agents.AgentOperation.Read, FluentCMS.Core.Agents.AgentOperation.Create]).Value,
        });
        Guid setId;
        await using (var client = await Mcp.McpAgents.ConnectOverHttpAsync(host, issued.Key))
        {
            var opened = await client.CallToolAsync("open_change_set", new Dictionary<string, object?> { ["rationale"] = "Add the brand type." });
            setId = Guid.Parse((string)System.Text.Json.Nodes.JsonNode.Parse(((ModelContextProtocol.Protocol.TextContentBlock)opened.Content.Single()).Text)!["changeSet"]!["id"]!);
            await client.CallToolAsync("create_content_type", new Dictionary<string, object?>
            {
                ["name"] = "brand",
                ["slug"] = "brand",
                ["definition"] = new System.Text.Json.Nodes.JsonObject { ["fields"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["name"] = "title", ["type"] = "text" }) },
                ["change_set_id"] = setId.ToString(),
            });
            await client.CallToolAsync("submit_change_set", new Dictionary<string, object?> { ["id"] = setId.ToString() });
        }

        await host.SignInAsync(AdminHost.Owner);
        var page = await host.Client.GetStringAsync($"/sets/{setId}?dir=rtl");

        Assert.Contains("<html lang=\"en\" dir=\"rtl\">", page);
        Assert.Contains("catalog-agent", page);
        Assert.Contains("data-state=\"awaiting-review\"", page);
        Assert.Single(Regex.Matches(page, "data-slot=\"diff-view-item\""));
    }

    private static Task<ContentType> CreateTypeAsync(AdminHost host, Guid userId, string slug)
    {
        return host.AsUserAsync<IContentTypeDirectory, ContentType>(userId, async directory =>
            (await directory.CreateAsync(new NewContentType(slug, slug, new ContentTypeDefinition([new FieldDefinition("title", "text")])), TestChangeSetTargets.ContentModel)).Value);
    }

    private static ChangeOperation Create(ContentType type)
    {
        return ChangeOperation.Create(OperationKind.Create, ItemKind.Schema, type.Id, null, 1).Value;
    }
}
