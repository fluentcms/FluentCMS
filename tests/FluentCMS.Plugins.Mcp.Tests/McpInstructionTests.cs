using System.Net;
using FluentCMS.Core.Authorization;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Mcp.Tests.McpSiteTesting;
using static FluentCMS.Plugins.Mcp.Tests.McpTesting;

namespace FluentCMS.Plugins.Mcp.Tests;

// The guidelines of a site and the instructions of its content types (#2980): kept through the Management API, read as
// resources, and the guidelines carried by the handshake.
public sealed class McpInstructionTests(McpSiteFixture fixture) : IClassFixture<McpSiteFixture>
{
    [Fact]
    public async Task GuidelinesSavedForASiteAreReadAtItsResourceAndComeWithTheHandshake()
    {
        var host = fixture.Host;
        var site = await CreateSiteAsync(host);
        var other = await CreateSiteAsync(host);
        using var http = await KeyInAsync(host, site);
        using var elsewhere = await KeyInAsync(host, other);
        const string text = "Titles are sentence case.\nNever touch legal pages.";

        var saved = await SendAsync(http, HttpMethod.Put, Instructions, new { text = $"  {text}  " });
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        await using var newest = await ConnectAsync(http);
        await using var inOther = await ConnectAsync(elsewhere, protocolVersion: InitializeProtocol);
        var uri = McpSiteResources.Guidelines(site);

        saved!["text"]!.GetValue<string>().ShouldBe(text);
        saved["type"].ShouldBeNull();
        uri.ShouldBe($"fluentcms://sites/{site:D}/guidelines");
        client.ServerInstructions.ShouldBe(text);
        newest.ServerInstructions.ShouldBe(text);
        (await client.ListResourcesAsync(cancellationToken: Token)).Select(resource => resource.Uri).ShouldContain(uri);
        (await ReadAsync(client, uri)).ShouldBe(text);

        // Another site has none, and reads its own only.
        inOther.ServerInstructions.ShouldBeNull();
        (await inOther.ListResourcesAsync(cancellationToken: Token)).ShouldAllBe(resource => !resource.Uri.EndsWith("/guidelines", StringComparison.Ordinal));
        (await RefusalAsync(inOther, uri)).ShouldContain(McpErrorCodes.UnknownResource);
        (await RefusalAsync(inOther, McpSiteResources.Guidelines(other))).ShouldContain(McpErrorCodes.UnknownResource);

        await SendAsync(http, HttpMethod.Delete, Instructions, expected: HttpStatusCode.NoContent);
        await using var after = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        after.ServerInstructions.ShouldBeNull();
        (await RefusalAsync(after, uri)).ShouldContain(McpErrorCodes.UnknownResource);
    }

    [Fact]
    public async Task InstructionsSavedForATypeAreReadAtItsResourceByACallerThatReadsTheType()
    {
        var host = fixture.Host;
        var site = await CreateSiteAsync(host);
        using var http = await KeyInAsync(host, site);
        using var pagesOnly = await KeyInAsync(host, site, await CreateRoleAsync(host, ("schema.read", null), ("content.page.read", null)));
        const string text = "An article has a title of at most six words.";

        var saved = await SendAsync(http, HttpMethod.Put, $"{Instructions}/article", new { text });
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        await using var reader = await ConnectAsync(pagesOnly, protocolVersion: InitializeProtocol);
        var uri = McpSiteResources.Instructions("article");

        saved!["type"]!.GetValue<string>().ShouldBe("article");
        saved["orphaned"]!.GetValue<bool>().ShouldBeFalse();
        uri.ShouldBe("fluentcms://types/article/instructions");
        (await client.ListResourcesAsync(cancellationToken: Token)).Select(resource => resource.Uri).ShouldContain(uri);
        (await ReadAsync(client, uri)).ShouldBe(text);
        (await RefusalAsync(client, McpSiteResources.Instructions("page"))).ShouldContain(McpErrorCodes.UnknownResource);

        // A type the caller may not read answers as one with no instructions, and is not listed.
        (await reader.ListResourcesAsync(cancellationToken: Token)).Select(resource => resource.Uri).ShouldNotContain(uri);
        (await RefusalAsync(reader, uri)).ShouldContain(McpErrorCodes.UnknownResource);
    }

    [Fact]
    public async Task InstructionsOfATypeThatNoLongerExistsAreLeftOutOfTheResourcesAndListedToBeRemoved()
    {
        var host = fixture.Host;
        var site = await CreateSiteAsync(host);
        using var http = await KeyInAsync(host, site);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<McpPlugin>>();
            database.Add(new McpInstruction { Id = Guid.CreateVersion7(), SiteId = site, Type = "retired", Text = "Was written for a type that went.", UpdatedAt = host.Time.GetUtcNow() });
            await database.SaveChangesAsync(Token);
        }

        await SendAsync(http, HttpMethod.Put, $"{Instructions}/page", new { text = "Pages are short." });
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var listed = (await SendAsync(http, HttpMethod.Get, Instructions))!["items"]!.AsArray();
        var resources = (await client.ListResourcesAsync(cancellationToken: Token)).Select(resource => resource.Uri).ToList();
        var read = await RefusalAsync(client, McpSiteResources.Instructions("retired"));
        var written = await SendAsync(http, HttpMethod.Put, $"{Instructions}/retired", new { text = "Again." }, HttpStatusCode.NotFound);
        await SendAsync(http, HttpMethod.Delete, $"{Instructions}/retired", expected: HttpStatusCode.NoContent);
        var left = (await SendAsync(http, HttpMethod.Get, Instructions))!["items"]!.AsArray();

        listed.Select(item => (item!["type"]!.GetValue<string>(), item["orphaned"]!.GetValue<bool>())).ShouldBe([("page", false), ("retired", true)]);
        resources.ShouldContain(McpSiteResources.Instructions("page"));
        resources.ShouldNotContain(McpSiteResources.Instructions("retired"));
        read.ShouldContain(McpErrorCodes.UnknownResource);
        written!["code"]!.GetValue<string>().ShouldBe(McpErrorCodes.InstructionTypeNotFound);
        left.ShouldHaveSingleItem()!["type"]!.GetValue<string>().ShouldBe("page");
    }

    [Fact]
    public async Task InstructionsThatCannotBeSavedOrAreNotThereAreRefusedWithTheirCodes()
    {
        var host = fixture.Host;
        using var http = await KeyInAsync(host, await CreateSiteAsync(host));

        var empty = await SendAsync(http, HttpMethod.Put, Instructions, new { text = "  " }, HttpStatusCode.UnprocessableEntity);
        var missing = await SendAsync(http, HttpMethod.Put, $"{Instructions}/article", new { }, HttpStatusCode.UnprocessableEntity);
        var tooLong = await SendAsync(http, HttpMethod.Put, Instructions, new { text = new string('x', McpInstructionService.TextMaxLength + 1) }, HttpStatusCode.UnprocessableEntity);
        var control = await SendAsync(http, HttpMethod.Put, Instructions, new { text = "bell \u0007" }, HttpStatusCode.UnprocessableEntity);
        var longest = await SendAsync(http, HttpMethod.Put, Instructions, new { text = new string('x', McpInstructionService.TextMaxLength) });
        var noType = await SendAsync(http, HttpMethod.Put, $"{Instructions}/nothing", new { text = "For no type." }, HttpStatusCode.NotFound);
        var noRow = await SendAsync(http, HttpMethod.Delete, $"{Instructions}/article", expected: HttpStatusCode.NotFound);

        foreach (var refused in new[] { empty, missing, tooLong, control })
        {
            Errors(refused).ShouldBe([("text", McpErrorCodes.InvalidInstruction)]);
        }

        longest!["text"]!.GetValue<string>().Length.ShouldBe(McpInstructionService.TextMaxLength);
        noType!["code"]!.GetValue<string>().ShouldBe(McpErrorCodes.InstructionTypeNotFound);
        noRow!["code"]!.GetValue<string>().ShouldBe(McpErrorCodes.InstructionNotFound);
    }

    // Writers that all find no row meet at the unique index, and each still saves: a site keeps one row of guidelines.
    [Fact]
    public async Task SavesOfNewGuidelinesAtOnceAllSucceedAndLeaveOneRow()
    {
        var host = fixture.Host;
        var site = await CreateSiteAsync(host);
        using var http = await KeyInAsync(host, site);

        var saved = await Task.WhenAll(Enumerable.Range(0, 8).Select(index => SendAsync(http, HttpMethod.Put, Instructions, new { text = $"Version {index}." })));

        var row = (await SendAsync(http, HttpMethod.Get, Instructions))!["items"]!.AsArray().ShouldHaveSingleItem()!;
        saved.Select(answer => answer!["text"]!.GetValue<string>()).ShouldContain(row["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task TheInstructionEndpointsNeedMcpManageForTheSite()
    {
        var host = fixture.Host;
        var site = await CreateSiteAsync(host);
        using var keeper = await KeyInAsync(host, site, await CreateRoleAsync(host, (McpPermissions.Manage, new PermissionConstraints { Sites = [new SiteId(site)] })));
        using var reader = await KeyInAsync(host, site, await CreateRoleAsync(host, ("content.article.read", null)));
        using var elsewhere = await KeyInAsync(host, site, await CreateRoleAsync(host, (McpPermissions.Manage, new PermissionConstraints { Sites = [new SiteId(Guid.CreateVersion7())] })));
        using var anonymous = host.CreateClient();
        var body = new { text = "Write plainly." };

        await SendAsync(anonymous, HttpMethod.Get, Instructions, expected: HttpStatusCode.Unauthorized);
        foreach (var refused in new[] { reader, elsewhere })
        {
            (await SendAsync(refused, HttpMethod.Get, Instructions, expected: HttpStatusCode.Forbidden))!.ToJsonString().ShouldContain(McpPermissions.Manage);
            await SendAsync(refused, HttpMethod.Put, Instructions, body, HttpStatusCode.Forbidden);
            await SendAsync(refused, HttpMethod.Put, $"{Instructions}/article", body, HttpStatusCode.Forbidden);
            await SendAsync(refused, HttpMethod.Delete, Instructions, expected: HttpStatusCode.Forbidden);
            await SendAsync(refused, HttpMethod.Delete, $"{Instructions}/article", expected: HttpStatusCode.Forbidden);
        }

        await SendAsync(keeper, HttpMethod.Put, Instructions, body);
        await SendAsync(keeper, HttpMethod.Put, $"{Instructions}/article", body);
        (await SendAsync(keeper, HttpMethod.Get, Instructions))!["items"]!.AsArray().Count.ShouldBe(2);
        await SendAsync(keeper, HttpMethod.Delete, Instructions, expected: HttpStatusCode.NoContent);
        await SendAsync(keeper, HttpMethod.Delete, $"{Instructions}/article", expected: HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task EachChangeOfTheInstructionsRaisesItsEventAndWritesAnActivityRow()
    {
        var host = fixture.Host;
        var site = await CreateSiteAsync(host);
        using var http = await KeyInAsync(host, site);
        const string text = "Do not name this text in the log.";

        await SendAsync(http, HttpMethod.Put, Instructions, new { text });
        await SendAsync(http, HttpMethod.Put, Instructions, new { text = text + " Twice." });
        await SendAsync(http, HttpMethod.Put, $"{Instructions}/article", new { text });
        await SendAsync(http, HttpMethod.Delete, $"{Instructions}/article", expected: HttpStatusCode.NoContent);
        await SendAsync(http, HttpMethod.Delete, Instructions, expected: HttpStatusCode.NoContent);

        var saved = await ActivityAsync(host, http, "mcp.instruction_saved");
        var deleted = await ActivityAsync(host, http, "mcp.instruction_deleted");

        saved.Select(row => (row["targetType"]?.GetValue<string>(), row["targetId"]?.GetValue<string>())).ShouldBe([("mcp_guidelines", null), ("mcp_guidelines", null), ("mcp_instruction", "article")]);
        deleted.Select(row => (row["targetType"]?.GetValue<string>(), row["targetId"]?.GetValue<string>())).ShouldBe([("mcp_instruction", "article"), ("mcp_guidelines", null)]);
        saved.Concat(deleted).ShouldAllBe(row => row["siteId"]!.GetValue<string>() == site.ToString("D") && !row.ToJsonString().Contains(text, StringComparison.Ordinal));
    }

    // A deleted site takes its guidelines, its instructions and its stored prompts with it, and another site keeps its own.
    // The handler may run again for the same event, and then finds nothing.
    [Fact]
    public async Task DeletingASiteRemovesItsGuidelinesInstructionsAndPrompts()
    {
        var host = fixture.Host;
        var site = await CreateSiteAsync(host);
        var other = await CreateSiteAsync(host);
        foreach (var each in (Guid[])[site, other])
        {
            using var http = await KeyInAsync(host, each);
            await SendAsync(http, HttpMethod.Put, Instructions, new { text = "Titles are sentence case." });
            await SendAsync(http, HttpMethod.Put, $"{Instructions}/article", new { text = "Articles are short." });
            await SendAsync(http, HttpMethod.Post, Prompts, new { name = "sum-up", template = "Sum it up." }, HttpStatusCode.Created);
        }

        await host.As(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token)).RunAsync<ISiteAdministration, bool>(async sites =>
        {
            await sites.DeleteAsync(new SiteId(site), Token);
            return true;
        });
        await DrainOutboxAsync(host);

        await using var scope = host.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<McpPlugin>>();
        Guid[] both = [site, other];
        (await database.Set<McpInstruction>().AsNoTracking().Where(row => both.Contains(row.SiteId)).Select(row => row.SiteId).ToListAsync(Token)).ShouldBe([other, other]);
        (await database.Set<McpPrompt>().AsNoTracking().Where(row => both.Contains(row.SiteId)).Select(row => row.SiteId).ToListAsync(Token)).ShouldBe([other]);
        (await scope.ServiceProvider.GetRequiredService<McpInstructionService>().RemoveAllAsync(site, Token)).ShouldBe(0);
        (await scope.ServiceProvider.GetRequiredService<McpPromptService>().RemoveAllAsync(site, Token)).ShouldBe(0);
    }
}
