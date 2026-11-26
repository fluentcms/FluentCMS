using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The AI assist half's field actions of A-38 for the built-in Editor: on an opted-in field of the Blog sample's article the
// editor runs Rewrite, sees the suggestion arrive in parts from the run's stand-in provider, accepts it and finds it in the
// draft as an unsaved change, which nothing saved; and translates the title into the French draft.
public sealed class AiAssistTests(EndToEndHost host) : BrowserTest(host)
{
    private static readonly Dictionary<string, string> AiTexts =
        XDocument.Load(Path.Combine(Repository.Root, "admin", "src", "plugins", "FluentCMS.Plugins.AiAssist.Admin", "AiAssistResources.resx")).Root!.Elements("data")
            .ToDictionary(data => (string)data.Attribute("name")!, data => (string)data.Element("value")!, StringComparer.Ordinal);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheEditorAcceptsASuggestionThatArrivesInPartsAndItIsAnUnsavedChangeOfTheDraft()
    {
        using var api = await Host.ManagementClientAsync();
        var version = await OptInAsync(api);
        try
        {
            await RunAsync(api);
        }
        finally
        {
            await ResetAsync(api, version);
        }
    }

    private async Task RunAsync(HttpClient api)
    {
        var id = await CreateArticleAsync(api, $"Ships are big boats {Mark}");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        (Host.Ai.Pieces, Host.Ai.Gate) = (["Ships sail ", $"the open sea {Mark}."], gate);

        var browser = await OpenBrowserAsync();
        var tab = await SignedInTabAsync(browser, $"content/article/{id:D}");
        var title = tab.Page.Locator("[data-field='title'] input");
        await Expect(title).ToHaveValueAsync($"Ships are big boats {Mark}");
        List<string> saves = [];
        tab.Page.Request += (_, request) =>
        {
            if (request.Method != "GET" && request.Url.Contains($"/content/article/{id:D}", StringComparison.Ordinal))
            {
                lock (saves)
                {
                    saves.Add(request.Method);
                }
            }
        };

        var field = tab.Page.Locator("[data-field='title']");
        await field.Locator(".field-actions > fluent-button").ClickAsync();
        var rewrite = Ui.ButtonIn(field.Locator(".field-actions-list"), AiTexts["Ai.Action.rewrite"]);
        await Expect(rewrite).ToBeVisibleAsync();
        var panel = await tab.OpenDialogAsync(() => rewrite.ClickAsync(), AiTexts["Ai.Action.rewrite"]);

        await Expect(panel.Locator(".ai-suggestion")).ToHaveTextAsync("Ships sail");
        await Expect(Ui.ButtonIn(panel, AiTexts["Ai.Panel.Accept"])).ToHaveCountAsync(0);
        await Expect(Ui.ButtonIn(panel, AiTexts["Ai.Panel.Stop"])).ToBeVisibleAsync();
        gate.SetResult();

        var accept = Ui.ButtonIn(panel, AiTexts["Ai.Panel.Accept"]);
        await Expect(accept).ToBeVisibleAsync();
        await Expect(panel.Locator(".ai-suggestion")).ToHaveTextAsync($"Ships sail the open sea {Mark}.");
        await Expect(tab.Announced(AiTexts["Ai.Panel.Ready"])).ToBeAttachedAsync();
        await accept.ClickAsync();

        await Expect(tab.ShownDialog(AiTexts["Ai.Action.rewrite"])).ToBeHiddenAsync();
        await Expect(title).ToHaveValueAsync($"Ships sail the open sea {Mark}.");
        await Expect(tab.Announced(Ui.Format(Repository.AdminText("Content.FieldActions.Changed"), AiTexts["Ai.Action.rewrite"], "Title"))).ToBeAttachedAsync();
        lock (saves)
        {
            saves.ShouldBeEmpty();
        }

        // The provider was asked for the field's text, and the entry on the server is as it was.
        Host.Ai.Requests.ShouldContain(request => request.ToJsonString().Contains($"Ships are big boats {Mark}", StringComparison.Ordinal));
        var stored = await api.GetFromJsonAsync<JsonObject>(new Uri($"api/management/v1/content/article/{id:D}?status=draft", UriKind.Relative), Token);
        stored!["title"]!.GetValue<string>().ShouldBe($"Ships are big boats {Mark}");
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // Translate (#3513): the English title translated into French goes into the French draft, which the form moves to, as an
    // unsaved change that French's own Save adds; the English title stays as it was.
    [Fact]
    public async Task TheEditorTranslatesATitleIntoTheFrenchDraftAndSavesItThere()
    {
        using var api = await Host.ManagementClientAsync();
        var version = await OptInAsync(api);
        try
        {
            await TranslateAsync(api);
        }
        finally
        {
            await ResetAsync(api, version);
        }
    }

    private async Task TranslateAsync(HttpClient api)
    {
        var id = await CreateArticleAsync(api, $"Ships are big boats {Mark}");
        (Host.Ai.Pieces, Host.Ai.Gate) = ([$"Les navires sont grands {Mark}."], null);

        var browser = await OpenBrowserAsync();
        var tab = await SignedInTabAsync(browser, $"content/article/{id:D}");
        var title = tab.Page.Locator("[data-field='title'] input");
        await Expect(title).ToHaveValueAsync($"Ships are big boats {Mark}");

        var field = tab.Page.Locator("[data-field='title']");
        await field.Locator(".field-actions > fluent-button").ClickAsync();
        var translate = Ui.ButtonIn(field.Locator(".field-actions-list"), AiTexts["Ai.Action.translate"]);
        await Expect(translate).ToBeVisibleAsync();
        var panel = await tab.OpenDialogAsync(() => translate.ClickAsync(), AiTexts["Ai.Action.translate"]);

        // French is the one other locale of the site, so it is chosen already.
        await Expect(panel.GetByText(AiTexts["Ai.Panel.TargetLocale"])).ToBeVisibleAsync();
        await Ui.ButtonIn(panel, AiTexts["Ai.Panel.Suggest"]).ClickAsync();
        var accept = Ui.ButtonIn(panel, AiTexts["Ai.Panel.Accept"]);
        await Expect(accept).ToBeVisibleAsync();
        await Expect(panel.Locator(".ai-suggestion")).ToHaveTextAsync($"Les navires sont grands {Mark}.");
        await Expect(panel.Locator(".ai-suggestion")).ToHaveAttributeAsync("lang", "fr");
        await accept.ClickAsync();

        await Expect(tab.ShownDialog(AiTexts["Ai.Action.translate"])).ToBeHiddenAsync();
        await Expect(tab.Page).ToHaveURLAsync(new Regex($"/admin/content/article/{id:D}\\?locale=fr$"));
        await Expect(title).ToHaveValueAsync($"Les navires sont grands {Mark}.");
        await Expect(title).ToHaveAttributeAsync("lang", "fr");
        await Expect(tab.Announced(Ui.Format(Repository.AdminText("Content.FieldActions.ChangedIn"), AiTexts["Ai.Action.translate"], "Title", "fr"))).ToBeAttachedAsync();

        // The provider was asked for the English text into French, and nothing was saved yet.
        Host.Ai.Requests.ShouldContain(request => Said(request).Contains($"Ships are big boats {Mark}", StringComparison.Ordinal) && Said(request).Contains("locale \"fr\"", StringComparison.Ordinal));
        (await StoredTitleAsync(api, id, "fr")).ShouldBeNull();

        var saved = await tab.AnswerAsync(() => tab.Button("Content.Form.Save").ClickAsync(), "PUT", tab.ManagementUrl($"content/article/{id:D}"));
        saved.Status.ShouldBe(200);
        (await saved.Request.HeaderValueAsync("x-cms-locale")).ShouldBe("fr");
        (await StoredTitleAsync(api, id, "fr")).ShouldBe($"Les navires sont grands {Mark}.");
        (await StoredTitleAsync(api, id, "en")).ShouldBe($"Ships are big boats {Mark}");
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // Every text of a request to the provider, its messages among them, one after another.
    private static string Said(JsonNode? node) => node switch
    {
        JsonObject members => string.Join('\n', members.Select(member => Said(member.Value))),
        JsonArray items => string.Join('\n', items.Select(Said)),
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        _ => "",
    };

    // The draft's title in the locale, or null where the entry has no translation in it.
    private static async Task<string?> StoredTitleAsync(HttpClient api, Guid id, string locale)
    {
        using var read = new HttpRequestMessage(HttpMethod.Get, new Uri($"api/management/v1/content/article/{id:D}?status=draft&fallback=none", UriKind.Relative));
        read.Headers.TryAddWithoutValidation("X-Cms-Locale", locale);
        using var answered = await api.SendAsync(read, Token);
        if (answered.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        answered.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await answered.Content.ReadFromJsonAsync<JsonObject>(Token))!["title"]!.GetValue<string>();
    }

    // The default site names the run's stand-in provider and opts in the article's title, until the case resets it, so that
    // no other case meets an action on the field. The version of the settings it wrote is the answer.
    private async Task<int> OptInAsync(HttpClient api)
    {
        var read = await api.GetFromJsonAsync<JsonObject>(new Uri("api/management/v1/settings/ai", UriKind.Relative), Token);
        var version = read!["version"]!.GetValue<int>();
        using var write = new HttpRequestMessage(HttpMethod.Put, new Uri("api/management/v1/settings/ai", UriKind.Relative))
        {
            Content = JsonContent.Create(new JsonObject
            {
                ["provider"] = "openAiCompatible",
                ["model"] = "stub",
                ["endpoint"] = Host.Ai.Endpoint,
                ["fields"] = new JsonObject { ["article"] = new JsonArray("title") },
            }),
        };
        write.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        using var written = await api.SendAsync(write, Token);
        var answer = await written.Content.ReadAsStringAsync(Token);
        written.StatusCode.ShouldBe(HttpStatusCode.OK, answer);
        return JsonNode.Parse(answer)!["version"]!.GetValue<int>();
    }

    private static async Task ResetAsync(HttpClient api, int version)
    {
        using var reset = new HttpRequestMessage(HttpMethod.Delete, new Uri("api/management/v1/settings/ai", UriKind.Relative));
        reset.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        using var answered = await api.SendAsync(reset, Token);
        answered.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static async Task<Guid> CreateArticleAsync(HttpClient api, string title)
    {
        using var created = await api.PostAsJsonAsync(new Uri("api/management/v1/content/article", UriKind.Relative), new JsonObject { ["title"] = title }, Token);
        var answer = await created.Content.ReadAsStringAsync(Token);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, answer);
        return JsonNode.Parse(answer)!["id"]!.GetValue<Guid>();
    }
}
