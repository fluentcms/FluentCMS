using Bunit;
using FluentCMS.Admin.Api;
using FluentCMS.Plugins.Webhooks.Admin.Api;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Webhooks.Admin.Tests;

// The form of a webhook: a new one with its events, types and headers, the secret the server makes, the stored headers of a
// changed one, which a move of the address asks for again, and a rotation of the secret.
public sealed class WebhookFormTests : WebhooksTestContext
{
    private const string Published = "Published (content.published)";

    [Fact]
    public void ANewWebhookForAPublishEventSendsWhatWasChosenAndHandsOverTheSecretTheServerMade()
    {
        WebhookSaved? saved = null;
        var cut = Render<WebhookForm>(parameters => parameters.Add(form => form.OnSaved, (WebhookSaved value) => saved = value));
        cut.WaitForAssertion(() => cut.FindComponents<FluentCMS.Admin.Components.CmsCheckbox>().Any(box => box.Instance.Label == "Article").ShouldBeTrue());

        Field(cut, "Name").Input("Publisher");
        Field(cut, "Address").Input("https://hooks.example.com/in");
        Box(cut, Published).Change(true);
        Box(cut, "Article").Change(true);
        Button(cut, "Add a header").Click();
        Field(cut, "Name of header 1").Input("Authorization");
        Field(cut, "Value of header 1").Input("Bearer abc");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => saved.ShouldNotBeNull());
        saved!.ShouldNotBeNull();
        Server.Requests.Single(request => request.Method == HttpMethod.Post).Body!.ToJsonString().ShouldBe(
            """{"name":"Publisher","url":"https://hooks.example.com/in","events":["content.published"],"types":["article"],"headers":{"Authorization":"Bearer abc"},"enabled":true}""");
        (saved.Secret, saved.Created, saved.Webhook.Name).ShouldBe((Secret, true, "Publisher"));
        saved.Webhook.Headers["Authorization"].ShouldBe(WebhooksApi.RedactedValue);

        // The form is empty for the next one.
        cut.WaitForAssertion(() => Field(cut, "Name").GetAttribute("value").ShouldBeNullOrEmpty());
        cut.FindAll("input[type=password]").ShouldBeEmpty();
    }

    [Fact]
    public void AWebhookWithoutAnEventIsNotSentAndTheFormSaysWhy()
    {
        var cut = Render<WebhookForm>();
        Field(cut, "Name").Input("Publisher");
        Field(cut, "Address").Input("https://hooks.example.com/in");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => cut.Find("fieldset [role=alert]").TextContent.ShouldBe("Choose one or more events."));
        Server.Requests.ShouldBeEmpty();
        Announcer.Messages.ShouldBe(["Choose one or more events."]);
    }

    [Fact]
    public async Task AStoredHeaderSentBackToTheSameHostKeepsItsValueAndShowsNone()
    {
        var id = Server.Add("Receiver", "https://hooks.example.com/in", ["content.published"], new() { ["Authorization"] = "Bearer old" });
        WebhookSaved? saved = null;
        var stored = await Services.GetRequiredService<IManagementApi>().GetWebhookAsync(id, Xunit.TestContext.Current.CancellationToken);
        var cut = Render<WebhookForm>(parameters => parameters
            .Add(form => form.Webhook, stored)
            .Add(form => form.OnSaved, (WebhookSaved value) => saved = value));

        Field(cut, "Name of header 1").GetAttribute("value").ShouldBe("Authorization");
        Field(cut, "Name of header 1").HasAttribute("readonly").ShouldBeTrue();
        Field(cut, "Value of header 1").GetAttribute("value").ShouldBeNullOrEmpty();
        Message(cut, "Value of header 1").ShouldBe("Leave it empty to keep the stored value.");

        // A new path on the same host keeps the stored value.
        Field(cut, "Address").Input("https://hooks.example.com/other");
        Message(cut, "Value of header 1").ShouldBe("Leave it empty to keep the stored value.");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => saved.ShouldNotBeNull());
        saved!.ShouldNotBeNull();
        Server.Requests.Single(request => request.Method == HttpMethod.Put).Body!["headers"]!.ToJsonString().ShouldBe("""{"Authorization":"***"}""");
        Server.Headers[id]["Authorization"].ShouldBe("Bearer old");
        (saved.Secret, saved.Created).ShouldBe((null, false));
        cut.Markup.ShouldNotContain("Bearer old");
    }

    [Fact]
    public async Task AChangeOfTheAddressAsksForEachHeadersValueAgainAndASaveWithoutItShowsTheRefusalOnTheHeader()
    {
        var id = Server.Add("Receiver", "https://hooks.example.com/in", ["content.published"], new() { ["Authorization"] = "Bearer old", ["X-Tenant"] = "blue" });
        WebhookSaved? saved = null;
        var stored = await Services.GetRequiredService<IManagementApi>().GetWebhookAsync(id, Xunit.TestContext.Current.CancellationToken);
        var cut = Render<WebhookForm>(parameters => parameters
            .Add(form => form.Webhook, stored)
            .Add(form => form.OnSaved, (WebhookSaved value) => saved = value));

        Field(cut, "Address").Input("https://elsewhere.example.com/in");

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("The address now has another scheme, host or port, so each header needs its value again."));
        Message(cut, "Value of header 1").ShouldBe("Enter the value again: the stored one is not sent to another host.");
        cut.Find($"label[for='{Field(cut, "Value of header 2").Id}'] .cms-field-required").ShouldNotBeNull();

        Field(cut, "Value of header 2").Input("green");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Message(cut, "Value of header 1").ShouldBe(
            "Enter this header's value again: its stored value is kept only while the address keeps its scheme, host and port."));
        Field(cut, "Value of header 1").GetAttribute("aria-invalid").ShouldBe("true");
        Field(cut, "Value of header 2").GetAttribute("aria-invalid").ShouldNotBe("true");
        Server.Requests.Single(request => request.Method == HttpMethod.Put).Body!["headers"]!.ToJsonString().ShouldBe("""{"Authorization":"***","X-Tenant":"green"}""");
        Server.Headers[id]["Authorization"].ShouldBe("Bearer old");
        saved.ShouldBeNull();
        Announcer.Messages.ShouldContain("The webhook was not saved. The fields say why.");

        Field(cut, "Value of header 1").Input("Bearer new");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => saved.ShouldNotBeNull());
        saved!.ShouldNotBeNull();
        Server.Headers[id].ShouldBe(new Dictionary<string, string> { ["Authorization"] = "Bearer new", ["X-Tenant"] = "green" });
        ((string)Server.Webhooks[id]["url"]!).ShouldBe("https://elsewhere.example.com/in");
    }

    [Fact]
    public async Task ARotationSendsANewSecretAndHandsItOverToBeShownOnce()
    {
        var id = Server.Add("Receiver", "https://hooks.example.com/in", ["content.published"]);
        WebhookSaved? saved = null;
        var stored = await Services.GetRequiredService<IManagementApi>().GetWebhookAsync(id, Xunit.TestContext.Current.CancellationToken);
        var cut = Render<WebhookForm>(parameters => parameters
            .Add(form => form.Webhook, stored)
            .Add(form => form.OnSaved, (WebhookSaved value) => saved = value));
        cut.Markup.ShouldNotContain("The server makes the signing secret");

        Box(cut, "Make a new secret").Change(true);
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => saved.ShouldNotBeNull());
        saved!.ShouldNotBeNull();
        var sent = Server.Requests.Single(request => request.Method == HttpMethod.Put).Body!["secret"]!.GetValue<string>();
        sent.ShouldStartWith("whsec_");
        sent.Length.ShouldBe(70);
        (saved.Secret, saved.Created).ShouldBe((sent, false));
        saved.Webhook.PreviousSecretExpiresAt.ShouldNotBeNull();
    }

    [Fact]
    public void ANameTheSiteHasAlreadyIsShownByTheName()
    {
        Server.Refuse = request => request.Method == HttpMethod.Post ? Problem(409, "FCMS1566") : null;
        var cut = Render<WebhookForm>();
        Field(cut, "Name").Input("Publisher");
        Field(cut, "Address").Input("https://hooks.example.com/in");
        Box(cut, Published).Change(true);

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Message(cut, "Name").ShouldBe("The site has a webhook of this name already."));
        Field(cut, "Name").GetAttribute("aria-invalid").ShouldBe("true");
        Field(cut, "Name").GetAttribute("value").ShouldBe("Publisher");
    }
}
