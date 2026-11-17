using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The relation picker of A-15 for the built-in Editor on the Blog sample: the author of a new article chosen by search, its
// tags chosen and ordered by keys, by drag and by a button, an author created in a side panel and chosen without losing the
// article's changes, and a related entry opened in a side panel that gives focus back to the control that opened it.
public sealed class RelationTests(EndToEndHost host) : BrowserTest(host)
{
    private const string AuthorPicker = "Choose an entry: Author";
    private const string TagPicker = "Choose entries: Tag";

    [Fact]
    public async Task TheEditorSetsTheAuthorAndTheOrderedTagsOfAnArticle()
    {
        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        await tab.Page.GotoAsync(new Uri(Host.Admin, "content/article/new").AbsoluteUri);
        await browser.SignInOnTheHostedPageAsync(tab, Host.Editor);
        var title = tab.Page.Locator("[data-field='title'] input");
        await title.FillAsync("Related in the browser");

        // The author, by search: the index may still be filling, so the search is sent again until it finds her.
        await FieldButton(tab, "author", "Content.Relation.ChooseEntry").ClickAsync();
        var picker = Dialog(tab, AuthorPicker);
        await Expect(tab.Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = AuthorPicker, Exact = true })).ToBeVisibleAsync();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        do
        {
            var search = await AnswerAsync(tab, () => SearchAsync(picker, "Grace"), response => response.Url.Contains("/content/author?", StringComparison.Ordinal));
            search.Url.ShouldContain("q=Grace");
        }
        while (await Row(picker, "Grace Hopper").CountAsync() == 0 && DateTimeOffset.UtcNow < deadline && await Delay());
        await Row(picker, "Grace Hopper").Locator("input[type=checkbox]").CheckAsync();
        await DialogButton(picker, "Content.Relation.Choose").ClickAsync();
        await Expect(Labels(tab, "author")).ToHaveTextAsync(["Grace Hopper"]);

        // Two tags, then their order by Alt with an arrow, by drag and by a button.
        await FieldButton(tab, "tags", "Content.Relation.ChooseEntries").ClickAsync();
        var tags = Dialog(tab, TagPicker);
        await Expect(Row(tags, "release notes")).ToBeVisibleAsync();
        await Row(tags, "dotnet").Locator("input[type=checkbox]").CheckAsync();
        await Row(tags, "release notes").Locator("input[type=checkbox]").CheckAsync();
        await DialogButton(tags, "Content.Relation.Choose").ClickAsync();
        await Expect(Labels(tab, "tags")).ToHaveTextAsync(["dotnet", "release notes"]);

        // The closed picker gives focus back to the button that opened it, on the library's own timer; until it has, a key
        // pressed on an entry can land on that button instead.
        await Expect(FieldButton(tab, "tags", "Content.Relation.ChooseEntries")).ToBeFocusedAsync();
        await Entry(tab, "tags", "release notes").FocusAsync();
        await tab.Page.EvaluateAsync(TmpDiagnostic);
        await tab.Page.Keyboard.PressAsync("Alt+ArrowUp");
        await Expect(Labels(tab, "tags")).ToHaveTextAsync(["release notes", "dotnet"]);
        try
        {
            await Expect(Entry(tab, "tags", "release notes")).ToBeFocusedAsync();
        }
        catch (PlaywrightException exception)
        {
            throw new PlaywrightException("TMPDIAG " + await tab.Page.EvaluateAsync<string>("() => window.__diag()"), exception);
        }
        await Item(tab, "tags", "release notes").DragToAsync(Item(tab, "tags", "dotnet"));
        await Expect(Labels(tab, "tags")).ToHaveTextAsync(["dotnet", "release notes"]);
        await tab.Page.Locator($"[data-field='tags'] fluent-button[aria-label=\"{Text("Content.Relation.MoveDown", "dotnet")}\"]").ClickAsync();
        await Expect(Labels(tab, "tags")).ToHaveTextAsync(["release notes", "dotnet"]);

        // A new author, made in a side panel and chosen once saved; the article keeps what was typed.
        var create = FieldButton(tab, "author", "Content.Relation.Create", "Author");
        await create.ClickAsync();
        var panel = tab.Page.Locator("fluent-drawer[aria-label=\"Author\"]");
        await Expect(panel.Locator("h2")).ToHaveTextAsync(Text("Content.Form.NewHeading", "Author"));
        await panel.Locator("[data-field='name'] input").FillAsync("Hedy Lamarr");
        var created = await AnswerAsync(tab, () => PanelButton(panel, "Content.Form.Save").ClickAsync(), response => response.Request.Method == "POST" && response.Url == tab.ManagementUrl("content/author"));
        created.Status.ShouldBe(201);
        var location = (await created.HeaderValueAsync("location")).ShouldNotBeNull();
        var hedy = Guid.Parse(location[(location.LastIndexOf('/') + 1)..].Split('?')[0]);
        await Expect(Labels(tab, "author")).ToHaveTextAsync(["Hedy Lamarr"]);
        await PanelButton(panel, "Content.Panel.Close").ClickAsync();
        await Expect(tab.Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Author", Exact = true })).ToBeHiddenAsync();
        (await FocusedButtonAsync(tab)).ShouldBe(Text("Content.Relation.Create", "Author"));
        await Expect(title).ToHaveValueAsync("Related in the browser");

        var saved = await AnswerAsync(tab, () => tab.Page.Locator(".content-form-toolbar fluent-button").Filter(Exactly("Content.Form.Save")).First.ClickAsync(), response => response.Request.Method == "POST" && response.Url == tab.ManagementUrl("content/article"));
        saved.Status.ShouldBe(201);
        var sent = JsonNode.Parse(saved.Request.PostData!)!;
        sent["title"]!.GetValue<string>().ShouldBe("Related in the browser");
        sent["author"]!.GetValue<string>().ShouldBe(hedy.ToString("D"));
        sent["tags"]!.AsArray().Count.ShouldBe(2);

        // The related entry opens in a side panel and closes back to the control that opened it.
        await Entry(tab, "author", "Hedy Lamarr").ClickAsync();
        await Expect(panel.Locator("h2")).ToHaveTextAsync("Hedy Lamarr");
        await tab.Page.Keyboard.PressAsync("Escape");
        await Expect(tab.Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Author", Exact = true })).ToBeHiddenAsync();
        (await FocusedButtonAsync(tab)).ShouldStartWith("Hedy Lamarr");
        browser.PolicyViolations.ShouldBeEmpty();
    }

    private static string Text(string key, string argument) => Repository.AdminText(key).Replace("{0}", argument, StringComparison.Ordinal);

    private static LocatorFilterOptions Exactly(string key, string? argument = null) =>
        new() { HasTextRegex = new Regex($@"^\s*{Regex.Escape(argument is null ? Repository.AdminText(key) : Text(key, argument))}\s*$") };

    // Temporary: what takes focus after the move in CI, logged when the focus check fails.
    private const string TmpDiagnostic = """
        () => {
            const d = e => {
                if (!e) return 'null';
                const field = e.closest ? e.closest('[data-field]')?.dataset.field : undefined;
                const item = e.closest ? Array.from(document.querySelectorAll("[data-field='tags'] .relation-field-item")).indexOf(e.closest('.relation-field-item')) : -1;
                return e.tagName + (e.id ? '#' + e.id : '') + '[' + (e.getAttribute?.('aria-label') || (e.textContent || '').trim().replace(/\s+/g, ' ').slice(0, 24)) + '] field=' + field + ' item=' + item;
            };
            const deep = () => { let a = document.activeElement; while (a && a.shadowRoot && a.shadowRoot.activeElement) a = a.shadowRoot.activeElement; return a; };
            const t0 = performance.now();
            const log = [];
            const at = s => log.push(Math.round(performance.now() - t0) + 'ms ' + s);
            const focus = HTMLElement.prototype.focus;
            HTMLElement.prototype.focus = function (...args) { at('focus() ' + d(this) + ' from ' + (new Error().stack || '').split(String.fromCharCode(10)).slice(2, 4).map(x => x.trim()).join(' < ')); return focus.apply(this, args); };
            document.addEventListener('focusin', e => at('focusin ' + d(e.target)), true);
            document.addEventListener('focusout', e => at('focusout ' + d(e.target) + ' -> ' + d(e.relatedTarget)), true);
            document.addEventListener('keydown', e => at('keydown ' + e.key + (e.altKey ? '+alt' : '') + ' on ' + d(e.target)), true);
            new MutationObserver(m => at('mutations ' + m.length + ' ' + m.map(x => x.type + ':' + (x.target.tagName || '#text')).slice(0, 4).join(','))).observe(document.querySelector("[data-field='tags']"), { childList: true, subtree: true, attributes: true, characterData: true });
            window.__diag = () => 'active=' + d(document.activeElement) + ' deep=' + d(deep()) + ' hasFocus=' + document.hasFocus() + ' visibility=' + document.visibilityState + ' || ' + log.join(' || ');
        }
        """;

    private static ILocator FieldButton(AdminTab tab, string field, string key, string? argument = null) =>
        tab.Page.Locator($"[data-field='{field}'] .relation-field-actions fluent-button").Filter(Exactly(key, argument));

    private static ILocator Labels(AdminTab tab, string field) => tab.Page.Locator($"[data-field='{field}'] .relation-field-name");

    private static ILocator Item(AdminTab tab, string field, string label) =>
        tab.Page.Locator($"[data-field='{field}'] .relation-field-item").Filter(new LocatorFilterOptions { Has = tab.Page.Locator(".relation-field-name").GetByText(label, new LocatorGetByTextOptions { Exact = true }) });

    private static ILocator Entry(AdminTab tab, string field, string label) => Item(tab, field, label).Locator(".relation-field-entry fluent-button");

    // The library's dialog host has no box; its content is found through the host, as in FormBehaviourTests.
    private static ILocator Dialog(AdminTab tab, string title) => tab.Page.Locator($"fluent-dialog[aria-label=\"{title}\"]");

    private static ILocator DialogButton(ILocator dialog, string key) => dialog.Locator("fluent-button").Filter(Exactly(key));

    private static ILocator PanelButton(ILocator panel, string key) => panel.Locator("fluent-button").Filter(Exactly(key)).Last;

    private static ILocator Row(ILocator picker, string label) =>
        picker.Locator(".cms-data-grid tbody tr").Filter(new LocatorFilterOptions { HasText = label });

    private static async Task SearchAsync(ILocator picker, string text)
    {
        var box = picker.Locator("input[type=search]");
        await box.FillAsync(text);
        await box.PressAsync("Enter");
    }

    private static async Task<bool> Delay()
    {
        await Task.Delay(500);
        return true;
    }

    // The library moves focus back on its own timer, and the panel after it, so the check waits for a button to have it.
    private static async Task<string> FocusedButtonAsync(AdminTab tab)
    {
        await tab.Page.WaitForFunctionAsync("() => document.activeElement?.closest('fluent-button') != null", null, new PageWaitForFunctionOptions { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds });
        await Task.Delay(300);
        return await tab.Page.EvaluateAsync<string>("() => document.activeElement?.closest('fluent-button')?.textContent?.trim().replace(/\\s+/g, ' ') ?? ''");
    }

    private static Task<IResponse> AnswerAsync(AdminTab tab, Func<Task> action, Func<IResponse, bool> match) =>
        tab.Page.RunAndWaitForResponseAsync(action, match, new PageRunAndWaitForResponseOptions { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds });
}
