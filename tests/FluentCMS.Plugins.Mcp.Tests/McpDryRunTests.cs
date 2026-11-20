using System.Net;
using System.Text;
using System.Text.Json;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Sites;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Mcp.Tests.McpTesting;
using static FluentCMS.Plugins.Mcp.Tests.McpToolsTesting;

namespace FluentCMS.Plugins.Mcp.Tests;

// The dry run in front of {type}_delete, unpublish and a scheduled unpublish: a call answers the plan and a confirmation
// token and changes nothing, and only the same call with that token runs.
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

    // An unpublish takes live content down, so it is planned and confirmed as a delete is; a publish stays direct.
    [Fact]
    public async Task AnUnpublishWithoutAValidTokenAnswersThePlanAndChangesNothing()
    {
        using var http = await KeyAsync(fixture.Host);
        using var otherHttp = await KeyAsync(fixture.Host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        await using var other = await ConnectAsync(otherHttp, protocolVersion: InitializeProtocol);
        var writer = (await OkAsync(client, "writer_create_draft", new { fields = new { name = "Ada" } })).GetProperty("id").GetGuid();
        var post = (await OkAsync(client, "post_create_draft", new { fields = new { title = "By Ada", author = writer } })).GetProperty("id").GetGuid();
        var second = await PostAsync(client, "Live too");
        await OkAsync(client, McpContentTools.Publish, new { type = "writer", id = writer });
        await OkAsync(client, McpContentTools.Publish, new { type = "post", id = post });
        await OkAsync(client, McpContentTools.Publish, new { type = "post", id = second });
        var arguments = new { type = "writer", id = writer };

        var plan = await OkAsync(client, McpContentTools.Unpublish, arguments);
        var asked = await OkAsync(client, McpContentTools.Unpublish, new { type = "writer", id = writer, dryRun = true });
        var missing = await CallAsync(client, McpContentTools.Unpublish, Confirmed(arguments, null));
        var forged = await CallAsync(client, McpContentTools.Unpublish, Confirmed(arguments, "v1.confirmed"));
        var ofAnotherClient = await CallAsync(other, McpContentTools.Unpublish, Confirmed(arguments, TokenOf(plan)));
        var ofAnotherEntry = await CallAsync(client, McpContentTools.Unpublish, Confirmed(new { type = "post", id = second }, TokenOf(plan)));
        var ofADelete = await CallAsync(client, McpContentTools.Unpublish, Confirmed(new { type = "post", id = second }, TokenOf(await OkAsync(client, "post_delete", new { id = second }))));
        var stillLive = await OkAsync(client, "writer_get", new { id = writer, status = "published" });
        var secondLive = await OkAsync(client, "post_get", new { id = second, status = "published" });
        var unpublished = await OkAsync(client, McpContentTools.Unpublish, Confirmed(arguments, TokenOf(plan)));

        plan.GetProperty("dryRun").GetBoolean().ShouldBeTrue();
        plan.GetProperty("action").GetString().ShouldBe("unpublish");
        plan.GetProperty("entries").EnumerateArray().ShouldHaveSingleItem().GetProperty("status").GetString().ShouldBe("published");
        plan.GetProperty("references").EnumerateArray().ShouldHaveSingleItem().GetProperty("id").GetGuid().ShouldBe(post);
        asked.GetProperty("dryRun").GetBoolean().ShouldBeTrue();
        Code(missing).ShouldBe("FCMS1630");
        Code(forged).ShouldBe("FCMS1631");
        Code(ofAnotherClient).ShouldBe("FCMS1631");
        Code(ofAnotherEntry).ShouldBe("FCMS1631");
        Code(ofADelete).ShouldBe("FCMS1631");
        stillLive.GetProperty("id").GetGuid().ShouldBe(writer);
        secondLive.GetProperty("id").GetGuid().ShouldBe(second);
        unpublished.GetProperty("status").GetString().ShouldBe("unpublished");
    }

    // The plan of an unpublish reads its entry and the references in the locale of the call, and its token is of that
    // locale: the same call in another locale is another call. The chapter is published in French only, so only the plan
    // in French names it.
    [Fact]
    public async Task ThePlanOfAnUnpublishIsOfTheLocaleOfTheCall()
    {
        await using var host = Host(Path.GetTempPath(), new FakeDownloads()).WithContentTypes(typeof(Guide), typeof(Chapter));
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<SiteManager>().EnsureLocaleAsync(DefaultSite.Id, "fr", Token);
        }

        using var http = await KeyAsync(host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        var guide = (await OkAsync(client, "guide_create_draft", new { fields = new { name = "Guide" } })).GetProperty("id").GetGuid();
        await PatchAsync(http, $"guide/{guide:D}?locale=fr", """{ "name": "Guide" }""");
        var chapter = (await OkAsync(client, "chapter_create_draft", new { fields = new { title = "On its own" } })).GetProperty("id").GetGuid();
        await PatchAsync(http, $"chapter/{chapter:D}?locale=fr", $$"""{ "title": "Dans le guide", "guide": "{{guide:D}}" }""");
        await OkAsync(client, McpContentTools.Publish, new { type = "guide", id = guide });
        await OkAsync(client, McpContentTools.Publish, new { type = "guide", id = guide, locale = "fr" });
        await OkAsync(client, McpContentTools.Publish, new { type = "chapter", id = chapter, locale = "fr" });

        var french = await OkAsync(client, McpContentTools.Unpublish, new { type = "guide", id = guide, locale = "fr" });
        var english = await OkAsync(client, McpContentTools.Unpublish, new { type = "guide", id = guide, locale = "en" });
        var otherLocale = await CallAsync(client, McpContentTools.Unpublish, Confirmed(new { type = "guide", id = guide, locale = "en" }, TokenOf(french)));
        var noLocale = await CallAsync(client, McpContentTools.Unpublish, Confirmed(new { type = "guide", id = guide }, TokenOf(french)));
        var unpublished = await OkAsync(client, McpContentTools.Unpublish, Confirmed(new { type = "guide", id = guide, locale = "fr" }, TokenOf(french)));

        french.GetProperty("references").EnumerateArray().ShouldHaveSingleItem().GetProperty("id").GetGuid().ShouldBe(chapter);
        english.GetProperty("references").GetArrayLength().ShouldBe(0);
        Code(otherLocale).ShouldBe("FCMS1631");
        Code(noLocale).ShouldBe("FCMS1631");
        unpublished.GetProperty("status").GetString().ShouldBe("unpublished");
        (await OkAsync(client, "guide_get", new { id = guide, status = "published" })).GetProperty("id").GetGuid().ShouldBe(guide);

        static async Task PatchAsync(HttpClient http, string path, string body)
        {
            using var request = new HttpRequestMessage(HttpMethod.Patch, new Uri($"/api/management/v1/content/{path}", UriKind.Relative))
            {
                Content = new StringContent(body, Encoding.UTF8, "application/merge-patch+json"),
            };
            request.Headers.TryAddWithoutValidation("If-Match", "\"0\"");
            using var response = await http.SendAsync(request, Token);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        }
    }

    // A scheduled unpublish takes live content down a moment later, so it is planned and confirmed as an unpublish is,
    // with a token of its time.
    [Fact]
    public async Task AScheduledUnpublishWithoutAValidTokenAnswersThePlanAndSchedulesNothing()
    {
        using var http = await KeyAsync(fixture.Host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        var writer = (await OkAsync(client, "writer_create_draft", new { fields = new { name = "Ada" } })).GetProperty("id").GetGuid();
        var post = (await OkAsync(client, "post_create_draft", new { fields = new { title = "By Ada", author = writer } })).GetProperty("id").GetGuid();
        await OkAsync(client, McpContentTools.Publish, new { type = "writer", id = writer });
        await OkAsync(client, McpContentTools.Publish, new { type = "post", id = post });
        var arguments = new { type = "writer", id = writer, action = "unpublish", at = "2999-03-01T09:00:00Z" };
        var later = new { type = "writer", id = writer, action = "unpublish", at = "2999-03-02T09:00:00Z" };

        var plan = await OkAsync(client, McpContentTools.Schedule, arguments);
        var asked = await OkAsync(client, McpContentTools.Schedule, new { type = "writer", id = writer, action = "unpublish", at = "2999-03-01T09:00:00Z", dryRun = true });
        var missing = await CallAsync(client, McpContentTools.Schedule, Confirmed(arguments, null));
        var forged = await CallAsync(client, McpContentTools.Schedule, Confirmed(arguments, "v1.confirmed"));
        var ofAnotherTime = await CallAsync(client, McpContentTools.Schedule, Confirmed(later, TokenOf(plan)));
        var ofAnUnpublish = await CallAsync(client, McpContentTools.Schedule, Confirmed(arguments, TokenOf(await OkAsync(client, McpContentTools.Unpublish, new { type = "writer", id = writer }))));
        var onAnUnpublish = await CallAsync(client, McpContentTools.Unpublish, Confirmed(new { type = "writer", id = writer }, TokenOf(plan)));
        var notThere = await CallAsync(client, McpContentTools.Schedule, new { type = "writer", id = Guid.NewGuid(), action = "unpublish", at = "2999-03-01T09:00:00Z" });
        var untouched = await OkAsync(client, "writer_get", new { id = writer });
        var scheduled = await OkAsync(client, McpContentTools.Schedule, Confirmed(arguments, TokenOf(plan)));
        var after = await OkAsync(client, "writer_get", new { id = writer });

        plan.GetProperty("dryRun").GetBoolean().ShouldBeTrue();
        plan.GetProperty("type").GetString().ShouldBe("writer");
        plan.GetProperty("action").GetString().ShouldBe("unpublish");
        plan.GetProperty("at").GetString().ShouldBe("2999-03-01T09:00:00Z");
        plan.TryGetProperty("timeZone", out _).ShouldBeFalse();
        plan.GetProperty("entries").EnumerateArray().ShouldHaveSingleItem().GetProperty("status").GetString().ShouldBe("published");
        plan.GetProperty("references").EnumerateArray().ShouldHaveSingleItem().GetProperty("id").GetGuid().ShouldBe(post);
        asked.GetProperty("dryRun").GetBoolean().ShouldBeTrue();
        Code(missing).ShouldBe("FCMS1630");
        Code(forged).ShouldBe("FCMS1631");
        Code(ofAnotherTime).ShouldBe("FCMS1631");
        Code(ofAnUnpublish).ShouldBe("FCMS1631");
        Code(onAnUnpublish).ShouldBe("FCMS1631");
        Code(notThere).ShouldBe("FCMS3104");
        untouched.GetProperty("status").GetString().ShouldBe("published");
        untouched.GetProperty("scheduledUnpublishAt").ValueKind.ShouldBe(JsonValueKind.Null);
        scheduled.GetProperty("action").GetString().ShouldBe("unpublish");
        scheduled.GetProperty("runAt").GetString().ShouldBe("2999-03-01T09:00:00Z");
        after.GetProperty("status").GetString().ShouldBe("published");
        after.GetProperty("scheduledUnpublishAt").GetString().ShouldBe("2999-03-01T09:00:00Z");
    }

    // A scheduled publish takes nothing from a reader, so it runs at once, and a dry run it is asked for is refused
    // rather than read as a run.
    [Fact]
    public async Task AScheduledPublishRunsAtOnceAndTakesNoDryRun()
    {
        using var http = await KeyAsync(fixture.Host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        var id = await PostAsync(client, "Goes live later");
        var arguments = new { type = "post", id, action = "publish", at = "2999-03-01T09:00:00Z" };

        var withDryRun = await CallAsync(client, McpContentTools.Schedule, new { type = "post", id, action = "publish", at = "2999-03-01T09:00:00Z", dryRun = true });
        var withToken = await CallAsync(client, McpContentTools.Schedule, Confirmed(arguments, "v1.confirmed"));
        var before = await OkAsync(client, "post_get", new { id });
        var scheduled = await OkAsync(client, McpContentTools.Schedule, arguments);
        var after = await OkAsync(client, "post_get", new { id });

        Code(withDryRun).ShouldBe("FCMS2108");
        Text(withDryRun).ShouldContain("- dryRun: ");
        Code(withToken).ShouldBe("FCMS2108");
        before.GetProperty("scheduledPublishAt").ValueKind.ShouldBe(JsonValueKind.Null);
        scheduled.GetProperty("action").GetString().ShouldBe("publish");
        scheduled.GetProperty("runAt").GetString().ShouldBe("2999-03-01T09:00:00Z");
        scheduled.TryGetProperty("dryRun", out _).ShouldBeFalse();
        after.GetProperty("scheduledPublishAt").GetString().ShouldBe("2999-03-01T09:00:00Z");
    }

    private static async Task<Guid> PostAsync(McpClient client, string title) =>
        (await OkAsync(client, "post_create_draft", new { fields = new { title } })).GetProperty("id").GetGuid();
}
