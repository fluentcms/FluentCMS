using System.Text.Json.Nodes;
using FluentCMS.Core.Agents;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Tools;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace FluentCMS.IntegrationTests.Mcp;

public sealed class WriteToolsTests
{
    private static readonly JsonObject Title = Definition(("title", "text"));

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_agent_proposes_a_new_type_and_a_revision_in_one_set_and_submits_it(string provider)
    {
        await using var agent = await AgentAsync(provider);
        var product = await agent.CreateAsPersonAsync("product");

        var opened = await agent.CallAsync("open_change_set", new() { ["rationale"] = "Add categories and prices.", ["sources"] = new[] { "https://example.com/spec" } });
        var id = (string)opened["changeSet"]!["id"]!;
        var created = await agent.CallAsync("create_content_type", new()
        {
            ["name"] = "Category",
            ["slug"] = "category",
            ["definition"] = Title,
            ["change_set_id"] = id,
        });
        var revised = await agent.CallAsync("revise_content_type", new()
        {
            ["slug"] = "product",
            ["base_version"] = 1,
            ["definition"] = Definition(("title", "text"), ("price", "number")),
            ["change_set_id"] = id,
        });
        var submitted = await agent.CallAsync("submit_change_set", new() { ["id"] = id });

        Assert.Equal(1, (int)created["contentType"]!["version"]!);
        Assert.Equal(2, (int)revised["contentType"]!["version"]!);
        Assert.Contains("submit_change_set", (string?)revised["next"]);
        Assert.Equal("checks_running", (string?)submitted["changeSet"]!["state"]);

        var stored = await agent.AsPersonAsync<IChangeSetDirectory, ChangeSet>(directory => directory.GetAsync(Guid.Parse(id)));
        var category = await agent.AsPersonAsync<IContentTypeDirectory, ContentType>(directory => directory.GetAsync("category"));
        Assert.Equal(ChangeSetState.ChecksRunning, stored.State);
        Assert.Equal(agent.Credential.Initiator, stored.Initiator);
        Assert.Equal(
            [
                ChangeOperation.Create(OperationKind.Create, ItemKind.Schema, category.Id, null, 1).Value,
                ChangeOperation.Create(OperationKind.Update, ItemKind.Schema, product.Id, 1, 2).Value,
            ],
            stored.Operations);

        var calls = (await agent.QueryAsync(context => context.ToolCalls.AsNoTracking().ToListAsync())).OrderBy(call => call.Timestamp).ToList();
        Assert.Equal(["open_change_set", "create_content_type", "revise_content_type", "submit_change_set"], calls.Select(call => call.Tool));
        Assert.All(calls, call => Assert.Equal(ToolCallOutcome.Succeeded, call.Outcome));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Writes_with_one_session_id_land_in_one_set_opened_with_the_first_rationale(string provider)
    {
        await using var agent = await AgentAsync(provider);

        var first = await agent.CallAsync("create_content_type", new()
        {
            ["name"] = "Category",
            ["slug"] = "category",
            ["definition"] = Title,
            ["session_id"] = "catalog-sync",
            ["rationale"] = "Sync the catalog.",
            ["sources"] = new[] { "exports/catalog.csv" },
        });
        var second = await agent.CallAsync("create_content_type", new()
        {
            ["name"] = "Region",
            ["slug"] = "region",
            ["definition"] = Title,
            ["session_id"] = "catalog-sync",
            ["rationale"] = "Ignored once the session's set is open.",
        });

        Assert.Equal((string?)first["changeSet"]!["id"], (string?)second["changeSet"]!["id"]);
        Assert.Equal("Sync the catalog.", (string?)second["changeSet"]!["rationale"]);
        Assert.Equal(2, second["changeSet"]!["operations"]!.AsArray().Count);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_refused_write_stores_no_version_and_a_refused_cap_keeps_its_event(string provider)
    {
        await using var agent = await AgentAsync(provider, itemsPerChangeSet: 1);
        var personsSet = await agent.AsPersonAsync<IChangeSetDirectory, ChangeSet>(directory => directory.OpenAsync(new NewChangeSet("A person's own set.", [])));
        var id = (string)(await agent.CallAsync("open_change_set", new() { ["rationale"] = "One type at most." }))["changeSet"]!["id"]!;
        var invalid = await agent.FailAsync("create_content_type", new()
        {
            ["name"] = "Region",
            ["slug"] = "Region Type",
            ["definition"] = Definition(("title", "no-such-type"), ("Bad Name", "text")),
            ["change_set_id"] = id,
        });
        await agent.CallAsync("create_content_type", new() { ["name"] = "Category", ["slug"] = "category", ["definition"] = Title, ["change_set_id"] = id });

        var overCap = await agent.FailAsync("create_content_type", new() { ["name"] = "Region", ["slug"] = "region", ["definition"] = Title, ["change_set_id"] = id });
        var notOwn = await agent.FailAsync("create_content_type", new()
        {
            ["name"] = "Region",
            ["slug"] = "region",
            ["definition"] = Title,
            ["change_set_id"] = personsSet.Id.ToString(),
        });
        var noTarget = await agent.FailAsync("create_content_type", new() { ["name"] = "Region", ["slug"] = "region", ["definition"] = Title });
        var submitPersons = await agent.FailAsync("submit_change_set", new() { ["id"] = personsSet.Id.ToString() });

        Assert.True(invalid["errors"]!.AsArray().Count >= 3, invalid.ToJsonString());
        Assert.Equal(AgentPermissionErrorCodes.CapExceeded, Code(overCap));
        Assert.Equal(AgentPermissionErrorCodes.NotOwner, Code(notOwn));
        Assert.Equal(ToolErrorCodes.ArgumentMissing, Code(noTarget));
        Assert.Equal(AgentPermissionErrorCodes.NotOwner, Code(submitPersons));

        Assert.Equal(["category"], await agent.QueryAsync(context => context.ContentTypes.AsNoTracking().Select(row => row.Slug).ToListAsync()));
        Assert.Single((await agent.AsPersonAsync<IChangeSetDirectory, ChangeSet>(directory => directory.GetAsync(Guid.Parse(id)))).Operations);
        Assert.Empty((await agent.AsPersonAsync<IChangeSetDirectory, ChangeSet>(directory => directory.GetAsync(personsSet.Id))).Operations);
        Assert.Contains(
            await agent.QueryAsync(context => context.AuditEvents.AsNoTracking().Select(row => row.Operation).ToListAsync()),
            operation => operation == "agent.items_per_change_set_exceeded");
    }

    [Fact]
    public async Task A_revision_from_a_stale_version_is_refused_with_the_way_to_the_current_one()
    {
        await using var agent = await AgentAsync(DatabaseProviders.Sqlite);
        await agent.CreateAsPersonAsync("product");
        await agent.AsPersonAsync<IContentTypeDirectory, ContentType>(directory => directory.ReviseAsync(
            "product",
            1,
            new ContentTypeDefinition([new FieldDefinition("title", "text"), new FieldDefinition("summary", "text")]), TestChangeSetTargets.ContentModel));

        var stale = await agent.FailAsync("revise_content_type", new()
        {
            ["slug"] = "product",
            ["base_version"] = 1,
            ["definition"] = Title,
            ["session_id"] = "product-work",
            ["rationale"] = "Drop the summary.",
        });

        Assert.Equal(SchemaErrorCodes.VersionConflict, Code(stale));
        Assert.Contains("get_content_type", (string?)stale["hint"]);
        Assert.Equal(2, (await agent.AsPersonAsync<IContentTypeDirectory, ContentType>(directory => directory.GetAsync("product"))).Version);
    }

    private static JsonObject Definition(params (string Name, string Type)[] fields)
    {
        return new JsonObject
        {
            ["fields"] = new JsonArray([.. fields.Select(field => new JsonObject { ["name"] = field.Name, ["type"] = field.Type })]),
        };
    }

    private static string? Code(JsonObject failure)
    {
        return (string?)failure["errors"]![0]!["code"];
    }

    private static async Task<ConnectedAgent> AgentAsync(string provider, int itemsPerChangeSet = 200)
    {
        var install = new TemporaryInstall();
        var database = await TemporaryDatabase.CreateAsync(provider);
        var host = await AdminHost.StartAsync(install, settings: database.Configuration);
        var administrator = await host.CreateFirstAdministratorAsync();
        var issued = await McpAgents.IssueAsync(host, administrator, new NewAgentCredential(
            "schema-agent",
            DateTimeOffset.UtcNow.AddDays(30),
            AgentScope.Create([Site.Default.Id], null, null, [AgentOperation.Read, AgentOperation.Create, AgentOperation.Update]).Value,
            AgentCaps.Create(600, TimeSpan.FromHours(1), 5m, TimeSpan.FromDays(1), itemsPerChangeSet, 20).Value));
        var client = await McpAgents.ConnectOverHttpAsync(host, issued.Key);
        return new ConnectedAgent(install, database, host, administrator, issued.Credential, client);
    }

    private sealed class ConnectedAgent(
        TemporaryInstall install,
        TemporaryDatabase database,
        AdminHost host,
        Guid administrator,
        AgentCredential credential,
        McpClient client) : IAsyncDisposable
    {
        public AgentCredential Credential { get; } = credential;

        public async Task<JsonObject> CallAsync(string tool, Dictionary<string, object?> arguments)
        {
            var result = await client.CallToolAsync(tool, arguments);
            Assert.False(result.IsError ?? false, Text(result));
            return JsonNode.Parse(Text(result))!.AsObject();
        }

        public async Task<JsonObject> FailAsync(string tool, Dictionary<string, object?> arguments)
        {
            var result = await client.CallToolAsync(tool, arguments);
            Assert.True(result.IsError, Text(result));
            return JsonNode.Parse(Text(result))!.AsObject();
        }

        public async Task<T> AsPersonAsync<TService, T>(Func<TService, Task<Core.Results.Result<T>>> action)
            where TService : notnull
        {
            return (await host.AsUserAsync<TService, Core.Results.Result<T>>(administrator, action)).Value;
        }

        public Task<ContentType> CreateAsPersonAsync(string slug)
        {
            return AsPersonAsync<IContentTypeDirectory, ContentType>(directory =>
                directory.CreateAsync(new NewContentType(slug, slug, new ContentTypeDefinition([new FieldDefinition("title", "text")])), TestChangeSetTargets.ContentModel));
        }

        public async Task<T> QueryAsync<T>(Func<FluentCmsDbContext, Task<T>> query)
        {
            await using var scope = host.App.Services.CreateAsyncScope();
            return await query(scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>());
        }

        public async ValueTask DisposeAsync()
        {
            await client.DisposeAsync();
            await host.DisposeAsync();
            await database.DisposeAsync();
            install.Dispose();
        }

        private static string Text(CallToolResult result)
        {
            return Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        }
    }
}
