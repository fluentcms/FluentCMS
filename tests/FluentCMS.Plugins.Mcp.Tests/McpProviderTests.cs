using System.Text.Json;
using FluentCMS.Activity;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Sites;
using FluentCMS.Plugins.Sample;
using FluentCMS.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Mcp.Tests.McpTesting;

namespace FluentCMS.Plugins.Mcp.Tests;

// What the providers of a plugin offer through the server, here the sample plugin's: listed and called as the caller,
// inside the operation of the request.
public sealed class McpProviderTests(McpFixture fixture) : IClassFixture<McpFixture>
{
    [Fact]
    public async Task AToolAResourceAndAPromptOfTheSamplePluginAreListedAndCalled()
    {
        using var http = await KeyAsync(fixture.Host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var tools = await client.ListToolsAsync(cancellationToken: Token);
        var resources = await client.ListResourcesAsync(cancellationToken: Token);
        var templates = await client.ListResourceTemplatesAsync(cancellationToken: Token);
        var prompts = await client.ListPromptsAsync(cancellationToken: Token);
        var called = await client.CallToolAsync(SampleTools.WhoAmI, cancellationToken: Token);
        var greeting = await client.ReadResourceAsync(SampleResources.Greeting, cancellationToken: Token);
        var echoed = await client.ReadResourceAsync("sample://echo/hello%20there", cancellationToken: Token);
        var prompt = await client.GetPromptAsync(SamplePrompts.Greet, new Dictionary<string, object?> { ["name"] = "Ada" }, cancellationToken: Token);

        tools.Select(tool => tool.Name).ShouldContain(SampleTools.WhoAmI);
        resources.ShouldHaveSingleItem().Uri.ShouldBe(SampleResources.Greeting);
        templates.ShouldHaveSingleItem().UriTemplate.ShouldBe(SampleResources.EchoTemplate);
        prompts.ShouldHaveSingleItem().Name.ShouldBe(SamplePrompts.Greet);
        prompts[0].ProtocolPrompt.Arguments.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            argument => argument.Name.ShouldBe("name"),
            argument => argument.Required.ShouldBe(true));
        called.IsError.ShouldBe(false);
        greeting.Contents.ShouldHaveSingleItem().ShouldBeOfType<TextResourceContents>().Text.ShouldBe("discovered");
        echoed.Contents.ShouldHaveSingleItem().ShouldBeOfType<TextResourceContents>().Text.ShouldBe("hello there");
        prompt.Messages.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            message => message.Role.ShouldBe(Role.User),
            message => message.Content.ShouldBeOfType<TextContentBlock>().Text.ShouldBe("Greet Ada with: discovered"));
    }

    [Fact]
    public async Task AToolIsListedWithItsSchemaAndItsHintsWrittenOut()
    {
        using var http = await KeyAsync(fixture.Host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var tools = (await client.ListToolsAsync(cancellationToken: Token)).ToDictionary(tool => tool.Name, tool => tool.ProtocolTool);

        var whoAmI = tools[SampleTools.WhoAmI];
        whoAmI.Title.ShouldBe("Who am I");
        whoAmI.Description.ShouldBe("Says who the caller is and where the call runs.");
        whoAmI.OutputSchema.ShouldNotBeNull();
        whoAmI.Annotations.ShouldNotBeNull().ShouldSatisfyAllConditions(
            hints => hints.ReadOnlyHint.ShouldBe(true),
            hints => hints.DestructiveHint.ShouldBe(false),
            hints => hints.IdempotentHint.ShouldBe(true),
            hints => hints.OpenWorldHint.ShouldBe(false));
        var create = tools[SampleTools.EntryCreate];
        create.InputSchema.GetProperty("required").EnumerateArray().Select(name => name.GetString()).ShouldBe(["type", "title"]);
        create.Annotations.ShouldNotBeNull().ShouldSatisfyAllConditions(
            hints => hints.ReadOnlyHint.ShouldBe(false),
            hints => hints.DestructiveHint.ShouldBe(false));

        // A tool that says nothing of itself is taken as one that may destroy.
        new FluentCMS.Agents.McpToolAnnotations().Destructive.ShouldBeTrue();
    }

    // The provider asks the permission checker, so the list of a caller holds what that caller may use.
    [Fact]
    public async Task TheToolListIsTheCallers()
    {
        var (editorHttp, _) = await UserAsync(fixture.Host, BuiltInRoles.EditorName);
        var (adminHttp, _) = await UserAsync(fixture.Host);
        using (editorHttp)
        using (adminHttp)
        {
            await using var editor = await ConnectAsync(editorHttp, protocolVersion: InitializeProtocol);
            await using var admin = await ConnectAsync(adminHttp, protocolVersion: InitializeProtocol);

            var forEditor = (await editor.ListToolsAsync(cancellationToken: Token)).Select(tool => tool.Name).ToList();
            var forAdmin = (await admin.ListToolsAsync(cancellationToken: Token)).Select(tool => tool.Name).ToList();
            var refused = await editor.CallToolAsync(SampleTools.NoteAdd, new Dictionary<string, object?> { ["text"] = "not mine" }, cancellationToken: Token);

            forEditor.ShouldContain(SampleTools.EntryCreate);
            forEditor.ShouldNotContain(SampleTools.NoteAdd);
            forAdmin.ShouldContain(SampleTools.NoteAdd);

            // A tool that is not listed for the caller is still the service's to refuse.
            refused.IsError.ShouldBe(true);
            Code(refused).ShouldBe("FCMS3103");
        }
    }

    [Fact]
    public async Task ACallRunsInAnOperationWithTheMcpOriginTheClientAndThePrincipal()
    {
        var (http, user) = await UserAsync(fixture.Host);
        using (http)
        {
            await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

            var result = await client.CallToolAsync(SampleTools.WhoAmI, cancellationToken: Token);

            var answer = result.StructuredContent.ShouldNotBeNull();
            answer.GetProperty("origin").GetString().ShouldBe(nameof(Origin.Mcp));
            answer.GetProperty("clientId").GetString().ShouldBe(FluentCmsTestHost.OAuthClientId);
            answer.GetProperty("principal").GetGuid().ShouldBe(user.Id);
            answer.GetProperty("kind").GetString().ShouldBe(nameof(PrincipalKind.AdminUser));
            answer.GetProperty("site").GetGuid().ShouldBe(DefaultSite.Id);
            answer.GetProperty("locale").GetString().ShouldBe(DefaultSite.Locale);
            JsonDocument.Parse(Text(result)).RootElement.GetProperty("origin").GetString().ShouldBe(nameof(Origin.Mcp));
        }
    }

    [Fact]
    public async Task AWriteThroughAToolRecordsTheMcpOriginTheClientAndThePrincipalOnItsVersionAndActivityRow()
    {
        var host = fixture.Host;
        var (http, user) = await UserAsync(host, BuiltInRoles.EditorName);
        using (http)
        {
            await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
            var title = "By agent " + Guid.NewGuid().ToString("N")[..6];

            var result = await client.CallToolAsync(SampleTools.EntryCreate, new Dictionary<string, object?> { ["type"] = "article", ["title"] = title }, cancellationToken: Token);

            result.IsError.ShouldBe(false, Text(result));
            var id = result.StructuredContent.ShouldNotBeNull().GetProperty("id").GetGuid();
            var admin = host.As(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token));
            var version = (await admin.Content<Article>().GetVersionsAsync(id, cancellationToken: Token)).ShouldHaveSingleItem();
            version.Origin.ShouldBe(Origin.Mcp);
            version.ClientId.ShouldBe(FluentCmsTestHost.OAuthClientId);
            version.AuthorId.ShouldBe(user.Id);

            await DrainOutboxAsync(host);
            var rows = await admin.RunAsync<IActivityQuery, PagedResult<ActivityItem>>(query => query.QueryAsync(new ActivityFilter { TargetType = "article", TargetId = id.ToString() }, null, Token));
            var row = rows.Items.ShouldHaveSingleItem();
            row.Origin.ShouldBe(Origin.Mcp);
            row.ClientId.ShouldBe(FluentCmsTestHost.OAuthClientId);
            row.ActorId.ShouldBe(user.Id);
            row.ActorKind.ShouldBe(PrincipalKind.AdminUser);
        }
    }

    // A key is its own client, as on the REST APIs.
    [Fact]
    public async Task AWriteByAKeyRecordsTheKeyAsItsClient()
    {
        var host = fixture.Host;
        var key = await host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.EditorName, cancellationToken: Token);
        using var http = host.AsKey(key);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var result = await client.CallToolAsync(SampleTools.EntryCreate, new Dictionary<string, object?> { ["type"] = "article", ["title"] = "By key" }, cancellationToken: Token);

        result.IsError.ShouldBe(false, Text(result));
        var id = result.StructuredContent.ShouldNotBeNull().GetProperty("id").GetGuid();
        var version = (await host.As(key).Content<Article>().GetVersionsAsync(id, cancellationToken: Token)).ShouldHaveSingleItem();
        version.Origin.ShouldBe(Origin.Mcp);
        version.ClientId.ShouldBe(key.Id.ToString());
        version.AuthorId.ShouldBe(key.Id);
    }

    // What a service refuses is answered as a result the model can read, with the code the REST APIs give the refusal.
    [Fact]
    public async Task ARefusalOfAServiceIsAnErrorResultWithTheCodeOfTheRestApis()
    {
        using var http = await KeyAsync(fixture.Host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var tooLong = await client.CallToolAsync(SampleTools.EntryCreate, new Dictionary<string, object?> { ["type"] = "article", ["title"] = new string('x', 40) }, cancellationToken: Token);
        var noType = await client.CallToolAsync(SampleTools.EntryCreate, new Dictionary<string, object?> { ["type"] = "nothing", ["title"] = "x" }, cancellationToken: Token);
        var byTool = await client.CallToolAsync(SampleTools.NoteAdd, new Dictionary<string, object?> { ["text"] = "" }, cancellationToken: Token);

        tooLong.IsError.ShouldBe(true);
        var code = Code(tooLong).ShouldNotBeNull();
        code.ShouldStartWith("FCMS21");
        Text(tooLong).ShouldStartWith(code + ": ");
        Text(tooLong).ShouldContain("- title: ");
        tooLong.StructuredContent.ShouldBeNull();
        noType.IsError.ShouldBe(true);
        Code(noType).ShouldNotBeNull();
        byTool.IsError.ShouldBe(true);
        Code(byTool).ShouldBe(SamplePlugin.NoteExists);
        Text(byTool).ShouldBe($"{SamplePlugin.NoteExists}: A note needs a text.");
    }

    [Fact]
    public async Task ARefusalOfAPromptIsAProtocolErrorWithItsCode()
    {
        using var http = await KeyAsync(fixture.Host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var refused = await Should.ThrowAsync<ModelContextProtocol.McpProtocolException>(async () => await client.GetPromptAsync(SamplePrompts.Greet, cancellationToken: Token));

        refused.ErrorCode.ShouldBe(ModelContextProtocol.McpErrorCode.InvalidParams);
        refused.Message.ShouldContain($"{SamplePlugin.NoteExists}: The prompt needs a name.");
    }

    [Fact]
    public async Task AToolOfThePluginWritesItsOwnTable()
    {
        var host = fixture.Host;
        using var http = await KeyAsync(host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        var text = "note " + Guid.NewGuid().ToString("N");

        var result = await client.CallToolAsync(SampleTools.NoteAdd, new Dictionary<string, object?> { ["text"] = text }, cancellationToken: Token);

        result.IsError.ShouldBe(false, Text(result));
        await using var scope = host.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<PluginDbContext<SamplePlugin>>().Set<SampleNote>().AnyAsync(note => note.Text == text, Token)).ShouldBeTrue();
    }
}
