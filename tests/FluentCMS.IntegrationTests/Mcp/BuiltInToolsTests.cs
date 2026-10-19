using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Agents;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Extensions;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Tools;
using FluentCMS.Host;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace FluentCMS.IntegrationTests.Mcp;

public sealed class BuiltInToolsTests
{
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_agent_reads_content_types_and_the_schema_document_with_the_schema_tools(string provider)
    {
        using var install = new TemporaryInstall();
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        await using var host = await AdminHost.StartAsync(install, settings: database.Configuration);
        var administrator = await host.CreateFirstAdministratorAsync();
        await CreateTypesAsync(host, administrator);
        var issued = await McpAgents.IssueAsync(host, administrator);
        await using var client = await McpAgents.ConnectOverHttpAsync(host, issued.Key);

        var tools = await client.ListToolsAsync();
        Assert.Equal(McpAgents.BuiltInTools, tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
        Assert.All(tools, tool =>
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Description));
            Assert.Equal("object", tool.ProtocolTool.InputSchema.GetProperty("type").GetString());
            Assert.Equal(new[] { "get_", "list_", "search_" }.Any(verb => tool.Name.StartsWith(verb, StringComparison.Ordinal)), tool.ProtocolTool.Annotations?.ReadOnlyHint);
        });

        var list = await CallAsync(client, "list_content_types");
        Assert.Equal(["category", "product"], list["contentTypes"]!.AsArray().Select(type => (string?)type!["slug"]));
        Assert.Equal(2, (int)list["contentTypes"]![1]!["version"]!);

        var current = await CallAsync(client, "get_content_type", new() { ["slug"] = "product" });
        var first = await CallAsync(client, "get_content_type", new() { ["slug"] = "product", ["version"] = 1 });
        Assert.Equal(2, (int)current["version"]!);
        Assert.Equal(["title", "price"], current["fields"]!.AsArray().Select(field => (string?)field!["name"]));
        Assert.Equal("number", (string?)current["fields"]![1]!["type"]);
        Assert.Equal(["title"], first["fields"]!.AsArray().Select(field => (string?)field!["name"]));

        var site = await CallAsync(client, "get_schema_document");
        var product = await CallAsync(client, "get_schema_document", new() { ["slug"] = "product" });
        var documents = await host.AsUserAsync<ISchemaDocuments, (JsonObject Site, JsonObject Product)>(
            administrator,
            async schema => (await schema.GetSiteAsync(), (await schema.GetTypeAsync("product")).Value));
        Assert.True(JsonNode.DeepEquals(documents.Site, site));
        Assert.True(JsonNode.DeepEquals(documents.Product, product));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_agent_reads_a_change_set_and_its_diff_with_the_change_set_tools(string provider)
    {
        using var install = new TemporaryInstall();
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        await using var host = await AdminHost.StartAsync(install, settings: database.Configuration);
        var administrator = await host.CreateFirstAdministratorAsync();
        var product = await CreateTypesAsync(host, administrator);
        var issued = await McpAgents.IssueAsync(host, administrator, McpAgents.Credential(DateTimeOffset.UtcNow) with
        {
            Scope = AgentScope.Create([Site.Default.Id], null, null, [AgentOperation.Read, AgentOperation.Update]).Value,
        });
        var changeSet = await AsAgentAsync(host, issued.Credential, async directory =>
        {
            var opened = (await directory.OpenAsync(new NewChangeSet("Add a price to products.", ["https://example.com/spec"]))).Value;
            var update = ChangeOperation.Create(OperationKind.Update, ItemKind.Schema, product.Id, 1, 2).Value;
            return (await directory.AddOperationAsync(opened.Id, update)).Value;
        });
        var personsSet = await host.AsUserAsync<IChangeSetDirectory, ChangeSet>(administrator, async directory =>
            (await directory.OpenAsync(new NewChangeSet("A person's set.", []))).Value);
        await using var client = await McpAgents.ConnectOverHttpAsync(host, issued.Key);

        var set = await CallAsync(client, "get_change_set", new() { ["id"] = changeSet.Id.ToString() });
        var diff = await CallAsync(client, "get_change_set_diff", new() { ["id"] = changeSet.Id.ToString() });
        var notOwn = await FailAsync(client, "get_change_set", new() { ["id"] = personsSet.Id.ToString() });
        var notOwnDiff = await FailAsync(client, "get_change_set_diff", new() { ["id"] = personsSet.Id.ToString() });

        Assert.Equal(AgentPermissionErrorCodes.NotOwner, Code(notOwn));
        Assert.Equal(AgentPermissionErrorCodes.NotOwner, Code(notOwnDiff));
        Assert.Equal("open", (string?)set["state"]);
        Assert.Equal("Add a price to products.", (string?)set["rationale"]);
        Assert.Equal(("agent", "pipeline-agent"), ((string?)set["initiator"]!["kind"], (string?)set["initiator"]!["name"]));
        var operation = Assert.Single(set["operations"]!.AsArray())!;
        Assert.Equal(("update", "schema", product.Id.ToString(), 1, 2), ((string?)operation["kind"], (string?)operation["itemKind"], (string?)operation["itemId"], (int)operation["baseVersion"]!, (int)operation["proposedVersion"]!));

        Assert.Equal(changeSet.Id.ToString(), (string?)diff["changeSetId"]);
        var field = Assert.Single(Assert.Single(diff["items"]!.AsArray())!["fields"]!.AsArray())!;
        Assert.Equal(("price", "added"), ((string?)field["name"], (string?)field["change"]));
        Assert.Null(field["before"]);
        Assert.Equal("number", (string?)field["after"]!["type"]);
    }

    [Fact]
    public async Task An_unknown_item_or_a_wrong_argument_is_a_tool_error_that_says_what_to_change()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        var administrator = await host.CreateFirstAdministratorAsync();
        await CreateTypesAsync(host, administrator);
        var issued = await McpAgents.IssueAsync(host, administrator);
        await using var client = await McpAgents.ConnectOverHttpAsync(host, issued.Key);

        var unknownType = await FailAsync(client, "get_content_type", new() { ["slug"] = "produkt" });
        var unknownVersion = await FailAsync(client, "get_content_type", new() { ["slug"] = "product", ["version"] = 9 });
        var unknownSet = await FailAsync(client, "get_change_set_diff", new() { ["id"] = Guid.NewGuid().ToString() });
        var wrong = await FailAsync(client, "get_change_set", new() { ["id"] = "42", ["include"] = "operations" });

        Assert.Equal(SchemaErrorCodes.ContentTypeNotFound, Code(unknownType));
        Assert.Equal("Call list_content_types for the slugs of the site's content types.", (string?)unknownType["hint"]);
        Assert.Equal(SchemaErrorCodes.VersionNotFound, Code(unknownVersion));
        Assert.Contains("its current version is 2", (string?)unknownVersion["errors"]![0]!["message"]);
        Assert.Equal(ChangeSetErrorCodes.ChangeSetNotFound, Code(unknownSet));
        Assert.Equal(
            [ToolErrorCodes.ArgumentInvalid, ToolErrorCodes.ArgumentUnknown],
            wrong["errors"]!.AsArray().Select(error => (string?)error!["code"]));
    }

    [Fact]
    public async Task The_schema_resources_serve_the_schema_document_and_refuse_an_unknown_slug()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        var administrator = await host.CreateFirstAdministratorAsync();
        await CreateTypesAsync(host, administrator);
        var issued = await McpAgents.IssueAsync(host, administrator);
        await using var client = await McpAgents.ConnectOverHttpAsync(host, issued.Key);

        var resources = await client.ListResourcesAsync();
        var templates = await client.ListResourceTemplatesAsync();
        var site = await client.ReadResourceAsync("fluentcms://schema");
        var product = await client.ReadResourceAsync("fluentcms://schema/product");
        var unknown = await Assert.ThrowsAnyAsync<McpException>(() => client.ReadResourceAsync("fluentcms://schema/produkt").AsTask());

        Assert.Equal(["fluentcms://schema"], resources.Select(resource => resource.Uri));
        Assert.Equal(["fluentcms://schema/{slug}"], templates.Select(template => template.UriTemplate));
        var siteText = Assert.IsType<TextResourceContents>(Assert.Single(site.Contents));
        Assert.Equal("application/schema+json", siteText.MimeType);
        Assert.NotNull(JsonNode.Parse(siteText.Text)!["$defs"]!["product"]);
        Assert.Equal(
            "urn:fluentcms:content-type:product:2",
            (string?)JsonNode.Parse(Assert.IsType<TextResourceContents>(Assert.Single(product.Contents)).Text)!["$id"]);
        Assert.Contains("There is no content type 'produkt'", unknown.Message);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task The_tools_answer_over_stdio_as_over_http(string provider)
    {
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        var issued = await McpAgents.IssueAsync(database.Configuration);
        await using var stdio = await InProcessStdio.StartAsync(database.Configuration, issued.Key);
        await using var client = await stdio.ConnectAsync();

        var list = await CallAsync(client, "list_content_types");
        var unknown = await FailAsync(client, "get_content_type", new() { ["slug"] = "product" });

        Assert.Contains("whoami", await McpAgents.ToolNamesAsync(client));
        Assert.Empty(list["contentTypes"]!.AsArray());
        Assert.Equal(SchemaErrorCodes.ContentTypeNotFound, Code(unknown));
    }

    [Fact]
    public async Task Two_tools_with_one_name_stop_the_host_at_startup()
    {
        using var install = new TemporaryInstall();
        var app = FluentCmsHost.BuildWebApplication([], builder =>
        {
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration.AddInMemoryCollection(install.Configuration);
            builder.Services.AddFluentCmsExtension<IToolProvider, DuplicateTools>();
        });

        await using (app)
        {
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => app.StartAsync());
            Assert.Contains("Two tools are named 'list_content_types'", failure.ToString());
        }
    }

    // As the agent, outside a tool, for setup a test needs to own: the directories see the credential as they would over MCP.
    private static async Task<T> AsAgentAsync<T>(AdminHost host, AgentCredential credential, Func<IChangeSetDirectory, Task<T>> action)
    {
        var accessor = host.App.Services.GetRequiredService<IHttpContextAccessor>();
        await using var scope = host.App.Services.CreateAsyncScope();
        accessor.HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider, User = AgentPrincipal.For(credential, "test") };
        try
        {
            return await action(scope.ServiceProvider.GetRequiredService<IChangeSetDirectory>());
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    private static async Task<ContentType> CreateTypesAsync(AdminHost host, Guid administrator)
    {
        return await host.AsUserAsync<IContentTypeDirectory, ContentType>(administrator, async directory =>
        {
            var title = new FieldDefinition("title", "text", required: true);
            await directory.CreateAsync(new NewContentType("Category", "category", new ContentTypeDefinition([title])));
            await directory.CreateAsync(new NewContentType("Product", "product", new ContentTypeDefinition([title])));
            return (await directory.ReviseAsync("product", 1, new ContentTypeDefinition([title, new FieldDefinition("price", "number")]))).Value;
        });
    }

    private static async Task<JsonObject> CallAsync(McpClient client, string tool, Dictionary<string, object?>? arguments = null)
    {
        var result = await client.CallToolAsync(tool, arguments);
        Assert.False(result.IsError ?? false, Text(result));
        return Structured(result);
    }

    private static async Task<JsonObject> FailAsync(McpClient client, string tool, Dictionary<string, object?> arguments)
    {
        var result = await client.CallToolAsync(tool, arguments);
        Assert.True(result.IsError, Text(result));
        return Structured(result);
    }

    // The structured content and the text carry the same JSON, for clients that read only one of them.
    private static JsonObject Structured(CallToolResult result)
    {
        var structured = JsonNode.Parse(result.StructuredContent!.Value.GetRawText())!.AsObject();
        Assert.True(JsonNode.DeepEquals(structured, JsonNode.Parse(Text(result))));
        return structured;
    }

    private static string Text(CallToolResult result)
    {
        return Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
    }

    private static string? Code(JsonObject failure)
    {
        return (string?)failure["errors"]![0]!["code"];
    }

    private sealed class DuplicateTools : IToolProvider
    {
        public IReadOnlyList<AgentTool> Tools { get; } =
        [
            new AgentTool(
                "list_content_types",
                "Another list",
                "A second tool with a built-in tool's name, which the host must refuse when it starts.",
                ToolSchemas.Object(),
                readOnly: true,
                (_, _) => Task.FromResult(ToolResult.Success(null))),
        ];
    }
}
