using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentCMS.Admin.PseudoLocalization;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The admin in the pseudo-cultures of a publish that has them (spec 4.17, 7): the run's default culture, ar-XB, for a browser
// whose language the admin does not offer, right to left with every text of the shell marked, and the satellite assemblies
// of that culture alone fetched, a lazy assembly's when it loads; and the component gallery under en-XA with no unmarked text
// of the library, shadow roots included. The image ships without the pseudo-cultures, so the run on the image leaves these
// cases out by their trait (run-with-retry.ps1 -ExcludeTrait Host=Publish) and the run on a publish has them.
[Trait("Host", "Publish")]
public sealed partial class PseudoCultureTests(EndToEndHost host) : BrowserTest(host)
{
    // The texts of the page, shadow roots included, and the text attributes, as the component tests' check reads them, each
    // with the element it is in: a text is marked when no letter is left once its marked segments are taken out.
    private const string Texts = """
        root => {
            const attributes = ['aria-label', 'aria-description', 'aria-valuetext', 'aria-roledescription', 'title', 'placeholder', 'alt', 'label'];
            const found = [];
            const describe = element => {
                if (!element) return '';
                const classes = typeof element.className === 'string' && element.className.trim() ? '.' + element.className.trim().split(' ').filter(name => name).join('.') : '';
                const parent = element.parentElement ? element.parentElement.localName + ' > ' : '';
                return parent + element.localName + classes;
            };
            const visit = (node, owner) => {
                if (node.nodeType === Node.TEXT_NODE) {
                    const text = node.data.trim();
                    if (text) found.push({ text, attribute: '', owner, element: describe(node.parentElement) });
                    return;
                }
                if (node.nodeType === Node.ELEMENT_NODE) {
                    if (node.localName === 'style' || node.localName === 'script') return;
                    for (const name of attributes) {
                        const value = node.getAttribute(name);
                        if (value && value.trim()) found.push({ text: value.trim(), attribute: name, owner, element: describe(node) });
                    }
                    if (node.shadowRoot) visit(node.shadowRoot, node.localName);
                }
                for (const child of node.childNodes) visit(child, owner);
            };
            visit(document.querySelector(root), 'document');
            return found;
        }
        """;

    // The browser's language the admin does not offer, so that it starts in the run's default culture.
    private const string Unoffered = "de-DE";

    [Fact]
    public async Task TheDefaultCultureRunsTheAdminRightToLeftAndEveryTextOfTheShellIsMarked()
    {
        Assert.SkipUnless(Repository.PseudoCultures, "The host is published without the pseudo-cultures: build with -p:FluentCmsAdminPseudoCultures=true.");
        Assert.SkipUnless(EndToEndHost.Culture == PseudoCulture.RightToLeft, $"The run's culture is {EndToEndHost.Culture}, not {PseudoCulture.RightToLeft}.");
        var browser = await OpenBrowserAsync();
        var tab = await SignedInTabAsync(browser, "");
        await Expect(tab.Sidebar).ToBeVisibleAsync();
        var satellites = Satellites(tab);

        await StartInAsync(tab, Unoffered);

        await Expect(tab.Page.Locator("html")).ToHaveAttributeAsync("dir", "rtl");
        await Expect(tab.Page.Locator("html")).ToHaveAttributeAsync("lang", PseudoCulture.RightToLeft);
        var sidebar = Sidebar(tab, PseudoCulture.RightToLeft);
        await Expect(sidebar).ToBeVisibleAsync();
        satellites.ShouldContain(culture => culture == PseudoCulture.RightToLeft);
        satellites.ShouldNotContain(culture => culture == PseudoCulture.LeftToRight);
        var data = await DataAsync();

        // The badge of a pending schedule names the user's time zone by its id, the browser's for a user who chose none.
        data.Add(await tab.Page.EvaluateAsync<string>("() => Intl.DateTimeFormat().resolvedOptions().timeZone"));

        await sidebar.Locator("a[href$='content/article']").ClickAsync();
        var grid = tab.Page.Locator("main table");
        await Expect(grid.Locator("tbody tr").First).ToBeVisibleAsync();
        (await UnmarkedAsync(tab, data)).ShouldBeEmpty();

        await grid.Locator("tbody tr a[href*='content/article/']").First.ClickAsync();
        await Expect(tab.Page.Locator("main .content-form-toolbar")).ToBeVisibleAsync();
        (await UnmarkedAsync(tab, data)).ShouldBeEmpty();

        // The gallery is in FluentCMS.Admin.Administration, which loads lazily, with its texts in the culture.
        await tab.Page.GotoAsync(new Uri(Host.Admin, "system/components").AbsoluteUri);
        await Expect(tab.Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = Marked(Repository.AdministrationText("Gallery", "System.Components.Heading"), PseudoCulture.RightToLeft), Exact = true }))
            .ToBeVisibleAsync();
        satellites.ShouldNotContain(culture => culture == PseudoCulture.LeftToRight);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    [Fact]
    public async Task NoWrappedComponentOfTheGalleryShowsAnUnmarkedTextOfTheLibrary()
    {
        Assert.SkipUnless(Repository.PseudoCultures, "The host is published without the pseudo-cultures: build with -p:FluentCmsAdminPseudoCultures=true.");
        var browser = await OpenBrowserAsync();
        var tab = await SignedInTabAsync(browser, "system/components");
        await Expect(tab.Sidebar).ToBeVisibleAsync();

        await StartInAsync(tab, PseudoCulture.LeftToRight);

        await Expect(tab.Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = Marked(Repository.AdministrationText("Gallery", "System.Components.Heading"), PseudoCulture.LeftToRight), Exact = true }))
            .ToBeVisibleAsync();
        await Expect(Sidebar(tab, PseudoCulture.LeftToRight)).ToBeVisibleAsync();
        (await UnmarkedAsync(tab, await DataAsync())).ShouldBeEmpty();
        browser.PolicyViolations.ShouldBeEmpty();
    }

    private static string Marked(string text, string culture) => PseudoCulture.Transform(text, culture);

    private static ILocator Sidebar(AdminTab tab, string culture) =>
        tab.Page.GetByRole(AriaRole.Navigation, new PageGetByRoleOptions { Name = Marked(Repository.AdminText("Shell.Sidebar.Label"), culture), Exact = true });

    // The tab starts again with the browser's languages given, as a browser in that language starts the admin; the tab's
    // session keeps it signed in.
    private static async Task StartInAsync(AdminTab tab, string language)
    {
        await tab.Page.AddInitScriptAsync($$"""
            Object.defineProperty(Navigator.prototype, 'languages', { get: () => ['{{language}}'] });
            Object.defineProperty(Navigator.prototype, 'language', { get: () => '{{language}}' });
            """);
        await tab.ReloadAsync();
    }

    // The cultures whose satellite assemblies the tab fetches, from the folder of each under _framework.
    private static ConcurrentBag<string> Satellites(AdminTab tab)
    {
        var cultures = new ConcurrentBag<string>();
        tab.Page.Request += (_, request) =>
        {
            var segments = new Uri(request.Url).AbsolutePath.Split('/');
            if (segments is [.., "_framework", var culture, var file] && file.Contains(".resources.", StringComparison.Ordinal))
            {
                cultures.Add(culture);
            }
        };
        return cultures;
    }

    // What the server holds that a page shows as it is: the names of the sites, every string of the schema as it is and as
    // the admin labels a name, every string of the entries of each collection and of the media library, which the form shows
    // as the labels of its relation and media fields once it has read them, the sites' locales by code and name, and the
    // user's address; and ids.
    private async Task<List<string>> DataAsync()
    {
        using var client = await Host.ManagementClientAsync();
        var schema = await client.GetFromJsonAsync<JsonObject>(new Uri("api/management/v1/schema", UriKind.Relative), TestContext.Current.CancellationToken);
        var data = new List<string> { (await EditorAsync()).Email };
        data.AddRange(Host.Sites.Select(site => site.Name));
        foreach (var text in Strings(schema!))
        {
            data.AddRange([text, Readable(text), Words(text)]);
        }

        var collections = schema!["types"]!.AsArray().OfType<JsonObject>().Where(type => (string?)type["kind"] == "collection").Select(type => (string)type["name"]!);
        foreach (var path in collections.Select(name => $"content/{name}?pageSize=100").Append("media?pageSize=100"))
        {
            var page = await client.GetFromJsonAsync<JsonObject>(new Uri($"api/management/v1/{path}", UriKind.Relative), TestContext.Current.CancellationToken);
            data.AddRange(Strings(page!["items"]!));
        }
        foreach (var site in Host.Sites)
        {
            var locales = await client.GetFromJsonAsync<JsonObject>(new Uri($"api/management/v1/sites/{site.Id}/locales", UriKind.Relative), TestContext.Current.CancellationToken);
            foreach (var code in locales!["items"]!.AsArray().OfType<JsonObject>().Select(locale => locale["code"]!.GetValue<string>()))
            {
                data.Add(code);
                data.Add(CultureInfo.GetCultureInfo(code).NativeName);
            }
        }

        return [.. data.Where(text => text.Length > 0).Distinct(StringComparer.Ordinal).OrderByDescending(text => text.Length)];
    }

    // The texts of the document that are not marked once the server's data is taken out of them, each with the element it is
    // in and the element whose shadow root holds it, for a text the library draws in script.
    private static async Task<List<string>> UnmarkedAsync(AdminTab tab, List<string> data)
    {
        var found = await tab.Page.EvaluateAsync<Found[]>(Texts, "body");
        return [.. found.Where(item => !IsMarked(item.Text, data)).Select(Describe).Distinct()];

        static string Describe(Found item)
        {
            var text = item.Attribute.Length == 0 ? item.Text : $"{item.Attribute}=\"{item.Text}\"";
            return $"{(item.Owner == "document" ? "" : item.Owner + ": ")}{text} (in {item.Element})";
        }
    }

    [GeneratedRegex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase)]
    private static partial Regex Ids();

    private static bool IsMarked(string text, List<string> data)
    {
        var rest = Ids().Replace(data.Aggregate(text, (left, value) => left.Replace(value, "", StringComparison.Ordinal)), "");
        var depth = 0;
        foreach (var c in rest)
        {
            if (c == PseudoCulture.Open)
            {
                depth++;
            }
            else if (c == PseudoCulture.Close && depth > 0)
            {
                depth--;
            }
            else if (depth == 0 && char.IsLetter(c))
            {
                return false;
            }
        }

        return true;
    }

    // Every string of the schema, which is data from its names to the labels of a select's options.
    private static IEnumerable<string> Strings(JsonNode node) => node switch
    {
        JsonObject members => members.SelectMany(member => member.Value is null ? [] : Strings(member.Value)),
        JsonArray items => items.SelectMany(item => item is null ? [] : Strings(item)),
        JsonValue value when value.TryGetValue<string>(out var text) => [text],
        _ => [],
    };

    // A name as the admin shows it without a label: kebab-case as words with a capital, camelCase as words in lower case.
    private static string Readable(string name)
    {
        var words = name.Replace('-', ' ').Trim();
        return words.Length == 0 ? name : char.ToUpperInvariant(words[0]) + words[1..];
    }

    private static string Words(string name) =>
        Readable(string.Concat(name.Select((c, index) => char.IsUpper(c) && index > 0 ? " " + char.ToLowerInvariant(c) : c.ToString())));

    // Read by Playwright, which sets its properties.
    private sealed class Found
    {
        public string Text { get; set; } = "";

        public string Attribute { get; set; } = "";

        public string Owner { get; set; } = "";

        public string Element { get; set; } = "";
    }
}
