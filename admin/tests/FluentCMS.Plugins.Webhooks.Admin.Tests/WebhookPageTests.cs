using Bunit;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Webhooks.Admin.Tests;

// The delivery log of a webhook: each attempt with its state, status, duration, body and answer, in pages; a failed delivery
// sent again and a ping, whose results the page follows and says.
public sealed class WebhookPageTests : WebhooksTestContext
{
    [Fact]
    public void ADeliveryShowsItsStatusBodyAndAnswerAndAFailedOneIsSentAgain()
    {
        var id = Server.Add("Receiver", "https://hooks.example.com/in", ["content.published"]);
        var failed = Server.AddDelivery(id, "content.published", "dead", 500, response: "Receiver broke");
        var deliveryId = failed["deliveryId"]!.GetValue<Guid>().ToString("D");
        var cut = Render<WebhookPage>(parameters => parameters.Add(page => page.Id, id));
        cut.WaitForAssertion(() => cut.Find("h1").TextContent.ShouldBe("Receiver"));

        Cells(cut, 0).ShouldBe(["content.published", "1", "Failed", "500", "42 ms"]);
        cut.Find("tr.webhooks-attempt th").TextContent.ShouldBe(new UtcFormats().FormatDateTime(DateTimeOffset.Parse(failed["createdAt"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture)));
        Button(cut, "Details of content.published, attempt 1").Click();
        cut.WaitForAssertion(() => cut.Find(".webhooks-attempt-details").TextContent.ShouldContain("Receiver broke"));
        cut.Find(".webhooks-body").TextContent.ShouldContain("\"event\": \"content.published\"");
        cut.Find(".webhooks-attempt-details").TextContent.ShouldContain(deliveryId);
        cut.Find("fluent-button[aria-expanded]").GetAttribute("aria-expanded").ShouldBe("true");

        Button(cut, "Send again of content.published, attempt 1").Click();

        cut.WaitForAssertion(() => cut.Find(".webhooks-message").TextContent.ShouldBe("The delivery was answered 200 in 42 ms."));
        Server.Requests.ShouldContain(request => request.Method == HttpMethod.Post && request.Path == $"webhooks/{id:D}/deliveries/{deliveryId}/redeliver");
        cut.FindAll("tr.webhooks-attempt").Count.ShouldBe(2);
        Cells(cut, 0).ShouldBe(["content.published", "1", "Delivered", "200", "42 ms"]);
        Announcer.Messages.ShouldBe(["The delivery is queued again. Its result follows here.", "The delivery was answered 200 in 42 ms."]);
    }

    [Fact]
    public void APingShowsItsResultOnceTheLogHasIt()
    {
        var id = Server.Add("Receiver", "https://hooks.example.com/in", ["content.published"]);
        (Server.Outcome, Server.QueuedFor) = (("dead", null, "The SSL connection could not be established."), 1);
        var cut = Render<WebhookPage>(parameters => parameters.Add(page => page.Id, id));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Nothing was delivered yet."));

        Button(cut, "Ping").Click();

        cut.WaitForAssertion(() => cut.Find(".webhooks-message").TextContent.ShouldBe("The ping failed: The SSL connection could not be established."), TimeSpan.FromSeconds(10));
        Server.Requests.ShouldContain(request => request.Method == HttpMethod.Post && request.Path == $"webhooks/{id:D}/ping");
        Cells(cut, 0).ShouldBe(["ping", "1", "Failed", "None", "42 ms"]);
        Server.Requests.Count(request => request.Path.StartsWith($"webhooks/{id:D}/deliveries", StringComparison.Ordinal)).ShouldBe(3);
    }

    [Fact]
    public void TheLogIsReadInPages()
    {
        var id = Server.Add("Receiver", "https://hooks.example.com/in", ["content.published"]);
        for (var count = 0; count < 25; count++)
        {
            Server.AddDelivery(id, "content.published", "succeeded", 204);
        }

        var cut = Render<WebhookPage>(parameters => parameters.Add(page => page.Id, id));
        cut.WaitForAssertion(() => cut.FindAll("tr.webhooks-attempt").Count.ShouldBe(20));
        cut.Markup.ShouldContain("Page 1 of 2");

        Button(cut, "Next").Click();

        cut.WaitForAssertion(() => cut.FindAll("tr.webhooks-attempt").Count.ShouldBe(5));
        cut.Markup.ShouldContain("Page 2 of 2");
        Server.Requests.Last().Path.ShouldBe($"webhooks/{id:D}/deliveries?page=2&pageSize=20");
    }

    [Fact]
    public void AWebhookThatDoesNotExistSaysSo()
    {
        var cut = Render<WebhookPage>(parameters => parameters.Add(page => page.Id, Guid.NewGuid()));

        cut.WaitForAssertion(() => cut.Find("h1").TextContent.ShouldBe("The webhook was not found"));
        cut.FindAll("table").ShouldBeEmpty();
    }

    // The cells of a row of the log after its time, without the buttons.
    private static string[] Cells(IRenderedComponent<WebhookPage> cut, int row) =>
        [.. cut.FindAll("tr.webhooks-attempt")[row].QuerySelectorAll("td").Take(5).Select(cell => cell.TextContent.Trim())];
}
