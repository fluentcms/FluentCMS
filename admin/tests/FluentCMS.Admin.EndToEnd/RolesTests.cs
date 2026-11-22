using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.EndToEnd.Ui;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The roles pages (spec 4.15): an administrator makes a role from the catalogue by keyboard, with grants of core, of a
// content type and of a plugin, changes it, and the role is given to a user, which the server's refusal to delete it shows;
// and a role limited to a locale, whose user changes an entry in that locale only.
public sealed class RolesTests(EndToEndHost host) : BrowserTest(host)
{
    [Fact]
    public async Task AnAdministratorMakesARoleWithGrantsOfCoreOfAContentTypeAndOfAPluginThatIsThenGivenToAUser()
    {
        var browser = await OpenBrowserAsync();
        var tab = await SignedInTabAsync(browser, "roles", await Host.CreateEditorAsync("Super Admin"));
        var main = tab.Page.Locator("main");
        await Expect(Heading(main, Repository.AdministrationHeading("Roles"))).ToBeVisibleAsync();

        await PressAsync(main.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = Text("Roles.List.New"), Exact = true }), "Enter");
        await Expect(Heading(main, Text("Roles.Editor.NewHeading"))).ToBeVisibleAsync();
        var role = $"Translators {Mark}";
        await main.GetByLabel(Text("Roles.Editor.Name")).FillAsync(role);
        await AddGrantAsync(main, "core", "schema.read");
        await AddGrantAsync(main, "content", "content.article.read");
        await AddGrantAsync(main, "plugin", "media.read");
        await Expect(main.Locator("[data-grant='media.read']")).ToContainTextAsync(Format(Text("Roles.Grants.OfPlugin"), "media"));

        var created = await tab.AnswerAsync(() => PressAsync(ButtonIn(main, Text("Roles.Editor.Create")), "Enter"), "POST", tab.ManagementUrl("roles"));
        var id = await CreatedIdAsync(created, "roles");
        await Expect(Heading(main, role)).ToBeVisibleAsync();
        await Expect(tab.Page).ToHaveURLAsync(new Uri(Host.Admin, $"roles/{id:D}").AbsoluteUri);
        await Expect(main.Locator("[data-grant]")).ToHaveCountAsync(3);

        var renamed = $"Article translators {Mark}";
        await main.GetByLabel(Text("Roles.Editor.Name")).FillAsync(renamed);
        var saved = await tab.AnswerAsync(() => PressAsync(ButtonIn(main, Text("Roles.Editor.Save")), "Enter"), "PUT", tab.ManagementUrl($"roles/{id:D}"));
        saved.Status.ShouldBe(200);
        await Expect(main.GetByText(Text("Roles.Editor.Saved"), new LocatorGetByTextOptions { Exact = true })).ToBeVisibleAsync();

        // The users page is another issue's: the host's command gives the role, as an operator does.
        await Host.CreateEditorAsync(renamed, site: "default");

        await PressAsync(ButtonIn(main, Text("Roles.Editor.Delete")), "Enter");
        var refused = await tab.AnswerAsync(() => PressAsync(ButtonIn(main, Text("Roles.Editor.DeleteConfirm")), "Enter"), "DELETE", tab.ManagementUrl($"roles/{id:D}"));
        refused.Status.ShouldBe(409);
        await Expect(main.GetByText(Text("Roles.Editor.InUse"), new LocatorGetByTextOptions { Exact = true })).ToBeVisibleAsync();
        await Expect(tab.Page).ToHaveURLAsync(new Uri(Host.Admin, $"roles/{id:D}").AbsoluteUri);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // A-52: a grant of update limited to French through the editor of its constraints, by keyboard; the role's user then
    // changes an article in French, and reads it in English with the form read-only.
    [Fact]
    public async Task ARoleLimitedToALocaleLetsItsUserChangeAnEntryInThatLocaleOnly()
    {
        var english = $"Written in English {Mark}";
        Guid entry;
        using (var api = await Host.ManagementClientAsync())
        {
            entry = await CreateInEnglishAndFrenchAsync(api, english, $"Écrit en français {Mark}");
        }

        var browser = await OpenBrowserAsync();
        var tab = await SignedInTabAsync(browser, "roles/new", await Host.CreateEditorAsync("Super Admin"));
        var main = tab.Page.Locator("main");
        await Expect(Heading(main, Text("Roles.Editor.NewHeading"))).ToBeVisibleAsync();
        var role = $"French translators {Mark}";
        await main.GetByLabel(Text("Roles.Editor.Name")).FillAsync(role);
        await AddGrantAsync(main, "core", "schema.read");
        await AddGrantAsync(main, "content", "content.article.read");
        await AddGrantAsync(main, "content", "content.article.update");

        var grant = main.Locator("[data-grant='content.article.update']");
        await PressAsync(grant.Locator("summary"), "Enter");
        var french = grant.Locator("fluent-field").Filter(new LocatorFilterOptions { HasTextRegex = new Regex(@"^\s*fr\b") }).Locator("fluent-checkbox");
        await PressAsync(french, "Space");

        // The library's checkbox keeps its state in a property, not in an attribute Playwright reads as checked.
        await Expect(french).ToHaveJSPropertyAsync("checked", true);
        await Expect(grant.Locator("dl")).ToContainTextAsync("fr");

        var created = await tab.AnswerAsync(() => PressAsync(ButtonIn(main, Text("Roles.Editor.Create")), "Enter"), "POST", tab.ManagementUrl("roles"));
        created.Status.ShouldBe(201);
        var sent = JsonNode.Parse(created.Request.PostData!)!["grants"]!.AsArray();
        JsonNode.DeepEquals(sent.Single(item => (string?)item!["permission"] == "content.article.update")!["constraints"], new JsonObject { ["locales"] = new JsonArray("fr") })
            .ShouldBeTrue(sent.ToJsonString());
        await Expect(Heading(main, role)).ToBeVisibleAsync();

        // The user of the role changes the French, and reads the English without a way to change it.
        var user = await Host.CreateEditorAsync(role, site: "default");
        var translator = await SignedInTabAsync(await OpenBrowserAsync(), $"content/article/{entry:D}?locale=fr", user);
        var title = translator.Page.Locator("[data-field='title'] input").First;
        await Expect(title).ToHaveValueAsync($"Écrit en français {Mark}");
        await Expect(title).Not.ToHaveAttributeAsync("readonly", "");
        await title.FillAsync($"Changé en français {Mark}");
        var saved = await translator.AnswerAsync(() => SaveButton(translator).ClickAsync(), "PUT", translator.ManagementUrl($"content/article/{entry:D}"));
        saved.Status.ShouldBe(200);
        (await saved.Request.HeaderValueAsync("x-cms-locale")).ShouldBe("fr");

        await translator.Page.GotoAsync(new Uri(Host.Admin, $"content/article/{entry:D}?locale=en").AbsoluteUri);
        await Expect(title).ToHaveValueAsync(english);
        await Expect(title).ToHaveAttributeAsync("readonly", "");
        await Expect(SaveButton(translator)).ToHaveCountAsync(0);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    private static async Task<Guid> CreateInEnglishAndFrenchAsync(HttpClient api, string english, string french)
    {
        var token = TestContext.Current.CancellationToken;
        using var created = await api.PostAsJsonAsync(new Uri("api/management/v1/content/article", UriKind.Relative), new JsonObject { ["title"] = english }, token);
        var answer = await created.Content.ReadAsStringAsync(token);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, answer);
        var id = JsonNode.Parse(answer)!["id"]!.GetValue<Guid>();

        using var translation = new HttpRequestMessage(HttpMethod.Put, new Uri($"api/management/v1/content/article/{id:D}", UriKind.Relative))
        {
            Content = JsonContent.Create(new JsonObject { ["title"] = french }),
        };
        translation.Headers.TryAddWithoutValidation("If-Match", "\"0\"");
        translation.Headers.Add("X-Cms-Locale", "fr");
        using var added = await api.SendAsync(translation, token);
        added.StatusCode.ShouldBe(HttpStatusCode.OK, await added.Content.ReadAsStringAsync(token));
        return id;
    }

    private static ILocator SaveButton(AdminTab tab) =>
        tab.Page.Locator(".content-form-toolbar fluent-button").Filter(new LocatorFilterOptions { HasTextRegex = new Regex($@"^\s*{Regex.Escape(Repository.AdminText("Content.Form.Save"))}\s*$") });

    private static string Text(string key) => Repository.AdministrationText("Roles", key);

    private static ILocator Heading(ILocator main, string name) =>
        main.GetByRole(AriaRole.Heading, new LocatorGetByRoleOptions { Name = name, Exact = true, Level = 1 });

    private static async Task AddGrantAsync(ILocator main, string source, string permission)
    {
        await main.GetByLabel(Text("Roles.Grants.Source"), new LocatorGetByLabelOptions { Exact = true }).SelectOptionAsync(source);
        await main.GetByLabel(Text("Roles.Grants.Permission"), new LocatorGetByLabelOptions { Exact = true }).SelectOptionAsync(permission);
        await PressAsync(ButtonIn(main, Text("Roles.Grants.Add")), "Enter");
        await Expect(main.Locator($"[data-grant='{permission}']")).ToBeVisibleAsync();
    }
}
