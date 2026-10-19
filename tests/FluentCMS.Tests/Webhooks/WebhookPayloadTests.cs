using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Webhooks;

namespace FluentCMS.Tests.Webhooks;

public sealed class WebhookPayloadTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 19, 9, 30, 15, TimeSpan.Zero);

    [Fact]
    public void The_signature_is_hmac_sha256_of_id_timestamp_and_body_with_the_secrets_bytes()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var secret = WebhookSecrets.Prefix + Convert.ToBase64String(key);

        var signature = WebhookSignatures.Sign(secret, "msg_1", At, """{"type":"webhook.test"}""");

        var expected = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes("""msg_1.1792402215.{"type":"webhook.test"}"""));
        Assert.Equal("v1," + Convert.ToBase64String(expected), signature);
        Assert.Equal("1792402215", WebhookSignatures.Timestamp(At));
        Assert.Throws<ArgumentException>(() => WebhookSignatures.Sign("not-a-secret", "msg_1", At, "{}"));
    }

    [Fact]
    public void A_publish_names_the_set_its_items_and_a_link_and_a_revert_names_the_set_it_reverted()
    {
        var published = Published("Sync prices from the catalog export.");
        var reverted = Published("The first sync.");

        using var publish = JsonDocument.Parse(WebhookPayloads.ForPublish("msg_1", At, published, null, "https://cms.example.com/admin/"));
        using var revert = JsonDocument.Parse(WebhookPayloads.ForPublish("msg_2", At, published, reverted, null));

        Assert.Equal("change_set.published", publish.RootElement.GetProperty("type").GetString());
        Assert.Equal("msg_1", publish.RootElement.GetProperty("id").GetString());
        Assert.Equal("2026-10-19T09:30:15.000Z", publish.RootElement.GetProperty("timestamp").GetString());
        var set = publish.RootElement.GetProperty("data").GetProperty("changeSet");
        Assert.Equal(("Sync prices from the catalog export.", $"https://cms.example.com/admin/sets/{published.Id}"), (set.GetProperty("rationale").GetString(), set.GetProperty("link").GetString()));
        Assert.Equal(["exports/catalog.csv"], set.GetProperty("sources").EnumerateArray().Select(source => source.GetString()));
        var item = Assert.Single(publish.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
        Assert.Equal(("entry", "update", 3), (item.GetProperty("kind").GetString(), item.GetProperty("operation").GetString(), item.GetProperty("version").GetInt32()));

        Assert.Equal("change_set.reverted", revert.RootElement.GetProperty("type").GetString());
        Assert.Equal(reverted.Id.ToString(), revert.RootElement.GetProperty("data").GetProperty("reverted").GetProperty("id").GetString());
        Assert.Equal($"/sets/{published.Id}", revert.RootElement.GetProperty("data").GetProperty("changeSet").GetProperty("link").GetString());
    }

    private static ChangeSet Published(string rationale)
    {
        var set = ChangeSet.Open(Site.Default.Id, rationale, ["exports/catalog.csv"], new UserInitiator(Guid.NewGuid(), "Amir")).Value;
        Assert.True(set.AddOperation(ChangeOperation.Create(OperationKind.Update, ItemKind.Entry, Guid.NewGuid(), 2, 3).Value).IsSuccess);
        return set;
    }
}
