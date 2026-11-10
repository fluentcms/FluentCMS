using System.Net;
using System.Text.Json.Nodes;
using FluentCMS.Core.Authorization;
using FluentCMS.Plugins.Sample;
using FluentCMS.Sites;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Mcp.Tests.McpSiteTesting;
using static FluentCMS.Plugins.Mcp.Tests.McpTesting;

namespace FluentCMS.Plugins.Mcp.Tests;

// The prompts an editor stores for the agents of a site (#2980), served beside the prompts of the plugins' providers.
public sealed class McpStoredPromptTests(McpSiteFixture fixture) : IClassFixture<McpSiteFixture>
{
    private static readonly object Summarize = new
    {
        name = "summarize",
        title = "Summarize an entry",
        description = "Sums an entry up for a reader.",
        arguments = new object[]
        {
            new { name = "entry", description = "The id of the entry.", required = true },
            new { name = "tone" },
        },
        template = "Summarize the entry {{entry}} in a {{tone}} tone. Keep {{entry}} linked.",
    };

    [Fact]
    public async Task AStoredPromptAndAPromptOfTheSamplePluginAreBothListedAndTheStoredOneIsFilled()
    {
        var host = fixture.Host;
        var site = await CreateSiteAsync(host);
        using var http = await KeyInAsync(host, site);
        using var elsewhere = await KeyInAsync(host, await CreateSiteAsync(host));
        await SendAsync(http, HttpMethod.Post, Prompts, Summarize, HttpStatusCode.Created);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        await using var inOther = await ConnectAsync(elsewhere, protocolVersion: InitializeProtocol);

        var prompts = (await client.ListPromptsAsync(cancellationToken: Token)).ToDictionary(prompt => prompt.Name, prompt => prompt.ProtocolPrompt);
        var filled = await client.GetPromptAsync("summarize", new Dictionary<string, object?> { ["entry"] = "42", ["tone"] = "dry {{entry}}", ["unknown"] = "ignored" }, cancellationToken: Token);
        var withoutOptional = await client.GetPromptAsync("summarize", new Dictionary<string, object?> { ["entry"] = "42" }, cancellationToken: Token);
        var ofProvider = await client.GetPromptAsync(SamplePrompts.Greet, new Dictionary<string, object?> { ["name"] = "Ada" }, cancellationToken: Token);

        prompts.Keys.ShouldBe([SamplePrompts.Greet, "summarize"], ignoreOrder: true);
        var stored = prompts["summarize"];
        stored.Title.ShouldBe("Summarize an entry");
        stored.Description.ShouldBe("Sums an entry up for a reader.");
        stored.Arguments.ShouldNotBeNull().Select(argument => (argument.Name, argument.Description, argument.Required)).ShouldBe([("entry", "The id of the entry.", true), ("tone", null, false)]);
        filled.Description.ShouldBe("Sums an entry up for a reader.");

        // A value is put in as it is: what looks like an argument inside it is not filled again.
        filled.Messages.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            message => message.Role.ShouldBe(Role.User),
            message => message.Content.ShouldBeOfType<TextContentBlock>().Text.ShouldBe("Summarize the entry 42 in a dry {{entry}} tone. Keep 42 linked."));
        withoutOptional.Messages.ShouldHaveSingleItem().Content.ShouldBeOfType<TextContentBlock>().Text.ShouldBe("Summarize the entry 42 in a  tone. Keep 42 linked.");
        ofProvider.Messages.ShouldHaveSingleItem().Content.ShouldBeOfType<TextContentBlock>().Text.ShouldBe("Greet Ada with: discovered");

        // A stored prompt is its site's.
        (await inOther.ListPromptsAsync(cancellationToken: Token)).Select(prompt => prompt.Name).ShouldBe([SamplePrompts.Greet]);
        (await Should.ThrowAsync<McpProtocolException>(async () => await inOther.GetPromptAsync("summarize", cancellationToken: Token))).Message.ShouldContain(McpErrorCodes.UnknownPrompt);
    }

    [Fact]
    public async Task AMissingRequiredArgumentIsRefused()
    {
        var host = fixture.Host;
        using var http = await KeyInAsync(host, await CreateSiteAsync(host));
        await SendAsync(http, HttpMethod.Post, Prompts, Summarize, HttpStatusCode.Created);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var missing = await Should.ThrowAsync<McpProtocolException>(async () => await client.GetPromptAsync("summarize", new Dictionary<string, object?> { ["tone"] = "dry" }, cancellationToken: Token));
        var empty = await Should.ThrowAsync<McpProtocolException>(async () => await client.GetPromptAsync("summarize", new Dictionary<string, object?> { ["entry"] = "" }, cancellationToken: Token));

        missing.ErrorCode.ShouldBe(McpErrorCode.InvalidParams);
        missing.Message.ShouldContain($"{McpErrorCodes.MissingPromptArgument}: The prompt 'summarize' needs the argument 'entry'.");
        empty.Message.ShouldContain(McpErrorCodes.MissingPromptArgument);
    }

    [Fact]
    public async Task AStoredPromptIsCreatedReadChangedAndDeleted()
    {
        var host = fixture.Host;
        var site = await CreateSiteAsync(host);
        using var http = await KeyInAsync(host, site);
        using var elsewhere = await KeyInAsync(host, await CreateSiteAsync(host));

        var created = await SendAsync(http, HttpMethod.Post, Prompts, Summarize, HttpStatusCode.Created);
        var path = $"{Prompts}/{created!["id"]!.GetValue<string>()}";
        var read = await SendAsync(http, HttpMethod.Get, path);
        var listed = await SendAsync(http, HttpMethod.Get, Prompts);
        var changed = await SendAsync(http, HttpMethod.Put, path, new { name = "sum-up", template = "Sum it up." });
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        var served = (await client.ListPromptsAsync(cancellationToken: Token)).Select(prompt => prompt.Name).ToList();
        var otherSite = await SendAsync(elsewhere, HttpMethod.Get, path, expected: HttpStatusCode.NotFound);
        await SendAsync(http, HttpMethod.Delete, path, expected: HttpStatusCode.NoContent);
        var gone = await SendAsync(http, HttpMethod.Get, path, expected: HttpStatusCode.NotFound);
        var goneAgain = await SendAsync(http, HttpMethod.Delete, path, expected: HttpStatusCode.NotFound);

        created["siteId"]!.GetValue<string>().ShouldBe(site.ToString("D"));
        created["name"]!.GetValue<string>().ShouldBe("summarize");
        created["arguments"]!.AsArray().Select(argument => (argument!["name"]!.GetValue<string>(), argument["required"]!.GetValue<bool>())).ShouldBe([("entry", true), ("tone", false)]);
        JsonNode.DeepEquals(read, created).ShouldBeTrue();
        JsonNode.DeepEquals(listed!["items"]!.AsArray().ShouldHaveSingleItem(), created).ShouldBeTrue();
        changed!["name"]!.GetValue<string>().ShouldBe("sum-up");
        changed["title"].ShouldBeNull();
        changed["arguments"]!.AsArray().ShouldBeEmpty();
        served.ShouldContain("sum-up");
        served.ShouldNotContain("summarize");
        foreach (var missing in new[] { otherSite, gone, goneAgain })
        {
            missing!["code"]!.GetValue<string>().ShouldBe(McpErrorCodes.PromptNotFound);
        }
    }

    [Fact]
    public async Task AStoredPromptWithTheNameOfAProvidersPromptIsRefused()
    {
        var host = fixture.Host;
        var site = await CreateSiteAsync(host);
        using var http = await KeyInAsync(host, site);
        var created = await SendAsync(http, HttpMethod.Post, Prompts, Summarize, HttpStatusCode.Created);

        var taken = await SendAsync(http, HttpMethod.Post, Prompts, new { name = SamplePrompts.Greet, template = "Hello." }, HttpStatusCode.Conflict);
        var renamed = await SendAsync(http, HttpMethod.Put, $"{Prompts}/{created!["id"]!.GetValue<string>()}", new { name = SamplePrompts.Greet, template = "Hello." }, HttpStatusCode.Conflict);
        var twice = await SendAsync(http, HttpMethod.Post, Prompts, new { name = "summarize", template = "Again." }, HttpStatusCode.Conflict);

        taken!["code"]!.GetValue<string>().ShouldBe(McpErrorCodes.PromptNameReserved);
        renamed!["code"]!.GetValue<string>().ShouldBe(McpErrorCodes.PromptNameReserved);
        twice!["code"]!.GetValue<string>().ShouldBe(McpErrorCodes.PromptNameTaken);

        // Where the plugin is off its prompt is not served, so the name is free.
        await SetEnabledAsync(host, "sample", site, enabled: false);
        await SendAsync(http, HttpMethod.Post, Prompts, new { name = SamplePrompts.Greet, template = "Hello." }, HttpStatusCode.Created);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        (await client.GetPromptAsync(SamplePrompts.Greet, cancellationToken: Token)).Messages.ShouldHaveSingleItem().Content.ShouldBeOfType<TextContentBlock>().Text.ShouldBe("Hello.");
    }

    [Fact]
    public async Task AStoredPromptThatNamesAnArgumentItDoesNotDeclareOrCannotBeSavedIsRefusedWithItsCodes()
    {
        var host = fixture.Host;
        using var http = await KeyInAsync(host, await CreateSiteAsync(host));

        var undeclared = await SendAsync(http, HttpMethod.Post, Prompts, new { name = "review", arguments = new[] { new { name = "entry" } }, template = "Review {{entry}} for {{audience}} and {{reader}}." }, HttpStatusCode.UnprocessableEntity);
        var empty = await SendAsync(http, HttpMethod.Post, Prompts, new { }, HttpStatusCode.UnprocessableEntity);
        var wrong = await SendAsync(
            http,
            HttpMethod.Post,
            Prompts,
            new
            {
                name = "Review It",
                title = new string('t', McpModel.TitleMaxLength + 1),
                description = "two\nlines",
                arguments = new object[] { new { name = "entry" }, new { name = "entry" }, new { name = "1st" }, new { } },
                template = "Review {{ entry }} and {{entry | upper}}.",
            },
            HttpStatusCode.UnprocessableEntity);
        var tooLong = await SendAsync(http, HttpMethod.Post, Prompts, new { name = "long", template = new string('x', McpPromptService.TemplateMaxLength + 1) }, HttpStatusCode.UnprocessableEntity);
        var tooMany = await SendAsync(http, HttpMethod.Post, Prompts, new { name = "many", arguments = Enumerable.Range(0, McpPromptService.MaxArguments + 1).Select(index => new { name = $"a{index}" }), template = "x" }, HttpStatusCode.UnprocessableEntity);

        undeclared!["code"]!.GetValue<string>().ShouldBe(McpErrorCodes.UndeclaredPromptArgument);
        Errors(undeclared).ShouldBe([("template", McpErrorCodes.UndeclaredPromptArgument), ("template", McpErrorCodes.UndeclaredPromptArgument)]);
        undeclared["errors"]![0]!["message"]!.GetValue<string>().ShouldContain("'audience'");
        Errors(empty).ShouldBe([("name", McpErrorCodes.InvalidPrompt), ("template", McpErrorCodes.InvalidPrompt)]);
        Errors(wrong).ShouldBe(
            [
                ("name", McpErrorCodes.InvalidPrompt),
                ("title", McpErrorCodes.InvalidPrompt),
                ("description", McpErrorCodes.InvalidPrompt),
                ("arguments[1].name", McpErrorCodes.InvalidPrompt),
                ("arguments[2].name", McpErrorCodes.InvalidPrompt),
                ("arguments[3].name", McpErrorCodes.InvalidPrompt),
                ("template", McpErrorCodes.InvalidPrompt),
            ]);
        Errors(tooLong).ShouldBe([("template", McpErrorCodes.InvalidPrompt)]);
        Errors(tooMany).ShouldBe([("arguments", McpErrorCodes.InvalidPrompt)]);
        (await SendAsync(http, HttpMethod.Get, Prompts))!["items"]!.AsArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task ASiteHoldsAtMostItsLimitOfStoredPrompts()
    {
        var host = fixture.Host;
        var site = await CreateSiteAsync(host);
        using var http = await KeyInAsync(host, site);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<McpPlugin>>();
            database.AddRange(Enumerable.Range(0, McpPromptService.MaxPrompts).Select(index => new McpPrompt { Id = Guid.CreateVersion7(), SiteId = site, Name = $"p{index}", Template = "x" }));
            await database.SaveChangesAsync(Token);
        }

        var refused = await SendAsync(http, HttpMethod.Post, Prompts, new { name = "one-more", template = "x" }, HttpStatusCode.UnprocessableEntity);

        Errors(refused).ShouldBe([("", McpErrorCodes.InvalidPrompt)]);
    }

    // A row that holds something other than the arguments the service writes is left out, and the others are served.
    [Fact]
    public async Task AStoredPromptWhoseArgumentsCannotBeReadIsLeftOut()
    {
        var host = fixture.Host;
        var site = await CreateSiteAsync(host);
        using var http = await KeyInAsync(host, site);
        await SendAsync(http, HttpMethod.Post, Prompts, Summarize, HttpStatusCode.Created);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<McpPlugin>>();
            database.Add(new McpPrompt { Id = Guid.CreateVersion7(), SiteId = site, Name = "broken", Arguments = "{ not a list", Template = "x" });
            await database.SaveChangesAsync(Token);
        }

        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        (await client.ListPromptsAsync(cancellationToken: Token)).Select(prompt => prompt.Name).ShouldBe([SamplePrompts.Greet, "summarize"], ignoreOrder: true);
    }

    [Fact]
    public async Task ThePromptEndpointsNeedMcpManageForTheSite()
    {
        var host = fixture.Host;
        var site = await CreateSiteAsync(host);
        using var admin = await KeyInAsync(host, site);
        using var keeper = await KeyInAsync(host, site, await CreateRoleAsync(host, (McpPermissions.Manage, new PermissionConstraints { Sites = [new SiteId(site)] })));
        using var reader = await KeyInAsync(host, site, await CreateRoleAsync(host, ("content.article.read", null)));
        using var elsewhere = await KeyInAsync(host, site, await CreateRoleAsync(host, (McpPermissions.Manage, new PermissionConstraints { Sites = [new SiteId(Guid.CreateVersion7())] })));
        using var anonymous = host.CreateClient();
        var path = $"{Prompts}/{(await SendAsync(admin, HttpMethod.Post, Prompts, Summarize, HttpStatusCode.Created))!["id"]!.GetValue<string>()}";
        var body = new { name = "other", template = "x" };

        await SendAsync(anonymous, HttpMethod.Get, Prompts, expected: HttpStatusCode.Unauthorized);
        foreach (var refused in new[] { reader, elsewhere })
        {
            (await SendAsync(refused, HttpMethod.Get, Prompts, expected: HttpStatusCode.Forbidden))!.ToJsonString().ShouldContain(McpPermissions.Manage);
            await SendAsync(refused, HttpMethod.Post, Prompts, body, HttpStatusCode.Forbidden);
            await SendAsync(refused, HttpMethod.Get, path, expected: HttpStatusCode.Forbidden);
            await SendAsync(refused, HttpMethod.Put, path, body, HttpStatusCode.Forbidden);
            await SendAsync(refused, HttpMethod.Delete, path, expected: HttpStatusCode.Forbidden);
        }

        (await SendAsync(keeper, HttpMethod.Get, Prompts))!["items"]!.AsArray().ShouldHaveSingleItem();
        await SendAsync(keeper, HttpMethod.Get, path);
        await SendAsync(keeper, HttpMethod.Put, path, body);
        await SendAsync(keeper, HttpMethod.Delete, path, expected: HttpStatusCode.NoContent);

        // A reader of the site still gets the stored prompts through the server: they are for every agent of the site.
        await SendAsync(admin, HttpMethod.Post, Prompts, Summarize, HttpStatusCode.Created);
        await using var agent = await ConnectAsync(reader, protocolVersion: InitializeProtocol);
        (await agent.ListPromptsAsync(cancellationToken: Token)).Select(prompt => prompt.Name).ShouldContain("summarize");
    }

    [Fact]
    public async Task EachChangeOfAStoredPromptRaisesItsEventAndWritesAnActivityRow()
    {
        var host = fixture.Host;
        var site = await CreateSiteAsync(host);
        using var http = await KeyInAsync(host, site);

        var id = (await SendAsync(http, HttpMethod.Post, Prompts, Summarize, HttpStatusCode.Created))!["id"]!.GetValue<string>();
        await SendAsync(http, HttpMethod.Put, $"{Prompts}/{id}", new { name = "sum-up", template = "Sum it up, secretly." });
        await SendAsync(http, HttpMethod.Delete, $"{Prompts}/{id}", expected: HttpStatusCode.NoContent);

        var created = (await ActivityAsync(host, http, "mcp.prompt_created")).ShouldHaveSingleItem();
        var updated = (await ActivityAsync(host, http, "mcp.prompt_updated")).ShouldHaveSingleItem();
        var deleted = (await ActivityAsync(host, http, "mcp.prompt_deleted")).ShouldHaveSingleItem();

        (created["targetType"]!.GetValue<string>(), created["targetId"]!.GetValue<string>(), created["targetLabel"]!.GetValue<string>()).ShouldBe(("mcp_prompt", id, "summarize"));
        (updated["targetId"]!.GetValue<string>(), updated["targetLabel"]!.GetValue<string>()).ShouldBe((id, "sum-up"));
        (deleted["targetId"]!.GetValue<string>(), deleted["targetLabel"]!.GetValue<string>()).ShouldBe((id, "sum-up"));
        new[] { created, updated, deleted }.ShouldAllBe(row => !row.ToJsonString().Contains("secretly", StringComparison.Ordinal));
    }
}
