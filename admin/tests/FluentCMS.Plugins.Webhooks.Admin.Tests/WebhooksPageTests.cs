using System.Reflection;
using Bunit;
using FluentCMS.Admin.Components;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Webhooks.Admin.Tests;

// The webhooks of the site: a create whose secret is shown once and held by nothing afterwards, enable and disable with the
// headers kept, an edit that rotates the secret, and a delete after the question.
public sealed class WebhooksPageTests : WebhooksTestContext
{
    [Fact]
    public void AnAdministratorMakesAWebhookForAPublishEventCopiesItsSecretAndTheSecretCannotBeReadAgain()
    {
        JSInterop.SetupVoid("navigator.clipboard.writeText", Secret).SetVoidResult();
        var cut = Render<WebhooksPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("The site has no webhooks yet."));

        Field(cut, "Name").Input("Publisher");
        Field(cut, "Address").Input("https://hooks.example.com/in");
        Box(cut, "Published (content.published)").Change(true);
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => SecretDialog(cut).Secret.ShouldBe(Secret));
        cut.WaitForAssertion(() => cut.Find("table tbody th a").TextContent.ShouldBe("Publisher"));
        cut.Find("table tbody th a").GetAttribute("href").ShouldBe($"webhooks/{Server.Webhooks.Keys.Single():D}");
        cut.Find(".webhooks-message").TextContent.ShouldBe("The webhook Publisher was created.");

        Button(cut, CmsText("SecretShow")).Click();
        cut.Find(".cms-secret-value").TextContent.ShouldBe(Secret);
        Button(cut, CmsText("SecretCopy")).Click();
        cut.WaitForAssertion(() => cut.Find(".cms-secret ~ p.cms-field-message[role=status]").TextContent.ShouldBe(CmsText("SecretCopied")));
        Button(cut, CmsText("SecretDone")).Click();

        cut.WaitForAssertion(() => SecretDialog(cut).Secret.ShouldBeNull());
        cut.Markup.ShouldNotContain(Secret);
        typeof(WebhooksPage).GetField("secret", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cut.Instance).ShouldBeNull();

        // No read gives it back.
        Server.Webhooks.Values.Single().ToJsonString().ShouldNotContain(Secret);
        cut.Find("table tbody td code").TextContent.ShouldBe("https://hooks.example.com/in");
    }

    [Fact]
    public void AWebhookIsDisabledAndEnabledAgainAndItsHeadersKeepTheirValues()
    {
        var id = Server.Add("Receiver", "https://hooks.example.com/in", ["content.published"], new() { ["Authorization"] = "Bearer old" });
        var cut = Render<WebhooksPage>();
        cut.WaitForAssertion(() => Row(cut).ShouldContain("Enabled"));

        Button(cut, "Disable the webhook Receiver").Click();

        cut.WaitForAssertion(() => Row(cut).ShouldContain("Disabled"));
        var put = Server.Requests.Single(request => request.Method == HttpMethod.Put);
        put.Path.ShouldBe($"webhooks/{id:D}");
        put.Body!.ToJsonString().ShouldBe("""{"name":"Receiver","url":"https://hooks.example.com/in","events":["content.published"],"types":[],"headers":{"Authorization":"***"},"enabled":false}""");
        Server.Headers[id]["Authorization"].ShouldBe("Bearer old");
        cut.Find(".webhooks-message").TextContent.ShouldBe("The webhook Receiver is disabled.");

        Button(cut, "Enable the webhook Receiver").Click();
        cut.WaitForAssertion(() => Row(cut).ShouldContain("Enabled"));
        Server.Headers[id]["Authorization"].ShouldBe("Bearer old");
    }

    // The form open on the webhook is read again from the toggle's answer, so its save keeps the toggle.
    [Fact]
    public void ASaveOfTheFormAfterTheSameWebhookWasDisabledDoesNotEnableItAgain()
    {
        var id = Server.Add("Receiver", "https://hooks.example.com/in", ["content.published"]);
        var cut = Render<WebhooksPage>();
        cut.WaitForAssertion(() => Row(cut).ShouldContain("Enabled"));

        Button(cut, "Edit the webhook Receiver").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Edit Receiver"));
        Button(cut, "Disable the webhook Receiver").Click();
        cut.WaitForAssertion(() => Row(cut).ShouldContain("Disabled"));
        cut.WaitForAssertion(() => cut.FindComponents<CmsCheckbox>().Single(box => box.Instance.Label == "Enabled").Instance.Value.ShouldBeFalse());

        Field(cut, "Name").Input("Receiver two");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Server.Requests.Count(request => request.Method == HttpMethod.Put).ShouldBe(2));
        var saved = Server.Requests.Last(request => request.Method == HttpMethod.Put).Body!;
        (saved["name"]!.GetValue<string>(), saved["enabled"]!.GetValue<bool>()).ShouldBe(("Receiver two", false));
        cut.WaitForAssertion(() => Row(cut).ShouldContain("Disabled"));
        Server.Webhooks[id]["enabled"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Fact]
    public void AnEditThatRotatesTheSecretShowsTheNewOneOnceAndUntilWhenTheOldOneSigns()
    {
        var id = Server.Add("Receiver", "https://hooks.example.com/in", ["content.published"]);
        var cut = Render<WebhooksPage>();
        cut.WaitForAssertion(() => Row(cut).ShouldContain("Receiver"));

        Button(cut, "Edit the webhook Receiver").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Edit Receiver"));
        Field(cut, "Name").GetAttribute("value").ShouldBe("Receiver");
        Box(cut, "Make a new secret").Change(true);
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => SecretDialog(cut).Secret.ShouldNotBeNull().ShouldStartWith("whsec_"));
        SecretDialog(cut).Title.ShouldBe("Copy the new signing secret");
        Server.Requests.Last(request => request.Method == HttpMethod.Put).Body!["secret"]!.GetValue<string>().ShouldBe(SecretDialog(cut).Secret);
        cut.WaitForAssertion(() => Row(cut).ShouldContain("The previous secret signs too until"));
        cut.Markup.ShouldContain("New webhook");
        Server.Webhooks[id]["name"]!.GetValue<string>().ShouldBe("Receiver");
    }

    [Fact]
    public void AWebhookIsDeletedAfterTheQuestionThatNamesIt()
    {
        var id = Server.Add("Receiver", "https://hooks.example.com/in", ["content.published"]);
        var cut = Render<WebhooksPage>();
        cut.WaitForAssertion(() => Row(cut).ShouldContain("Receiver"));

        Button(cut, "Delete the webhook Receiver").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("The webhook Receiver and its delivery log are deleted"));
        Button(cut, "Delete").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("The site has no webhooks yet."));
        Server.Requests.ShouldContain(request => request.Method == HttpMethod.Delete && request.Path == $"webhooks/{id:D}");
        cut.Find(".webhooks-message").TextContent.ShouldBe("The webhook Receiver was deleted.");
    }

    [Fact]
    public void ACallerWithoutTheGrantIsToldSo()
    {
        Server.Refuse = _ => Problem(403, "FCMS0403");
        var cut = Render<WebhooksPage>();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.ShouldContain("You may not manage the webhooks of this site."));
    }

    private static CmsSecretDialog SecretDialog(IRenderedComponent<WebhooksPage> cut) => cut.FindComponent<CmsSecretDialog>().Instance;

    private static string Row(IRenderedComponent<WebhooksPage> cut) => cut.Find("table tbody tr").TextContent;
}
