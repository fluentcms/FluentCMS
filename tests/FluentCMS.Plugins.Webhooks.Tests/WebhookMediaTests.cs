using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Sites;
using FluentCMS.Events;
using FluentCMS.Sites;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Webhooks.Tests.WebhookTesting;

namespace FluentCMS.Plugins.Webhooks.Tests;

// The media plugin's events are the SDK's, so a webhook subscribes to them whether or not the plugin is in the host.
public sealed class WebhookMediaTests
{
    [Fact]
    public async Task AMediaEventIsDeliveredWithTheAssetAndItsManagementLink()
    {
        await using var receiver = await TestReceiver.StartAsync();
        await using var host = Host();
        using var client = await AdminAsync(host);
        await CreateAsync(client, new { name = "media", url = receiver.Url().ToString(), events = new List<string> { "media.uploaded", "media.moved" }, types = new List<string> { "article" } });
        var asset = Guid.CreateVersion7();
        var folder = Guid.CreateVersion7();

        await using (var scope = host.Services.CreateAsyncScope())
        using (SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), Origin.Job, new SiteId(DefaultSite.Id)))
        {
            var events = scope.ServiceProvider.GetRequiredService<IEventBus>();
            await events.PublishAsync(new MediaAssetUploadedEvent(asset, folder, "photo.jpg", "image/jpeg", 10), Token);
            await events.PublishAsync(new MediaAssetDeletedEvent(asset, folder, "photo.jpg"), Token);
        }

        await PumpAsync(host);

        var request = receiver.Requests.ShouldHaveSingleItem();
        request.Headers[WebhookSender.EventHeader].ShouldBe("media.uploaded");
        var body = JsonNode.Parse(request.Body)!;
        (Guid.Parse(body["data"]!["assetId"]!.GetValue<string>()), Guid.Parse(body["data"]!["folderId"]!.GetValue<string>()), body["data"]!["fileName"]!.GetValue<string>())
            .ShouldBe((asset, folder, "photo.jpg"));
        body["links"]!["management"]!.GetValue<string>().ShouldBe($"api/management/v1/media/{asset:D}");
    }
}
