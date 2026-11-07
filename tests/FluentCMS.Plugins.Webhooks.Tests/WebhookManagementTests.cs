using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Webhooks.Tests.WebhookTesting;

namespace FluentCMS.Plugins.Webhooks.Tests;

public sealed class WebhookManagementTests
{
    private const string Root = "/api/management/v1/webhooks";

    [Fact]
    public async Task AWebhookIsCreatedReadChangedAndDeleted()
    {
        await using var host = Host();
        using var client = await AdminAsync(host);

        var created = await CreateAsync(client, new { name = "builds", url = "https://example.com/hooks", events = new List<string> { "content.published", "cache.invalidated" }, headers = new Dictionary<string, string> { ["X-Build"] = "site" } });
        var id = Id(created);
        created["secret"]!.GetValue<string>().ShouldStartWith("whsec_");
        created["enabled"]!.GetValue<bool>().ShouldBeTrue();
        created["siteId"]!.GetValue<string>().ShouldBe(Core.Sites.DefaultSite.Id.ToString("D"));

        using var read = await client.GetAsync(new Uri($"{Root}/{id:D}", UriKind.Relative), Token);
        using var list = await client.GetAsync(new Uri(Root, UriKind.Relative), Token);
        using var changed = await client.PutAsJsonAsync(new Uri($"{Root}/{id:D}", UriKind.Relative), new { name = "deploys", url = "https://example.com/deploy", events = new List<string> { "content.unpublished" }, types = new List<string> { "article" }, enabled = false }, Token);
        using var deleted = await client.DeleteAsync(new Uri($"{Root}/{id:D}", UriKind.Relative), Token);
        using var gone = await client.GetAsync(new Uri($"{Root}/{id:D}", UriKind.Relative), Token);

        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        var stored = JsonNode.Parse(await TextAsync(read))!;
        stored["name"]!.GetValue<string>().ShouldBe("builds");
        stored["headers"]!["X-Build"]!.GetValue<string>().ShouldBe(WebhookService.RedactedValue);
        stored.AsObject().ContainsKey("secret").ShouldBeFalse();
        JsonNode.Parse(await TextAsync(list))!["items"]!.AsArray().ShouldHaveSingleItem()!["id"]!.GetValue<string>().ShouldBe(id.ToString("D"));
        changed.StatusCode.ShouldBe(HttpStatusCode.OK, await TextAsync(changed));
        var update = JsonNode.Parse(await TextAsync(changed))!;
        (update["name"]!.GetValue<string>(), update["enabled"]!.GetValue<bool>(), update["types"]!.AsArray().Single()!.GetValue<string>()).ShouldBe(("deploys", false, "article"));
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(gone)).ShouldBe(WebhookErrorCodes.NotFound);
    }

    [Fact]
    public async Task TheSecretIsInNoResponseAfterCreateAndInNoEventActivityRowOrLogLine()
    {
        const string secret = "s3cr3t-value-that-must-not-leak";
        const string rotated = "r0tated-value-that-must-not-leak";
        var logs = new LogCapture();
        var directory = Directory.CreateTempSubdirectory("fluentcms-webhooks-");
        try
        {
            string[] bodies;
            await using (var host = Host(logs: logs).UseSqlite(Path.Combine(directory.FullName, "cms.db")))
            {
                using var client = await AdminAsync(host);
                var created = await CreateAsync(client, new { name = "builds", url = "https://example.com/hooks", events = new List<string> { "content.published" }, secret });
                var id = Id(created);
                using var read = await client.GetAsync(new Uri($"{Root}/{id:D}", UriKind.Relative), Token);
                using var list = await client.GetAsync(new Uri(Root, UriKind.Relative), Token);
                using var changed = await client.PutAsJsonAsync(new Uri($"{Root}/{id:D}", UriKind.Relative), new { name = "builds", url = "https://example.com/hooks", events = new List<string> { "content.published" }, secret = rotated }, Token);
                using var invalid = await client.PutAsJsonAsync(new Uri($"{Root}/{id:D}", UriKind.Relative), new { name = "", url = "https://example.com/hooks", events = new List<string> { "content.published" }, secret = "short" }, Token);
                await PumpAsync(host);
                using var activity = await client.GetAsync(new Uri("/api/management/v1/activity", UriKind.Relative), Token);
                bodies = [created.ToJsonString(), await TextAsync(read), await TextAsync(list), await TextAsync(changed), await TextAsync(invalid), await TextAsync(activity)];

                created.AsObject().ContainsKey("secret").ShouldBeFalse("a secret the caller gave is not returned");
                changed.StatusCode.ShouldBe(HttpStatusCode.OK, "a webhook keeps its own name");
                invalid.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
                var actions = JsonNode.Parse(await TextAsync(activity))!["items"]!.AsArray().Select(item => item!["action"]!.GetValue<string>()).ToList();
                actions.ShouldContain("webhook.created");
                actions.ShouldContain("webhook.updated");

                await using var scope = host.Services.CreateAsyncScope();
                var row = await scope.ServiceProvider.GetRequiredService<PluginDbContext<WebhooksPlugin>>().Set<Webhook>().FindAsync([id], Token);
                scope.ServiceProvider.GetRequiredService<WebhookSigning>().Unprotect(row!.Secret).ShouldBe(rotated);
            }

            foreach (var body in bodies)
            {
                body.ShouldNotContain(secret);
                body.ShouldNotContain(rotated);
            }

            logs.Lines.ShouldNotBeEmpty();
            logs.Lines.ShouldAllBe(line => !line.Contains(secret) && !line.Contains(rotated));

            // The outbox, the activity log and the job table are in the database file, where only the protected form is.
            foreach (var file in directory.EnumerateFiles())
            {
                var bytes = await File.ReadAllBytesAsync(file.FullName, Token);
                bytes.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(secret)).ShouldBe(-1, file.Name);
                bytes.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(rotated)).ShouldBe(-1, file.Name);
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task TheEndpointsNeedWebhooksManageForTheSite()
    {
        await using var host = Host();
        using var admin = await AdminAsync(host);
        await host.CreateRoleAsync("hook-keeper", [WebhookPermissions.Manage], Token);
        await host.CreateRoleAsync("reader", ["content.article.read"], Token);
        await CreateRoleAsync(host, "elsewhere", WebhookPermissions.Manage, new SiteId(Guid.CreateVersion7()));
        using var keeper = host.AsKey(await host.CreateApiKeyAsync(ApiKeyClass.Management, "hook-keeper", cancellationToken: Token));
        using var reader = host.AsKey(await host.CreateApiKeyAsync(ApiKeyClass.Management, "reader", cancellationToken: Token));
        using var elsewhere = host.AsKey(await host.CreateApiKeyAsync(ApiKeyClass.Management, "elsewhere", cancellationToken: Token));
        var webhook = await CreateAsync(admin, new { name = "builds", url = "https://example.com/hooks", events = new List<string> { "content.published" } });
        var root = new Uri(Root, UriKind.Relative);
        var ping = new Uri($"{Root}/{Id(webhook):D}/ping", UriKind.Relative);

        using var anonymous = await host.CreateClient().GetAsync(root, Token);
        using var refused = await reader.GetAsync(root, Token);
        using var otherSite = await elsewhere.GetAsync(root, Token);
        using var otherSitePing = await elsewhere.PostAsync(ping, null, Token);
        using var allowed = await keeper.GetAsync(root, Token);
        using var allowedPing = await keeper.PostAsync(ping, null, Token);
        using var permissions = await admin.GetAsync(new Uri("/api/management/v1/permissions", UriKind.Relative), Token);

        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TextAsync(refused)).ShouldContain(WebhookPermissions.Manage);
        otherSite.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        otherSitePing.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, await TextAsync(allowed));
        allowedPing.StatusCode.ShouldBe(HttpStatusCode.Accepted, await TextAsync(allowedPing));
        var entry = JsonNode.Parse(await TextAsync(permissions))!["items"]!.AsArray().Single(item => item!["name"]!.GetValue<string>() == WebhookPermissions.Manage)!;
        entry["source"]!.GetValue<string>().ShouldBe("plugin");
    }

    [Fact]
    public async Task AWebhookThatCannotBeSavedIsRefusedWithItsCodes()
    {
        await using var host = Host();
        using var client = await AdminAsync(host);
        await CreateAsync(client, new { name = "builds", url = "https://example.com/hooks", events = new List<string> { "content.published" } });

        var empty = await RefusalAsync(client, new { });
        var wrong = await RefusalAsync(client, new
        {
            name = "wrong",
            url = "ftp://example.com/hooks",
            events = new List<string> { "content.published", "content.exploded" },
            types = new List<string> { "nothing" },
            headers = new Dictionary<string, string> { ["Content-Type"] = "text/plain", ["X-FluentCMS-Event"] = "forged", ["Bad Name"] = "x" },
            secret = "short",
        });
        using var taken = await client.PostAsJsonAsync(new Uri(Root, UriKind.Relative), new { name = "builds", url = "https://example.com/other", events = new List<string> { "content.published" } }, Token);

        List<(string, string)> missing = [("name", WebhookErrorCodes.InvalidField), ("url", WebhookErrorCodes.InvalidUrl), ("events", WebhookErrorCodes.UnknownEvent)];
        List<(string, string)> refused =
            [
                ("url", WebhookErrorCodes.InvalidUrl),
                ("events[1]", WebhookErrorCodes.UnknownEvent),
                ("types[0]", WebhookErrorCodes.InvalidField),
                ("headers.Content-Type", WebhookErrorCodes.InvalidField),
                ("headers.X-FluentCMS-Event", WebhookErrorCodes.InvalidField),
                ("headers.Bad Name", WebhookErrorCodes.InvalidField),
                ("secret", WebhookErrorCodes.InvalidField),
            ];
        empty.ShouldBe(missing, ignoreOrder: true);
        wrong.ShouldBe(refused, ignoreOrder: true);
        taken.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(taken)).ShouldBe(WebhookErrorCodes.NameTaken);
    }

    [Fact]
    public async Task TheStreamsMigrationAppliesAndMatchesTheModel()
    {
        await using var host = Host();

        await using var scope = host.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<WebhooksPlugin>>();
        var applied = await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GetAppliedMigrationsAsync(database.Database, Token);
        applied.Select(migration => migration[15..]).ShouldBe(["InitialWebhooks", "AddDeliveryClaim", "AddPreviousSecret"]);
        Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.HasPendingModelChanges(database.Database).ShouldBeFalse();
        using var design = new WebhooksDbContextFactory().CreateDbContext([]);
        Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.HasPendingModelChanges(design.Database).ShouldBeFalse();
    }

    private static async Task<List<(string Path, string Code)>> RefusalAsync(HttpClient client, object webhook)
    {
        using var response = await client.PostAsJsonAsync(new Uri(Root, UriKind.Relative), webhook, Token);
        var text = await TextAsync(response);
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, text);
        return [.. JsonNode.Parse(text)!["errors"]!.AsArray().Select(error => (error!["path"]!.GetValue<string>(), error["code"]!.GetValue<string>()))];
    }

    private static async Task CreateRoleAsync(FluentCmsTestHost host, string name, string permission, SiteId site)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        var role = new Role { Id = Guid.CreateVersion7(), Name = name };
        context.Set<Role>().Add(role);
        context.Set<RoleGrant>().Add(new RoleGrant { Id = Guid.CreateVersion7(), RoleId = role.Id, Permission = permission, Constraints = new PermissionConstraints { Sites = [site] }.ToJson() });
        await context.SaveChangesAsync(Token);
    }
}
