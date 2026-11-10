using System.Text.Json;
using FluentCMS.Core.Authorization;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Mcp.Tests.McpTesting;
using static FluentCMS.Plugins.Mcp.Tests.McpToolsTesting;

namespace FluentCMS.Plugins.Mcp.Tests;

// The dry run in front of {type}_delete: a call answers the plan and a confirmation token and changes nothing, and only
// the same call with that token runs.
public sealed class McpDryRunTests(McpToolsFixture fixture) : IClassFixture<McpToolsFixture>
{
    [Fact]
    public async Task ADeleteWithoutDryRunDeletesNothingAndAnswersThePlanAndTheSameCallWithTheTokenDeletesTheEntry()
    {
        var host = fixture.Host;
        using var http = await KeyAsync(host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        var id = await PostAsync(client, "To the trash");

        var plan = await OkAsync(client, "post_delete", new { id });
        var asked = await OkAsync(client, "post_delete", new { id, dryRun = true });
        var kept = await OkAsync(client, "post_get", new { id });
        var deleted = await OkAsync(client, "post_delete", new { id, dryRun = false, confirmationToken = TokenOf(plan) });
        var gone = await CallAsync(client, "post_get", new { id });

        plan.GetProperty("dryRun").GetBoolean().ShouldBeTrue();
        plan.GetProperty("type").GetString().ShouldBe("post");
        plan.GetProperty("action").GetString().ShouldBe("delete");
        plan.GetProperty("count").GetInt32().ShouldBe(1);
        var entry = plan.GetProperty("entries").EnumerateArray().ShouldHaveSingleItem();
        entry.GetProperty("id").GetGuid().ShouldBe(id);
        entry.GetProperty("title").GetString().ShouldBe("To the trash");
        entry.GetProperty("status").GetString().ShouldBe("draft");
        entry.GetProperty("version").GetInt32().ShouldBe(1);
        plan.GetProperty("references").GetArrayLength().ShouldBe(0);
        plan.GetProperty("expiresAt").GetDateTimeOffset().ShouldBe(DateTimeOffset.FromUnixTimeSeconds(host.Time.GetUtcNow().AddMinutes(5).ToUnixTimeSeconds()));
        asked.GetProperty("dryRun").GetBoolean().ShouldBeTrue();
        kept.GetProperty("id").GetGuid().ShouldBe(id);
        deleted.GetProperty("deleted").GetBoolean().ShouldBeTrue();
        deleted.GetProperty("id").GetGuid().ShouldBe(id);
        Code(gone).ShouldBe("FCMS3104");
    }

    [Fact]
    public async Task ARunWithNoTokenAnExpiredTokenOrATokenOfAnotherClientIsRefusedAndNothingIsDeleted()
    {
        var host = fixture.Host;
        using var http = await KeyAsync(host);
        using var otherHttp = await KeyAsync(host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        await using var other = await ConnectAsync(otherHttp, protocolVersion: InitializeProtocol);
        var id = await PostAsync(client, "Stays");
        var second = await PostAsync(client, "Stays too");
        var inTime = await PostAsync(client, "Goes in time");
        var token = TokenOf(await OkAsync(client, "post_delete", new { id }));
        var inTimeToken = TokenOf(await OkAsync(client, "post_delete", new { id = inTime }));

        var missing = await CallAsync(client, "post_delete", new { id, dryRun = false });
        var ofAnotherClient = await CallAsync(other, "post_delete", new { id, dryRun = false, confirmationToken = token });
        var ofAnotherEntry = await CallAsync(client, "post_delete", new { id = second, dryRun = false, confirmationToken = token });
        var ofAnotherTool = await CallAsync(client, McpContentTools.Bulk, new { type = "post", action = "delete", ids = new[] { id }, limit = 1, dryRun = false, confirmationToken = token });
        var changed = await CallAsync(client, "post_delete", new { id, dryRun = false, confirmationToken = token[..^1] + (token[^1] == 'A' ? 'B' : 'A') });
        var notAToken = await CallAsync(client, "post_delete", new { id, dryRun = false, confirmationToken = "v1.confirmed" });

        // A token lasts five minutes and not a second more.
        host.Time.Advance(TimeSpan.FromSeconds(299));
        var lastSecond = await OkAsync(client, "post_delete", new { id = inTime, dryRun = false, confirmationToken = inTimeToken });
        host.Time.Advance(TimeSpan.FromSeconds(1));
        var expired = await CallAsync(client, "post_delete", new { id, dryRun = false, confirmationToken = token });

        Code(missing).ShouldBe("FCMS1630");
        Code(ofAnotherClient).ShouldBe("FCMS1631");
        Code(ofAnotherEntry).ShouldBe("FCMS1631");
        Code(ofAnotherTool).ShouldBe("FCMS1631");
        Code(changed).ShouldBe("FCMS1631");
        Code(notAToken).ShouldBe("FCMS1631");
        Code(expired).ShouldBe("FCMS1632");
        new[] { missing, ofAnotherClient, ofAnotherEntry, ofAnotherTool, changed, notAToken, expired }.ShouldAllBe(result => result.IsError == true);
        lastSecond.GetProperty("deleted").GetBoolean().ShouldBeTrue();
        (await OkAsync(client, "post_get", new { id })).GetProperty("title").GetString().ShouldBe("Stays");
        (await OkAsync(client, "post_get", new { id = second })).GetProperty("title").GetString().ShouldBe("Stays too");
    }

    [Fact]
    public async Task ATokenMadeBeforeTheEntryChangedIsRefusedAndNothingIsDeleted()
    {
        using var http = await KeyAsync(fixture.Host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        var id = await PostAsync(client, "Reviewed");
        var token = TokenOf(await OkAsync(client, "post_delete", new { id }));

        await OkAsync(client, "post_update_draft", new { id, expectedVersion = 1, fields = new { title = "Changed since" } });
        var stale = await CallAsync(client, "post_delete", new { id, dryRun = false, confirmationToken = token });
        var kept = await OkAsync(client, "post_get", new { id });
        var again = await OkAsync(client, "post_delete", new { id });
        var deleted = await OkAsync(client, "post_delete", new { id, dryRun = false, confirmationToken = TokenOf(again) });

        stale.IsError.ShouldBe(true);
        Code(stale).ShouldBe("FCMS1633");
        kept.GetProperty("title").GetString().ShouldBe("Changed since");
        again.GetProperty("entries")[0].GetProperty("version").GetInt32().ShouldBe(2);
        deleted.GetProperty("deleted").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task ThePlanOfADeleteNamesTheReferencesThatWouldBreak()
    {
        using var http = await KeyAsync(fixture.Host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        var writer = (await OkAsync(client, "writer_create_draft", new { fields = new { name = "Ada" } })).GetProperty("id").GetGuid();
        var post = (await OkAsync(client, "post_create_draft", new { fields = new { title = "By Ada", author = writer } })).GetProperty("id").GetGuid();
        await PostAsync(client, "By no one");

        var plan = await OkAsync(client, "writer_delete", new { id = writer });

        plan.GetProperty("entries")[0].GetProperty("title").GetString().ShouldBe("Ada");
        var reference = plan.GetProperty("references").EnumerateArray().ShouldHaveSingleItem();
        reference.GetProperty("type").GetString().ShouldBe("post");
        reference.GetProperty("id").GetGuid().ShouldBe(post);
        reference.GetProperty("title").GetString().ShouldBe("By Ada");
        reference.GetProperty("field").GetString().ShouldBe("author");
        plan.TryGetProperty("moreReferences", out _).ShouldBeFalse();
    }

    // A reference of an entry the caller may not read is not in its plan, as no read of the caller shows that entry.
    [Fact]
    public async Task ThePlanNamesOnlyReferencesTheCallerMayRead()
    {
        var host = fixture.Host;
        using var admin = await KeyAsync(host);
        using var http = await RoleAsync(host, Grant("content.writer.read"), Grant("content.writer.delete"));
        await using var adminClient = await ConnectAsync(admin, protocolVersion: InitializeProtocol);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        var writer = (await OkAsync(adminClient, "writer_create_draft", new { fields = new { name = "Grace" } })).GetProperty("id").GetGuid();
        await OkAsync(adminClient, "post_create_draft", new { fields = new { title = "By Grace", author = writer } });

        var plan = await OkAsync(client, "writer_delete", new { id = writer });

        plan.GetProperty("references").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task ADeleteOfAnEntryThatIsNotThereIsNotFoundBeforeAnyPlan()
    {
        using var http = await KeyAsync(fixture.Host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var missing = await CallAsync(client, "post_delete", new { id = Guid.NewGuid() });
        var unknownArgument = await CallAsync(client, "post_delete", new { id = Guid.NewGuid(), force = true });
        var notAFlag = await CallAsync(client, "post_delete", new { id = Guid.NewGuid(), dryRun = "no" });

        Code(missing).ShouldBe("FCMS3104");
        Code(unknownArgument).ShouldBe("FCMS2108");
        Text(unknownArgument).ShouldContain("- force: ");
        Code(notAFlag).ShouldBe("FCMS2108");
        Text(notAFlag).ShouldContain("- dryRun: ");
    }

    [Fact]
    public async Task ATokenMadeOnOneInstanceIsAcceptedByAnotherThatSharesTheDataProtectionKeyRing()
    {
        var directory = Directory.CreateTempSubdirectory("fluentcms-mcp-instances-");
        try
        {
            var database = Path.Combine(directory.FullName, "cms.db");
            using var downloads = new FakeDownloads();
            await using var first = Host(directory.FullName, downloads).UseSqlite(database);
            await using var second = Host(directory.FullName, downloads).UseSqlite(database);
            var key = await first.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token);
            using var firstHttp = first.AsKey(key);
            using var secondHttp = second.AsKey(key);
            await using var onFirst = await ConnectAsync(firstHttp, protocolVersion: InitializeProtocol);
            await using var onSecond = await ConnectAsync(secondHttp, protocolVersion: InitializeProtocol);
            var id = await PostAsync(onFirst, "On two instances");

            var plan = await OkAsync(onFirst, "post_delete", new { id });
            var deleted = await OkAsync(onSecond, "post_delete", new { id, dryRun = false, confirmationToken = TokenOf(plan) });

            // Each instance signs with a key of its own, which the other unwraps with the ring they share.
            first.Services.GetRequiredService<McpConfirmations>().ShouldNotBeSameAs(second.Services.GetRequiredService<McpConfirmations>());
            deleted.GetProperty("deleted").GetBoolean().ShouldBeTrue();
            Code(await CallAsync(onFirst, "post_get", new { id })).ShouldBe("FCMS3104");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static async Task<Guid> PostAsync(McpClient client, string title) =>
        (await OkAsync(client, "post_create_draft", new { fields = new { title } })).GetProperty("id").GetGuid();
}
