using System.Net;
using System.Net.Http.Headers;
using FluentCMS.Activity;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Events;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Management;

// Jobs, failed event deliveries, the activity log and /me over HTTP on SQLite (#2786), and the refusal of every group to a
// caller without its permission.
public sealed class OperationsEndpointsTests(AdminFixture fixture) : IClassFixture<AdminFixture>
{
    [Fact]
    public async Task ADeadLetteredJobIsListedAndRetried()
    {
        var id = (await fixture.EnqueueAsync(new FailingPayload("boom"))).ToString("D");
        await fixture.RunDueJobsAsync();

        (await fixture.GetAsync("jobs/dead-letter")).Json["items"]!.AsArray().ShouldContain(item => item!["id"]!.GetValue<string>() == id);
        var listed = await fixture.GetAsync($"jobs?status=dead_lettered&type={FailingJob.Type}");
        listed.Json["items"]!.AsArray().ShouldContain(item => item!["id"]!.GetValue<string>() == id);

        var job = await fixture.GetAsync($"jobs/{id}");
        job.Json["status"]!.GetValue<string>().ShouldBe("dead_lettered");
        job.Json["history"]!.AsArray().ShouldHaveSingleItem()!["outcome"]!.GetValue<string>().ShouldBe("failed");
        job.Json.AsObject().ContainsKey("payload").ShouldBeFalse();

        var retried = await fixture.PostAsync($"jobs/{id}/retry");
        retried.Status.ShouldBe(HttpStatusCode.OK, retried.Body?.ToJsonString());
        retried.Json["status"]!.GetValue<string>().ShouldBe("pending");
        retried.Json["maxAttempts"]!.GetValue<int>().ShouldBeGreaterThan(1);
        (await fixture.PostAsync($"jobs/{id}/retry")).Json["code"]!.GetValue<string>().ShouldBe("FCMS3133");
        (await fixture.GetAsync($"jobs/{Guid.NewGuid():D}")).Json["code"]!.GetValue<string>().ShouldBe("FCMS3132");
        (await fixture.GetAsync("jobs?status=finished")).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await fixture.GetAsync("jobs?page=2147483647")).Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task APendingJobIsCancelledIntoTheDeadLetterList()
    {
        var id = (await fixture.EnqueueAsync(new FailingPayload("later"), DateTimeOffset.UtcNow.AddDays(1))).ToString("D");

        var cancelled = await fixture.PostAsync($"jobs/{id}/cancel");
        cancelled.Status.ShouldBe(HttpStatusCode.OK, cancelled.Body?.ToJsonString());
        cancelled.Json["status"]!.GetValue<string>().ShouldBe("dead_lettered");
        cancelled.Json["lastError"]!.GetValue<string>().ShouldContain("cancelled");

        (await fixture.PostAsync($"jobs/{id}/cancel")).Json["code"]!.GetValue<string>().ShouldBe("FCMS3134");
        (await fixture.GetAsync("jobs/dead-letter")).Json["items"]!.AsArray().ShouldContain(item => item!["id"]!.GetValue<string>() == id);
    }

    [Fact]
    public async Task AFailedEventDeliveryIsListedRetriedAndRemovedWithAnActivityRowForEach()
    {
        var handler = $"acme.{Guid.NewGuid():N}";
        var retried = await FailedDeliveryAsync(handler);
        var removed = await FailedDeliveryAsync(handler);

        var listed = await fixture.GetAsync("events/dead-letter?pageSize=100");
        listed.Status.ShouldBe(HttpStatusCode.OK, listed.Body?.ToJsonString());
        var item = listed.Json["items"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == retried.ToString("D"))!;
        item["handler"]!.GetValue<string>().ShouldBe(handler);
        item["lastError"]!.GetValue<string>().ShouldBe("Down.");
        item.AsObject().ContainsKey("payload").ShouldBeFalse();

        (await fixture.PostAsync($"events/dead-letter/{retried:D}/retry")).Status.ShouldBe(HttpStatusCode.NoContent);
        (await fixture.PostAsync($"events/dead-letter/{retried:D}/retry")).Json["code"]!.GetValue<string>().ShouldBe("FCMS3139");
        (await fixture.DeleteAsync($"events/dead-letter/{retried:D}")).Json["code"]!.GetValue<string>().ShouldBe("FCMS3139");
        (await fixture.DeleteAsync($"events/dead-letter/{removed:D}")).Status.ShouldBe(HttpStatusCode.NoContent);
        var gone = await fixture.DeleteAsync($"events/dead-letter/{removed:D}");
        gone.Status.ShouldBe(HttpStatusCode.NotFound);
        gone.Json["code"]!.GetValue<string>().ShouldBe("FCMS3138");
        (await fixture.PostAsync($"events/dead-letter/{Guid.NewGuid():D}/retry")).Json["code"]!.GetValue<string>().ShouldBe("FCMS3138");

        var activity = await fixture.GetAsync($"activity?action=event_delivery.*&targetType=event_delivery");
        var rows = activity.Json["items"]!.AsArray().Where(row => row!["targetLabel"]!.GetValue<string>() == handler).ToList();
        rows.Select(row => (row!["action"]!.GetValue<string>(), row["targetId"]!.GetValue<string>()))
            .ShouldBe([("event_delivery.retried", retried.ToString("D")), ("event_delivery.removed", removed.ToString("D"))], ignoreOrder: true);
    }

    // For the failed deliveries of a plugin that was removed: one request, and one activity row with the count.
    [Fact]
    public async Task TheFailedDeliveriesOfOneHandlerArePurgedTogether()
    {
        var handler = $"gone.{Guid.NewGuid():N}";
        var other = $"kept.{Guid.NewGuid():N}";
        await FailedDeliveryAsync(handler);
        await FailedDeliveryAsync(handler);
        var kept = await FailedDeliveryAsync(other);

        var purged = await fixture.DeleteAsync($"events/dead-letter?handler={handler}");
        purged.Status.ShouldBe(HttpStatusCode.OK, purged.Body?.ToJsonString());
        purged.Json["handler"]!.GetValue<string>().ShouldBe(handler);
        purged.Json["removed"]!.GetValue<int>().ShouldBe(2);
        (await fixture.DeleteAsync($"events/dead-letter?handler={handler}")).Json["removed"]!.GetValue<int>().ShouldBe(0);

        var missing = await fixture.DeleteAsync("events/dead-letter");
        missing.Status.ShouldBe(HttpStatusCode.UnprocessableEntity, missing.Body?.ToJsonString());
        missing.Json["code"]!.GetValue<string>().ShouldBe("FCMS2101");

        (await fixture.GetAsync("events/dead-letter?pageSize=100")).Json["items"]!.AsArray().ShouldContain(item => item!["id"]!.GetValue<string>() == kept.ToString("D"));
        var activity = await fixture.GetAsync($"activity?action=event_delivery.purged&targetId={handler}");
        var row = activity.Json["items"]!.AsArray().ShouldHaveSingleItem()!;
        row["targetType"]!.GetValue<string>().ShouldBe("event_handler");
        row["details"]!["removed"]!.GetValue<string>().ShouldBe("2");
    }

    [Fact]
    public async Task TheRowsOfNoSiteAreReadOnlyWithAnInstanceWidePermission()
    {
        var marker = Guid.NewGuid().ToString("N");
        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var log = scope.ServiceProvider.GetRequiredService<IActivityLog>();
            var source = EventSource.FromSystem(DateTimeOffset.UtcNow);
            await log.WriteAsync(new ActivityEntry("test.site_row") { TargetType = "test", TargetId = marker }, source with { SiteId = DefaultSite.Id }, TestContext.Current.CancellationToken);
            await log.WriteAsync(new ActivityEntry("test.instance_row") { TargetType = "test", TargetId = marker }, source, TestContext.Current.CancellationToken);
        }

        var everything = await fixture.GetAsync($"activity?targetId={marker}");
        everything.Json["items"]!.AsArray().Select(item => item!["action"]!.GetValue<string>()).ShouldBe(["test.site_row", "test.instance_row"], ignoreOrder: true);

        var siteRole = await fixture.CreateRoleAsync($$"""
            { "name": "Site auditor {{marker}}", "siteId": "{{DefaultSite.Id:D}}", "grants": [ { "permission": "activity.read" } ] }
            """);
        using var siteReader = await fixture.ClientForAsync((await fixture.GetAsync($"roles/{siteRole}")).Json["name"]!.GetValue<string>());
        var siteRows = await fixture.GetAsync($"activity?targetId={marker}", siteReader);
        siteRows.Status.ShouldBe(HttpStatusCode.OK, siteRows.Body?.ToJsonString());
        siteRows.Json["items"]!.AsArray().ShouldHaveSingleItem()!["action"]!.GetValue<string>().ShouldBe("test.site_row");

        var limited = await fixture.CreateRoleAsync($$"""
            { "name": "Limited auditor {{marker}}", "grants": [ { "permission": "activity.read", "constraints": { "sites": ["{{DefaultSite.Id:D}}"] } } ] }
            """);
        using var limitedReader = await fixture.ClientForAsync((await fixture.GetAsync($"roles/{limited}")).Json["name"]!.GetValue<string>());
        (await fixture.GetAsync($"activity?targetId={marker}", limitedReader)).Json["items"]!.AsArray().ShouldHaveSingleItem();

        using var instanceReader = await fixture.ClientWithAsync(CorePermissions.ActivityRead);
        (await fixture.GetAsync($"activity?targetId={marker}&action=test.*", instanceReader)).Json["items"]!.AsArray().Count.ShouldBe(2);
        (await fixture.GetAsync($"activity?targetId={marker}&from=2000-01-01T00:00:00Z&to=2001-01-01T00:00:00Z")).Json["items"]!.AsArray().ShouldBeEmpty();
        (await fixture.GetAsync($"activity?targetId={marker}&origin=sideways")).Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MeIsTheCallerWithItsRoles()
    {
        var me = await fixture.GetAsync("me");
        me.Status.ShouldBe(HttpStatusCode.OK, me.Body?.ToJsonString());
        me.Json["kind"]!.GetValue<string>().ShouldBe("adminUser");
        me.Json["roles"]!.AsArray().Select(role => role!["name"]!.GetValue<string>()).ShouldBe([BuiltInRoles.SuperAdminName]);
        me.Json["twoFactorEnabled"]!.GetValue<bool>().ShouldBeFalse();

        var key = await fixture.Host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.EditorName, cancellationToken: TestContext.Current.CancellationToken);
        using var client = fixture.Host.AsKey(key);
        var asKey = await fixture.GetAsync("me", client);
        asKey.Json["kind"]!.GetValue<string>().ShouldBe("apiKey");
        asKey.Json["id"]!.GetValue<string>().ShouldBe(key.Id.ToString("D"));
        (await fixture.GetAsync("me/sessions", client)).Json["items"]!.AsArray().ShouldBeEmpty();
    }

    // A user with a role in each of two sites reads both, the default site first, with no sites.manage in either, and
    // nothing of a third site. A key is held to the sites it is bound to, and a grant to the sites it is limited to.
    [Fact]
    public async Task MySitesListsOnlyTheSitesInWhichTheCallerHoldsAGrant()
    {
        var token = TestContext.Current.CancellationToken;
        Site first, second, third;
        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var sites = scope.ServiceProvider.GetRequiredService<SiteManager>();
            first = await sites.CreateAsync("First", $"a-{Guid.NewGuid():N}", "en", token);
            second = await sites.CreateAsync("Second", $"b-{Guid.NewGuid():N}", "fr", token);
            third = await sites.CreateAsync("Third", $"c-{Guid.NewGuid():N}", "en", token);
        }

        var all = await fixture.GetAsync("me/sites");
        all.Status.ShouldBe(HttpStatusCode.OK, all.Body?.ToJsonString());
        var listed = all.Json["items"]!.AsArray();
        listed[0]!.ToJsonString().ShouldBe($$"""{"id":"{{DefaultSite.Id}}","name":"{{DefaultSite.Name}}","slug":"{{DefaultSite.Slug}}","defaultLocale":"{{DefaultSite.Locale}}","isDefault":true}""");
        new[] { first.Id, second.Id, third.Id }.ShouldBeSubsetOf(Ids(all));

        var user = await fixture.Host.CreateUserAsync(await fixture.RoleWithAsync(), cancellationToken: token);
        foreach (var site in (Site[])[first, second])
        {
            var role = await fixture.PostAsync("roles", $$"""{ "name": "reader-{{Guid.NewGuid():N}}", "siteId": "{{site.Id}}", "grants": [ { "permission": "content.page.read" } ] }""");
            role.Status.ShouldBe(HttpStatusCode.Created, role.Body?.ToJsonString());
            (await fixture.PutAsync($"users/{user.Id}/roles/{role.Json["id"]!.GetValue<string>()}")).Status.ShouldBe(HttpStatusCode.OK);
        }

        using var reader = await fixture.Host.AsUserAsync(user);
        var mine = await fixture.GetAsync("me/sites", reader);
        mine.Status.ShouldBe(HttpStatusCode.OK, mine.Body?.ToJsonString());
        Ids(mine).ShouldBe([first.Id, second.Id]);
        mine.Json["items"]![1]!["defaultLocale"]!.GetValue<string>().ShouldBe("fr");
        (await fixture.GetAsync("sites", reader)).Status.ShouldBe(HttpStatusCode.Forbidden);

        var limited = await fixture.PostAsync("roles", $$"""{ "name": "limited-{{Guid.NewGuid():N}}", "grants": [ { "permission": "content.page.read", "constraints": { "sites": ["{{third.Id}}"] } } ] }""");
        limited.Status.ShouldBe(HttpStatusCode.Created, limited.Body?.ToJsonString());
        using var limitedKey = fixture.Host.AsKey(await fixture.Host.CreateApiKeyAsync(ApiKeyClass.Management, limited.Json["name"]!.GetValue<string>(), cancellationToken: token));
        Ids(await fixture.GetAsync("me/sites", limitedKey)).ShouldBe([third.Id]);

        var bound = await fixture.PostAsync("api-keys", $$"""{ "name": "bound-{{Guid.NewGuid():N}}", "class": "management", "roleId": "{{BuiltInRoles.Editor}}", "siteIds": ["{{second.Id}}"] }""");
        bound.Status.ShouldBe(HttpStatusCode.Created, bound.Body?.ToJsonString());
        using var boundKey = fixture.Host.CreateClient();
        boundKey.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", bound.Json["secret"]!.GetValue<string>());
        Ids(await fixture.GetAsync("me/sites", boundKey)).ShouldBe([second.Id]);

        using var anonymous = fixture.Host.CreateClient();
        (await fixture.GetAsync("me/sites", anonymous)).Status.ShouldBe(HttpStatusCode.Unauthorized);

        static List<Guid> Ids(ApiResponse response) => [.. response.Json["items"]!.AsArray().Select(site => Guid.Parse(site!["id"]!.GetValue<string>()))];
    }

    [Theory]
    [InlineData("users")]
    [InlineData("invitations")]
    [InlineData("roles")]
    [InlineData("permissions")]
    [InlineData("api-keys")]
    [InlineData("sites")]
    [InlineData("sites/00000000-0000-7000-8002-000000000001/locales")]
    [InlineData("settings/mail")]
    [InlineData("settings/unknown")]
    [InlineData("jobs")]
    [InlineData("jobs/dead-letter")]
    [InlineData("events/dead-letter")]
    [InlineData("activity")]
    public async Task EachGroupRefusesACallerWithoutItsPermission(string path)
    {
        using var reader = await fixture.ClientWithAsync(CorePermissions.SchemaRead, "content.*.read");
        var refused = await fixture.GetAsync(path, reader);
        refused.Status.ShouldBe(HttpStatusCode.Forbidden, refused.Body?.ToJsonString());
        refused.Json["code"]!.GetValue<string>().ShouldBe("FCMS3103");

        using var anonymous = fixture.Host.CreateClient();
        (await fixture.GetAsync(path, anonymous)).Status.ShouldBe(HttpStatusCode.Unauthorized);

        var deliveryKey = await fixture.Host.CreateApiKeyAsync(ApiKeyClass.Delivery, BuiltInRoles.SuperAdminName, cancellationToken: TestContext.Current.CancellationToken);
        using var delivery = fixture.Host.CreateClient();
        delivery.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", deliveryKey.Secret);
        (await fixture.GetAsync(path, delivery)).Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    // A Super Admin's key of the Delivery class and its token without the management scope hold system.read, so only the
    // scope refuses them, before the permission is looked at.
    [Theory]
    [InlineData("system/diagnostics")]
    [InlineData("system/health")]
    public async Task TheSystemGroupNeedsTheManagementScope(string path)
    {
        var deliveryKey = await fixture.Host.CreateApiKeyAsync(ApiKeyClass.Delivery, BuiltInRoles.SuperAdminName, cancellationToken: TestContext.Current.CancellationToken);
        using var delivery = fixture.Host.AsKey(deliveryKey);
        using var scopeless = await fixture.Host.AsUserAsync(await fixture.Host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: TestContext.Current.CancellationToken), "openid");

        foreach (var client in new[] { delivery, scopeless })
        {
            var refused = await fixture.GetAsync(path, client);
            refused.Status.ShouldBe(HttpStatusCode.Forbidden, refused.Body?.ToJsonString());
            refused.Json["code"]!.GetValue<string>().ShouldBe("FCMS3103");
            refused.Json["detail"]!.GetValue<string>().ShouldContain("management scope");
        }

        (await fixture.GetAsync("system/health")).Status.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task WritesAreRefusedWithoutThePermissionToo()
    {
        using var reader = await fixture.ClientWithAsync(CorePermissions.SchemaRead);
        var id = Guid.NewGuid().ToString("D");

        (await fixture.PostAsync("roles", """{ "name": "X", "grants": [] }""", reader)).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.PostAsync("invitations", """{ "email": "x@example.com" }""", reader)).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.PostAsync("api-keys", $$"""{ "name": "X", "class": "delivery", "roleId": "{{BuiltInRoles.Editor:D}}" }""", reader)).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.PatchAsync($"sites/{DefaultSite.Id:D}", """{ "name": "X" }""", reader)).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.PutAsync("settings/mail", """{ "host": "x", "port": 1 }""", reader, "\"0\"")).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.PostAsync($"jobs/{id}/retry", client: reader)).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.PostAsync($"events/dead-letter/{id}/retry", client: reader)).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.DeleteAsync($"events/dead-letter/{id}", reader)).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.DeleteAsync("events/dead-letter?handler=x", reader)).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.PostAsync($"users/{id}/deactivate", client: reader)).Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    private async Task<Guid> FailedDeliveryAsync(string handler)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        var row = new OutboxMessage
        {
            EventId = Guid.CreateVersion7(),
            EventType = "Acme.Thing",
            Handler = handler,
            Payload = "{}",
            OccurredAt = DateTimeOffset.UtcNow,
            Sequence = DateTimeOffset.UtcNow.Ticks,
            Status = OutboxStatus.Failed,
            Attempts = 8,
            AvailableAt = DateTimeOffset.UtcNow,
            LastError = "Down.",
        };
        context.Add(row);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return row.Id;
    }
}
