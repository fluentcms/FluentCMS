using System.Net;
using System.Text.Json.Nodes;
using Bunit;
using FluentCMS.Admin.Administration.ApiKeys;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Administration.ApiKeys.SecretChecks;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Administration.ApiKeys;

// The keys pages (spec 4.15): the list, the create form, a key's changes, the secret shown once and the refusals by what
// they mean.
public sealed class ApiKeysPageTests : AdministrationTestContext
{
    private const string Permission = "apikeys.manage";

    private readonly ApiKeysFake keys = new();
    private readonly LogCapture logs = new();

    public ApiKeysPageTests()
    {
        Services.AddSingleton<ILoggerFactory>(new LoggerFactory([logs]));
        Services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        Management.Respond = keys.Respond;
        GrantArea(Permission, instanceOnly: false);
    }

    [Fact]
    public void TheListShowsEachKeyWithItsRoleSitesAndState()
    {
        keys.Keys =
        [
            Key("Active key", "fcms_dk_ac01"),
            Key("Rotated key", "fcms_dk_ro01", previousKeyExpiresAt: "2999-01-01T00:00:00Z"),
            Key("Expired key", "fcms_dk_ex01", expiresAt: "2020-01-01T00:00:00Z"),
            Key("Revoked key", "fcms_dk_rv01", revokedAt: "2026-11-02T10:00:00Z"),
        ];

        var app = Load("api-keys");

        app.WaitForAssertion(() => app.FindAll("main a[href^='api-keys/0']").Count.ShouldBe(4));
        var text = app.Find("main").TextContent;
        text.ShouldContain("Reader");
        text.ShouldContain("Main");
        text.ShouldContain(Localized<ApiKeysResources>("ApiKeys.Status.Active"));
        text.ShouldContain(Localized<ApiKeysResources>("ApiKeys.Status.Overlap", Formats().DateTime(DateTimeOffset.Parse("2999-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture))));
        text.ShouldContain(Localized<ApiKeysResources>("ApiKeys.Status.Expired", Formats().DateTime(DateTimeOffset.Parse("2020-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture))));
        text.ShouldContain(Localized<ApiKeysResources>("ApiKeys.Status.Revoked", Formats().DateTime(DateTimeOffset.Parse("2026-11-02T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture))));
        app.Find("main a[href='api-keys/new']").TextContent.ShouldBe(Localized<ApiKeysResources>("ApiKeys.Page.New"));
    }

    // Acceptance: a key for one site and one role, whose secret cannot be read again once the dialog closes, and which was in
    // no log, no storage, no address and no other script call meanwhile.
    [Fact]
    public void AKeyIsMadeForOneSiteAndOneRoleAndItsSecretIsShownOnceAndHeldByNothingAfterwards()
    {
        JSInterop.SetupVoid("navigator.clipboard.writeText", ApiKeysFake.Secret).SetVoidResult();
        var app = OpenForm();

        app.Find("form.api-key-form input").Input("Shop reader");
        app.FindAll("form.api-key-form select")[1].Change(ApiKeysFake.Reader.ToString("D"));
        app.Find("form.api-key-form").Submit();

        app.WaitForAssertion(() => SecretDialog(app).Secret.ShouldBe(ApiKeysFake.Secret));
        var create = keys.Calls.Single(call => call.Method == HttpMethod.Post && call.Path == "api-keys");
        var body = JsonNode.Parse(create.Body!)!.AsObject();
        body["name"]!.GetValue<string>().ShouldBe("Shop reader");
        body["class"]!.GetValue<string>().ShouldBe("delivery");
        body["roleId"]!.GetValue<string>().ShouldBe(ApiKeysFake.Reader.ToString("D"));
        body["siteIds"]!.AsArray().Select(site => site!.GetValue<string>()).ShouldBe([MainSite.ToString("D")]);
        body.Select(member => member.Key).ShouldBe(["name", "class", "roleId", "siteIds"], ignoreOrder: true);
        create.Idempotent.ShouldBeFalse();

        Button(app, Text("SecretShow")).Click();
        app.Find(".cms-secret-value").TextContent.ShouldBe(ApiKeysFake.Secret);
        Button(app, Text("SecretCopy")).Click();
        app.WaitForAssertion(() => app.Find("p.cms-field-message[role=status]").TextContent.ShouldBe(Text("SecretCopied")));
        // The check sees the page's field and the dialog's parameter while the dialog is open.
        Holders(app, ApiKeysFake.Secret).ShouldBe(["ApiKeyNewPage.secret", "CmsSecretDialog.<Secret>k__BackingField"], ignoreOrder: true);
        Button(app, Text("SecretDone")).Click();

        app.WaitForAssertion(() => Navigation.Uri.ShouldEndWith($"api-keys/{ApiKeysFake.KeyId:D}"));
        app.WaitForAssertion(() => Heading(app).ShouldBe("Shop reader"));
        app.Markup.ShouldNotContain(ApiKeysFake.Secret);
        Holders(app, ApiKeysFake.Secret).ShouldBeEmpty();
        Storage.Items.Values.ShouldAllBe(value => !value.Contains(ApiKeysFake.Secret));
        Storage.Writes.ShouldAllBe(value => !value.Contains(ApiKeysFake.Secret));
        logs.Messages.ShouldAllBe(message => !message.Contains(ApiKeysFake.Secret));
        JSInterop.Invocations.Where(invocation => invocation.Arguments.Any(argument => argument is string text && text.Contains(ApiKeysFake.Secret, StringComparison.Ordinal)))
            .Select(invocation => invocation.Identifier).ShouldBe(["navigator.clipboard.writeText"]);
    }

    [Fact]
    public void TheFormSendsTheBindingsItIsGiven()
    {
        var app = OpenForm();

        app.Find("form.api-key-form input").Input("Feed");
        app.FindAll("form.api-key-form select")[0].Change("preview");
        app.FindAll("form.api-key-form select")[1].Change(ApiKeysFake.Reader.ToString("D"));
        Labelled(app, Localized<ApiKeysResources>("ApiKeys.New.RateLimit")).Change("120");
        app.Find("form.api-key-form textarea").Input("203.0.113.0/24\n\n 198.51.100.7 \n");
        app.Find("form.api-key-form").Submit();

        app.WaitForAssertion(() => keys.Calls.ShouldContain(call => call.Method == HttpMethod.Post && call.Path == "api-keys"));
        var body = JsonNode.Parse(keys.Calls.Single(call => call.Method == HttpMethod.Post).Body!)!;
        body["class"]!.GetValue<string>().ShouldBe("preview");
        body["rateLimit"]!.GetValue<int>().ShouldBe(120);
        body["allowedAddresses"]!.AsArray().Select(address => address!.GetValue<string>()).ShouldBe(["203.0.113.0/24", "198.51.100.7"]);
    }

    // A key bound to no site reaches every one, which needs apikeys.manage for the instance.
    [Fact]
    public void ACallerWhoManagesKeysInTheSiteOnlyIsNotOfferedEverySite()
    {
        var app = OpenForm();

        var labels = app.FindComponents<FluentCMS.Admin.Components.CmsCheckbox>().Select(box => box.Instance.Label).ToList();
        labels.ShouldContain("Main");
        labels.ShouldNotContain(Localized<ApiKeysResources>("ApiKeys.New.EverySite"));
    }

    [Fact]
    public void ACallerWhoManagesKeysForTheInstanceMayBindAKeyToNoSite()
    {
        Grant([], Permission);
        var app = OpenForm();
        Fill(app);

        EverySite(app).Change(true);
        app.Find("form.api-key-form").Submit();

        app.WaitForAssertion(() => keys.Calls.ShouldContain(call => call.Method == HttpMethod.Post && call.Path == "api-keys"));
        var body = JsonNode.Parse(keys.Calls.Single(call => call.Method == HttpMethod.Post).Body!)!.AsObject();
        body.ContainsKey("siteIds").ShouldBeFalse();
    }

    [Fact]
    public void ANameARoleAndASiteAreAskedForBeforeAnythingIsSent()
    {
        var app = OpenForm();

        app.Find("form.api-key-form").Submit();

        app.WaitForAssertion(() => app.Find("form.api-key-form").TextContent.ShouldContain(Localized<ApiKeysResources>("ApiKeys.New.NameRequired")));
        app.Find("form.api-key-form").TextContent.ShouldContain(Localized<ApiKeysResources>("ApiKeys.New.RoleRequired"));
        keys.Calls.ShouldNotContain(call => call.Method == HttpMethod.Post);
    }

    // The server checks apikeys.manage before it checks the role and the sites, so a caller without it is told only that.
    [Fact]
    public void ACallerWhoMayNotMakeTheKeyIsShownTheRefusalAndNoFieldErrors()
    {
        keys.Refuse = (method, path) => method == HttpMethod.Post && path == "api-keys" ? Problem(HttpStatusCode.Forbidden, "FCMS3103") : null;
        var app = OpenForm();

        Fill(app);
        app.Find("form.api-key-form").Submit();

        app.WaitForAssertion(() => app.Find(".api-key-problem").TextContent.ShouldBe(Localized<ApiKeysResources>("ApiKeys.New.Refused")));
        app.FindAll("form.api-key-form [aria-invalid=true]").ShouldBeEmpty();
        SecretDialog(app).Secret.ShouldBeNull();
    }

    [Fact]
    public void TheFieldsTheServerRefusesShowWhy()
    {
        keys.Refuse = (method, path) => method == HttpMethod.Post && path == "api-keys" ? ApiKeysFake.Invalid(("name", "FCMS3010"), ("allowedAddresses[0]", "FCMS3024")) : null;
        var app = OpenForm();

        Fill(app);
        app.Find("form.api-key-form").Submit();

        app.WaitForAssertion(() => app.Find("form.api-key-form").TextContent.ShouldContain(Localized<ApiKeysResources>("ApiKeys.Error.Name")));
        app.Find("form.api-key-form").TextContent.ShouldContain(Localized<ApiKeysResources>("ApiKeys.Error.Address"));
        app.FindAll(".api-key-problem").ShouldBeEmpty();
    }

    // Acceptance: rotated with an overlap, its new secret shown once; then expired and revoked, each shown with the key.
    [Fact]
    public void AKeyIsRotatedWithAnOverlapThenExpiredAndRevoked()
    {
        var app = OpenKey();

        app.Find("section[aria-labelledby=api-key-rotate] input").Change("2");
        Button(app, Localized<ApiKeysResources>("ApiKeys.Key.Rotate")).Click();
        app.WaitForAssertion(() => app.Find("main").TextContent.ShouldContain(Localized<ApiKeysResources>("ApiKeys.Confirm.RotateHours", "2")));
        Button(app, Localized<ApiKeysResources>("ApiKeys.Confirm.Rotate")).Click();

        app.WaitForAssertion(() => SecretDialog(app).Secret.ShouldBe(ApiKeysFake.RotatedSecret));
        JsonNode.Parse(keys.Calls.Single(call => call.Path.EndsWith("/rotate", StringComparison.Ordinal)).Body!)!["overlapSeconds"]!.GetValue<int>().ShouldBe(7200);
        Button(app, Text("SecretDone")).Click();
        app.WaitForAssertion(() => SecretDialog(app).Secret.ShouldBeNull());
        app.Markup.ShouldNotContain(ApiKeysFake.RotatedSecret);

        // Rotate was disabled while the rotation ran, so focus goes back to it once the dialog has closed.
        var host = app.FindComponent<FluentCMS.Admin.Components.CmsSecretDialog>().Find("fluent-dialog");
        host.TriggerEvent("ondialogtoggle", new Microsoft.FluentUI.AspNetCore.Components.DialogToggleEventArgs { Id = host.Id, OldState = "open", NewState = "closed" });
        var rotate = app.FindComponents<Microsoft.FluentUI.AspNetCore.Components.FluentButton>()
            .Single(button => button.FindAll("fluent-button") is [var host, ..] && host.TextContent.Trim() == Localized<ApiKeysResources>("ApiKeys.Key.Rotate")).Instance.Element.Id;
        app.WaitForAssertion(() => JSInterop.Invocations.Last(invocation => invocation.Identifier == "Blazor._internal.domWrapper.focus").Arguments[0]
            .ShouldBeOfType<Microsoft.AspNetCore.Components.ElementReference>().Id.ShouldBe(rotate));
        Holders(app, ApiKeysFake.RotatedSecret).ShouldBeEmpty();
        app.WaitForAssertion(() => app.Find(".api-key-facts").TextContent.ShouldContain(
            Localized<ApiKeysResources>("ApiKeys.Status.Overlap", Formats().DateTime(DateTimeOffset.Parse("2999-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture)))));

        Button(app, Localized<ApiKeysResources>("ApiKeys.Key.Expire")).Click();
        Button(app, Localized<ApiKeysResources>("ApiKeys.Confirm.Expire")).Click();
        app.WaitForAssertion(() => app.Find(".api-key-facts").TextContent.ShouldContain(
            Localized<ApiKeysResources>("ApiKeys.Status.Expired", Formats().DateTime(DateTimeOffset.Parse("2020-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture)))));
        JsonNode.Parse(keys.Calls.Single(call => call.Path.EndsWith("/expire", StringComparison.Ordinal)).Body!)!.AsObject().Count.ShouldBe(0);

        Button(app, Localized<ApiKeysResources>("ApiKeys.Key.Revoke")).Click();
        Button(app, Localized<ApiKeysResources>("ApiKeys.Confirm.Revoke")).Click();
        app.WaitForAssertion(() => app.Find("main").TextContent.ShouldContain(Localized<ApiKeysResources>("ApiKeys.Key.RevokedNote")));
        keys.Calls.ShouldContain(call => call.Method == HttpMethod.Delete);
        app.FindAll("section[aria-labelledby=api-key-rotate]").ShouldBeEmpty();
    }

    [Fact]
    public void TheAllowedAddressesAreSavedAndNoneIsAnyAddress()
    {
        var app = OpenKey();

        app.Find("section[aria-labelledby=api-key-addresses] textarea").Input("203.0.113.0/24");
        app.Find("section[aria-labelledby=api-key-addresses] form").Submit();
        app.WaitForAssertion(() => app.Find(".api-key-facts").TextContent.ShouldContain("203.0.113.0/24"));

        app.Find("section[aria-labelledby=api-key-addresses] textarea").Input("  ");
        app.Find("section[aria-labelledby=api-key-addresses] form").Submit();
        app.WaitForAssertion(() => app.Find(".api-key-facts").TextContent.ShouldContain(Localized<ApiKeysResources>("ApiKeys.Key.AnyAddress")));

        keys.Calls.Where(call => call.Method == HttpMethod.Put).Select(call => call.Body)
            .ShouldBe(["""{"allowedAddresses":["203.0.113.0/24"]}""", """{"allowedAddresses":null}"""]);
    }

    // Acceptance: rotate, expire, revoke and the addresses need every grant of the key's role as bound, and a caller below it
    // is told so by name.
    [Theory]
    [InlineData("rotate")]
    [InlineData("expire")]
    [InlineData("revoke")]
    [InlineData("addresses")]
    public void ACallerBelowTheKeysRoleIsToldWhyTheChangeIsRefused(string change)
    {
        keys.Refuse = (method, path) => method != HttpMethod.Get ? Problem(HttpStatusCode.Forbidden, "FCMS3103") : null;
        var app = OpenKey();

        if (change == "addresses")
        {
            app.Find("section[aria-labelledby=api-key-addresses] form").Submit();
        }
        else
        {
            var name = char.ToUpperInvariant(change[0]) + change[1..];
            Button(app, Localized<ApiKeysResources>($"ApiKeys.Key.{name}")).Click();
            Button(app, Localized<ApiKeysResources>($"ApiKeys.Confirm.{name}")).Click();
        }

        app.WaitForAssertion(() => app.Find(".api-key-problem").TextContent.ShouldBe(Localized<ApiKeysResources>("ApiKeys.Key.Refused")));
        SecretDialog(app).Secret.ShouldBeNull();
    }

    [Fact]
    public void AnOverlapTheServerRefusesIsShownOnItsField()
    {
        keys.Refuse = (method, path) => path.EndsWith("/rotate", StringComparison.Ordinal) ? ApiKeysFake.Invalid(("overlapSeconds", "FCMS3026")) : null;
        var app = OpenKey();

        app.Find("section[aria-labelledby=api-key-rotate] input").Change("9999");
        Button(app, Localized<ApiKeysResources>("ApiKeys.Key.Rotate")).Click();
        Button(app, Localized<ApiKeysResources>("ApiKeys.Confirm.Rotate")).Click();

        app.WaitForAssertion(() => app.Find("section[aria-labelledby=api-key-rotate]").TextContent.ShouldContain(Localized<ApiKeysResources>("ApiKeys.Error.Overlap")));
    }

    // A key the caller does not manage answers 404, as one that is not there.
    [Fact]
    public void AKeyTheCallerDoesNotManageIsNotFound()
    {
        var app = Load($"api-keys/{Guid.CreateVersion7():D}");

        app.WaitForAssertion(() => app.Find("main").TextContent.ShouldContain(Localized<ApiKeysResources>("ApiKeys.Key.NotFound")));
        app.Find($"main a[href='{ApiKeyRoutes.List}']");
    }

    private IRenderedComponent<AdminApp> OpenForm()
    {
        var app = Load("api-keys/new");
        app.WaitForAssertion(() => app.FindAll("form.api-key-form select").Count.ShouldBe(2));
        return app;
    }

    private IRenderedComponent<AdminApp> OpenKey()
    {
        var app = Load(ApiKeyRoutes.Key(ApiKeysFake.KeyId));
        app.WaitForAssertion(() => app.Find(".api-key-facts"));
        return app;
    }

    // The control a label names, by the label's text without its required mark.
    private static AngleSharp.Dom.IElement Labelled(IRenderedComponent<AdminApp> app, string label) =>
        app.Find("#" + app.FindAll("label[for]").First(element => element.TextContent.Trim().StartsWith(label, StringComparison.Ordinal)).GetAttribute("for"));

    private AngleSharp.Dom.IElement EverySite(IRenderedComponent<AdminApp> app) =>
        app.FindComponents<FluentCMS.Admin.Components.CmsCheckbox>().Single(box => box.Instance.Label == Localized<ApiKeysResources>("ApiKeys.New.EverySite")).Find("fluent-checkbox");

    private static void Fill(IRenderedComponent<AdminApp> app)
    {
        app.Find("form.api-key-form input").Input("Shop reader");
        app.FindAll("form.api-key-form select")[1].Change(ApiKeysFake.Reader.ToString("D"));
    }

    private static JsonObject Key(string name, string prefix, string? expiresAt = null, string? revokedAt = null, string? previousKeyExpiresAt = null)
    {
        var key = ApiKeysFake.KeyOf(Guid.CreateVersion7(), name, prefix);
        key["expiresAt"] = expiresAt;
        key["revokedAt"] = revokedAt;
        key["previousKeyExpiresAt"] = previousKeyExpiresAt;
        return key;
    }

    private Localization.FormatService Formats() => Services.GetRequiredService<Localization.FormatService>();

    private string Text(string name) => Services.GetRequiredService<FluentCMS.Admin.Components.ICmsTextSource>().GetText(name)!;
}
