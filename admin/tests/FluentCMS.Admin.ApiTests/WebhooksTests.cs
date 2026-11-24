using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Sites;
using FluentCMS.Plugins.Webhooks.Admin;
using FluentCMS.Plugins.Webhooks.Admin.Api;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.ApiTests;

// The webhooks half's calls (spec 5.3) against the server half: its records held to the OpenAPI document, its list of events
// held to the one the server takes, a webhook made with the secret answered once, its stored header kept on the same host and
// asked for again on another, a rotation, a ping and the log, and the refusal of a caller without webhooks.manage. The
// addresses are under .invalid, which never resolves (RFC 6761), so that nothing is delivered anywhere.
public sealed class WebhooksTests
{
    private const string Prefix = "/api/management/v1/";

    private static CancellationToken Token => AdminHost.Token;

    [Fact]
    public async Task TheRecordsOfTheHalfMatchTheOpenApiDocument()
    {
        var document = await OpenApiDocument.ReadAsync();
        var paths = document["paths"]!;
        var records = WebhooksJsonContext.Default;
        List<string> gaps = [];

        void Check(System.Text.Json.Serialization.Metadata.JsonTypeInfo record, string path, string method, string status = "200") =>
            OpenApiContractTests.Check(record, OpenApiContractTests.Answer(paths, Prefix + path, method, status), $"{path} {method}", document, gaps);

        Check(records.ItemListWebhookRecord, "webhooks", "get");
        Check(records.WebhookRecord, "webhooks", "post", "201");
        Check(records.WebhookRecord, "webhooks/{id}", "get");
        Check(records.WebhookRecord, "webhooks/{id}", "put");
        Check(records.WebhookDeliveryPage, "webhooks/{id}/deliveries", "get");
        Check(records.WebhookDeliveryQueued, "webhooks/{id}/deliveries/{delivery}/redeliver", "post", "202");
        Check(records.WebhookDeliveryQueued, "webhooks/{id}/ping", "post", "202");

        gaps.ShouldBeEmpty();
        paths[Prefix + "webhooks/{id}"]?["delete"]?["responses"]?["204"].ShouldNotBeNull();
        string[] draft = ["name", "url", "events", "types", "headers", "enabled", "secret"];
        Body(document, "webhooks", "post").Select(member => member.Key).ShouldBe(draft, ignoreOrder: true);
        Body(document, "webhooks/{id}", "put").Select(member => member.Key).ShouldBe(draft, ignoreOrder: true);
        paths[Prefix + "webhooks/{id}/deliveries"]!["get"]!["parameters"]!.AsArray().Select(parameter => (string?)parameter!["name"]).OfType<string>().ShouldBe(["id", "page", "pageSize"], ignoreOrder: true);
    }

    // The server names the events it takes when none is given; the half's list is the same, and each of them is taken.
    [Fact]
    public async Task TheEventsOfTheHalfAreTheOnesTheServerTakes()
    {
        await using var admin = new AdminHost();
        using var client = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        var api = client.Api;

        var refused = await Should.ThrowAsync<ApiProblemException>(() => api.CreateWebhookAsync(Draft("None", []), Token));
        var named = WebhookProblems.FieldError(refused.Problem, "events").ShouldNotBeNull();
        named.Code.ShouldBe(WebhookProblems.UnknownEvent);
        named.Message!.Split(':', 2)[1].Trim().TrimEnd('.').Split(", ").ShouldBe(WebhookEvents.All, ignoreOrder: true);

        var every = await api.CreateWebhookAsync(Draft("Every event", [.. WebhookEvents.Groups.SelectMany(group => group.Names)]), Token);
        every.Events.ShouldBe([.. WebhookEvents.Groups.SelectMany(group => group.Names)]);
        WebhookProblems.Has(await Should.ThrowAsync<ApiProblemException>(() => api.CreateWebhookAsync(Draft("Ping", [WebhookEvents.Ping]), Token)), WebhookProblems.UnknownEvent).ShouldBeTrue();
    }

    [Fact]
    public async Task AWebhookIsMadeWithItsSecretOnceKeepsItsHeaderOnTheSameHostIsAskedForItOnAnotherAndIsPingedAndDeleted()
    {
        await using var admin = new AdminHost();
        using var client = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        var api = client.Api;
        var headers = new Dictionary<string, string> { ["Authorization"] = "Bearer receiver" };

        var created = await api.CreateWebhookAsync(Draft("Publisher", ["content.published"], headers: headers), Token);
        created.Secret.ShouldNotBeNull().ShouldStartWith("whsec_");
        created.Headers.ShouldBe(new Dictionary<string, string> { ["Authorization"] = WebhooksApi.RedactedValue });
        var read = await api.GetWebhookAsync(created.Id, Token);
        (read.Secret, read.PreviousSecretExpiresAt, read.Enabled).ShouldBe((null, null, true));
        (await api.GetWebhooksAsync(Token)).Select(webhook => webhook.Id).ShouldContain(created.Id);

        // Sent back redacted to a new path of the same host, the header keeps its value; to another host it needs it again.
        var kept = await api.UpdateWebhookAsync(created.Id, Draft("Publisher", ["content.published"], "https://receiver.invalid/other", Redacted(read)), Token);
        kept.Url.ShouldBe("https://receiver.invalid/other");
        var moved = await Should.ThrowAsync<ApiProblemException>(() => api.UpdateWebhookAsync(created.Id, Draft("Publisher", ["content.published"], "https://elsewhere.invalid/hooks", Redacted(read)), Token));
        var refusal = WebhookProblems.HeaderError(moved.Problem, "Authorization").ShouldNotBeNull();
        refusal.Code.ShouldBe(WebhookProblems.InvalidField);
        (await api.GetWebhookAsync(created.Id, Token)).Url.ShouldBe("https://receiver.invalid/other");
        (await api.UpdateWebhookAsync(created.Id, Draft("Publisher", ["content.published"], "https://elsewhere.invalid/hooks", headers), Token)).Url.ShouldBe("https://elsewhere.invalid/hooks");

        var disabled = await api.UpdateWebhookAsync(created.Id, WebhooksApi.Toggled(kept with { Url = "https://elsewhere.invalid/hooks" }, enabled: false), Token);
        (disabled.Enabled, disabled.Headers.Keys.Single()).ShouldBe((false, "Authorization"));
        var rotated = await api.UpdateWebhookAsync(created.Id, WebhooksApi.Toggled(disabled, enabled: true) with { Secret = "whsec_" + new string('a', 64) }, Token);
        (rotated.Enabled, rotated.Secret).ShouldBe((true, null));
        rotated.PreviousSecretExpiresAt.ShouldNotBeNull().ShouldBeGreaterThan(DateTimeOffset.UtcNow);

        (await api.PingAsync(created.Id, Token)).ShouldNotBe(Guid.Empty);
        var log = await api.GetDeliveriesAsync(created.Id, 1, Token);
        (log.Page, log.PageSize).ShouldBe((1, WebhooksApi.PageSize));
        WebhookProblems.Has(await Should.ThrowAsync<ApiProblemException>(() => api.RedeliverAsync(created.Id, Guid.NewGuid(), Token)), WebhookProblems.DeliveryNotFound).ShouldBeTrue();

        await api.DeleteWebhookAsync(created.Id, Token);
        WebhookProblems.IsGone(await Should.ThrowAsync<ApiProblemException>(() => api.GetWebhookAsync(created.Id, Token))).ShouldBeTrue();
        WebhookProblems.Has(await Should.ThrowAsync<ApiProblemException>(() => api.CreateWebhookAsync(Draft("Plain", ["content.published"], "ftp://receiver.invalid/hooks"), Token)), WebhookProblems.InvalidUrl).ShouldBeTrue();
    }

    [Fact]
    public async Task ACallerWithoutWebhooksManageIsRefusedTheCallsOfTheHalf()
    {
        await using var admin = new AdminHost();
        using var super = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        var role = new JsonObject
        {
            ["name"] = "Site manager",
            ["siteId"] = DefaultSite.Id,
            ["grants"] = new JsonArray(new JsonObject { ["permission"] = CorePermissions.SitesManage }),
        };
        await super.Api.SendAsync(new ApiRequest(HttpMethod.Post, "roles") { Body = role }, Token);
        using var client = await admin.SignInAsync("Site manager");

        WebhookProblems.IsForbidden(await Should.ThrowAsync<ApiProblemException>(() => client.Api.GetWebhooksAsync(Token))).ShouldBeTrue();
        WebhookProblems.IsForbidden(await Should.ThrowAsync<ApiProblemException>(() => client.Api.CreateWebhookAsync(Draft("Publisher", ["content.published"]), Token))).ShouldBeTrue();
    }

    private static WebhookDraft Draft(string name, string[] events, string url = "https://receiver.invalid/hooks", IReadOnlyDictionary<string, string>? headers = null) =>
        new(name, url, events, [], headers ?? new Dictionary<string, string>(), Enabled: true);

    private static Dictionary<string, string> Redacted(WebhookRecord webhook) => webhook.Headers.ToDictionary(header => header.Key, _ => WebhooksApi.RedactedValue);

    private static JsonObject Body(JsonObject document, string path, string method)
    {
        var schema = document["paths"]![Prefix + path]![method]!["requestBody"]!["content"]!["application/json"]!["schema"]!;
        var reference = schema["$ref"]?.GetValue<string>();
        var resolved = reference is null ? schema : document["components"]!["schemas"]![reference.Split('/')[^1]]!;
        return resolved["properties"]!.AsObject();
    }
}
