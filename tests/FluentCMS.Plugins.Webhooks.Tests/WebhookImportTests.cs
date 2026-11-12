using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using FluentCMS.Core.Authorization;
using FluentCMS.Testing;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Webhooks.Tests.WebhookTesting;

namespace FluentCMS.Plugins.Webhooks.Tests;

// What an import writes carries Origin.Import, and is delivered only when the import asks for webhooks (spec 4.11).
public sealed class WebhookImportTests
{
    [Fact]
    public async Task AnImportDeliversWebhooksOnlyWhenItAsksForThemAndItsEventsAndActivityHaveTheImportOrigin()
    {
        await using var receiver = await TestReceiver.StartAsync();
        await using var source = Host();
        await using var quiet = Host();
        await using var loud = Host();
        using var sourceAdmin = await AdminAsync(source);
        var user = await source.CreateUserAsync(BuiltInRoles.SuperAdminName, "editor", Token);
        var articles = source.As(user).Content<Article>();
        var article = await articles.CreateAsync(new Article { Title = "Hello" }, Token);
        await articles.PublishAsync(article.Id, cancellationToken: Token);
        var package = await ExportAsync(sourceAdmin);

        using var quietAdmin = await AdminAsync(quiet);
        await CreateAsync(quietAdmin, new { name = "all", url = receiver.Url().ToString(), events = new List<string> { "content.published", "content.draft_saved" } });
        await ImportAsync(quietAdmin, package, "");
        await PumpAsync(quiet);
        receiver.Requests.ShouldBeEmpty();

        using var loudAdmin = await AdminAsync(loud);
        await CreateAsync(loudAdmin, new { name = "all", url = receiver.Url().ToString(), events = new List<string> { "content.published", "content.draft_saved" } });
        await ImportAsync(loudAdmin, package, "?webhooks=true");
        await PumpAsync(loud);
        receiver.Requests.ShouldNotBeEmpty();
        receiver.Requests.ShouldAllBe(request => JsonNode.Parse(request.Body)!["origin"]!.GetValue<string>() == "import");
        receiver.Requests.Select(request => request.Headers[WebhookSender.EventHeader]).ShouldContain("content.published");

        var activity = JsonNode.Parse(await quietAdmin.GetStringAsync(new Uri("/api/management/v1/activity?pageSize=100", UriKind.Relative), Token))!["items"]!.AsArray();
        var actions = activity.Select(item => (Action: item!["action"]!.GetValue<string>(), Origin: item["origin"]!.GetValue<string>())).ToList();
        actions.ShouldContain(("transfer.imported", "import"));
        actions.ShouldContain(("content.published", "import"));
        actions.Where(item => item.Action.StartsWith("content.", StringComparison.Ordinal)).ShouldAllBe(item => item.Origin == "import");
    }

    [Fact]
    public void OnlyAnImportThatAskedForWebhooksHasEventsTheDispatcherDelivers()
    {
        var at = DateTimeOffset.UnixEpoch;
        WebhookDispatcher.Delivers(new Events.EventSource(at, Guid.Empty, Guid.Empty, "", Content.PrincipalKind.System, Content.Origin.Import, "import:abc")).ShouldBeFalse();
        WebhookDispatcher.Delivers(new Events.EventSource(at, Guid.Empty, Guid.Empty, "", Content.PrincipalKind.System, Content.Origin.Import, "import+webhooks:abc")).ShouldBeTrue();
        WebhookDispatcher.Delivers(new Events.EventSource(at, Guid.Empty, Guid.Empty, "", Content.PrincipalKind.AdminUser, Content.Origin.Admin, "import:abc")).ShouldBeTrue();
    }

    private static async Task<byte[]> ExportAsync(HttpClient client)
    {
        using var response = await client.PostAsync(new Uri("/api/management/v1/transfer/export", UriKind.Relative), new StringContent("{}", Encoding.UTF8, "application/json"), Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return await response.Content.ReadAsByteArrayAsync(Token);
    }

    private static async Task ImportAsync(HttpClient client, byte[] package, string query)
    {
        using var body = new ByteArrayContent(package);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        using var response = await client.PostAsync(new Uri("/api/management/v1/transfer/import" + query, UriKind.Relative), body, Token);
        var text = await response.Content.ReadAsStringAsync(Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        JsonNode.Parse(text.Split('\n', StringSplitOptions.RemoveEmptyEntries)[^1])!["report"]!["total"]!["errors"]!.GetValue<int>().ShouldBe(0, text);
    }
}
