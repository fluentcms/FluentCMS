using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using FluentCMS.Admin.PseudoLocalization;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The accessibility sweep (spec 4.18, 7): axe-core checks every view of AccessibilityViews in the light and the dark theme,
// signed in once as a Super Admin and walking the views by the app's own navigation, and once more right to left under ar-XB,
// which needs a publish with the pseudo-cultures. A violation that AccessibilityExceptions does not cover fails the sweep,
// which names each one with its view, rule and element.
public sealed class AccessibilityTests(EndToEndHost host) : BrowserTest(host)
{
    private const string PreviewTemplate = EndToEndHost.PreviewOrigin + "/showcase/{id}?preview={token}";

    // A run's data, made once and shared by its sweeps; a second run of a case has a host of its own.
    private static readonly SemaphoreSlim Made = new(1, 1);
    private static (EndToEndHost Host, SweepData Data, Editor Administrator, Editor WithoutSite)? made;

    // The browsers of the users of no access and no site, signed in once a run and kept for its sweeps, which open a tab of
    // their own in them; the host's browser closes them at the end of the run.
    private static readonly Dictionary<string, (EndToEndHost Host, AdminBrowser Browser)> Others = new(StringComparer.Ordinal);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheCheckFailsAPageWithAKnownViolationAndNamesItsRuleAndElement()
    {
        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        await tab.Page.SetContentAsync("""
            <!doctype html>
            <html lang="en"><head><title>Known violation</title></head>
            <body><main><h1>Known violation</h1><img id="unnamed" src="data:image/gif;base64,R0lGODlhAQABAAAAACw="></main></body></html>
            """);

        var violation = (await Axe.ViolationsAsync(tab.Page, "known")).ShouldHaveSingleItem();

        violation.ShouldStartWith("known: image-alt (critical) at #unnamed:");
        violation.ShouldContain("https://dequeuniversity.com/rules/axe/");
        violation.ShouldContain("<img id=\"unnamed\"");
    }

    [Fact]
    public Task EveryViewPassesInTheLightTheme() => SweepAsync("light", culture: null);

    [Fact]
    public Task EveryViewPassesInTheDarkTheme() => SweepAsync("dark", culture: null);

    // The image ships English alone, so the run on the image leaves this case out by its trait.
    [Fact]
    [Trait("Host", "Publish")]
    public async Task EveryViewPassesRightToLeft()
    {
        Assert.SkipUnless(Repository.PseudoCultures, "The host is published without the pseudo-cultures: build with -p:FluentCmsAdminPseudoCultures=true.");
        Assert.SkipUnless(EndToEndHost.Culture == PseudoCulture.RightToLeft, $"The run's culture is {EndToEndHost.Culture}, not {PseudoCulture.RightToLeft}.");
        await SweepAsync("light", PseudoCulture.RightToLeft);
    }

    // The branding's accent as an operator sets it in config.json (spec 2.5): the color on a primary button is black or white,
    // whichever contrasts more, and its hover and pressed states are the accent mixed with the other of the two, in both
    // themes. The gallery shows every appearance of a button.
    [Fact]
    public async Task APrimaryButtonOnAnAccentOfTheBrandingContrastsInBothThemesAndItsStatesFollowTheAccent()
    {
        using var client = Host.Client();
        var configuration = await client.GetStringAsync(new Uri(Host.Admin, "config.json"), Token);
        var browser = await OpenBrowserAsync();
        var signedIn = false;
        foreach (var (accent, onAccent, shade) in new[] { ("#ffd400", "#000000", "#ffffff"), ("#00205b", "#ffffff", "#000000") })
        {
            foreach (var theme in new[] { "light", "dark" })
            {
                var tab = await browser.NewTabAsync();
                await PrepareAsync(tab.Page, theme, culture: null);
                var branded = JsonNode.Parse(configuration)!.AsObject();
                branded["branding"] = new JsonObject { ["accentColor"] = accent };
                await tab.Page.RouteAsync(new Uri(Host.Admin, "config.json").AbsoluteUri, route => route.FulfillAsync(new RouteFulfillOptions { Status = 200, ContentType = "application/json", Body = branded.ToJsonString() }));
                await tab.Page.GotoAsync(new Uri(Host.Admin, "system/components").AbsoluteUri);
                if (!signedIn)
                {
                    await browser.SignInOnTheHostedPageAsync(tab, await EditorAsync());
                    signedIn = true;
                }

                await Expect(tab.Page.Locator($".cms-root[data-cms-theme='{theme}']")).ToBeAttachedAsync();
                var button = tab.Page.Locator("main fluent-button[appearance='primary']:not([disabled])").First;
                await Expect(button).ToBeVisibleAsync();
                await tab.Page.Mouse.MoveAsync(0, 0);

                await ColorsAsync(button, Rgb(accent), Rgb(onAccent), $"{accent}, {theme}, at rest");
                await button.HoverAsync();
                await ColorsAsync(button, Mix(accent, shade, 0.85), Rgb(onAccent), $"{accent}, {theme}, hovered");
                await tab.Page.Mouse.DownAsync();
                await ColorsAsync(button, Mix(accent, shade, 0.70), Rgb(onAccent), $"{accent}, {theme}, pressed");
                await tab.Page.Mouse.MoveAsync(0, 0);
                await tab.Page.Mouse.UpAsync();
                await tab.Page.CloseAsync();
            }
        }

        browser.PolicyViolations.ShouldBeEmpty();
    }

    // The button's background and text as the browser draws them, once they are the expected ones, and their contrast.
    private static async Task ColorsAsync(ILocator button, (double R, double G, double B) background, (double R, double G, double B) text, string state)
    {
        var drawn = (Background: (0.0, 0.0, 0.0), Text: (0.0, 0.0, 0.0));
        await Ui.UntilAsync(
            async () =>
            {
                var colors = await button.EvaluateAsync<string[]>("element => { const style = getComputedStyle(element); return [style.backgroundColor, style.color]; }");
                drawn = (Parse(colors[0]), Parse(colors[1]));
                return Near(drawn.Background, background) && Near(drawn.Text, text);
            },
            $"the primary button draws {background} under {text} ({state})");
        Contrast(drawn.Background, drawn.Text).ShouldBeGreaterThanOrEqualTo(4.5, state);
    }

    private static bool Near((double R, double G, double B) a, (double R, double G, double B) b) =>
        Math.Abs(a.R - b.R) <= 2 && Math.Abs(a.G - b.G) <= 2 && Math.Abs(a.B - b.B) <= 2;

    private static (double R, double G, double B) Rgb(string hex) =>
        (Convert.ToInt32(hex[1..3], 16), Convert.ToInt32(hex[3..5], 16), Convert.ToInt32(hex[5..7], 16));

    // color-mix in srgb: the share of the first color, the rest of the second, per channel.
    private static (double R, double G, double B) Mix(string first, string second, double share)
    {
        var (a, b) = (Rgb(first), Rgb(second));
        return ((a.R * share) + (b.R * (1 - share)), (a.G * share) + (b.G * (1 - share)), (a.B * share) + (b.B * (1 - share)));
    }

    // rgb(r, g, b) as a browser writes a color, or color(srgb r g b) with channels from 0 to 1 as it writes a color-mix.
    private static (double R, double G, double B) Parse(string color)
    {
        var srgb = color.StartsWith("color(srgb", StringComparison.Ordinal);
        var channels = color[(color.IndexOf(srgb ? ' ' : '(', StringComparison.Ordinal) + 1)..color.IndexOf(')', StringComparison.Ordinal)]
            .Split([' ', ',', '/'], StringSplitOptions.RemoveEmptyEntries)
            .Take(3)
            .Select(channel => double.Parse(channel, System.Globalization.CultureInfo.InvariantCulture) * (srgb ? 255 : 1))
            .ToArray();
        return (channels[0], channels[1], channels[2]);
    }

    private static double Contrast((double R, double G, double B) a, (double R, double G, double B) b)
    {
        var (lighter, darker) = (Luminance(a), Luminance(b)) is var (x, y) && x > y ? (x, y) : (y, x);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance((double R, double G, double B) color) =>
        (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));

    private static double Linear(double channel) =>
        channel / 255 <= 0.04045 ? channel / 255 / 12.92 : Math.Pow(((channel / 255) + 0.055) / 1.055, 2.4);

    private async Task SweepAsync(string theme, string? culture)
    {
        var (data, administrator, withoutSite) = await DataAsync();
        var browser = await OpenBrowserAsync();
        var tab = await PreparedTabAsync(browser, theme, culture, "");
        await browser.SignInOnTheHostedPageAsync(tab, administrator);

        // The sign-in ends on the callback, which returns to the home page; the sweep starts once the shell is there.
        await Expect(tab.Page).ToHaveURLAsync(Host.Admin.AbsoluteUri);
        var sidebar = Repository.AdminText("Shell.Sidebar.Label");
        await Expect(tab.Page.GetByRole(AriaRole.Navigation, new PageGetByRoleOptions { Name = culture is null ? sidebar : PseudoCulture.Transform(sidebar, culture), Exact = true })).ToBeVisibleAsync();
        await Expect(tab.Page.Locator($".cms-root[data-cms-theme='{theme}']")).ToBeAttachedAsync();
        await Expect(tab.Page.Locator("html")).ToHaveAttributeAsync("dir", culture is null ? "ltr" : "rtl");
        await tab.Page.RouteAsync($"{EndToEndHost.PreviewOrigin}/**", route => route.FulfillAsync(new RouteFulfillOptions
        {
            Status = 200,
            ContentType = "text/html",
            Body = "<!doctype html><html lang=\"en\"><head><title>Preview</title></head><body><main><h1>Preview</h1></main></body></html>",
        }));

        var sweep = new Sweep(
            tab,
            data,
            culture,
            asEditor: async (route, heading) => await OtherUserAsync("no access", await EditorAsync(), theme, culture, route, heading),
            asUserWithoutSite: heading => OtherUserAsync("no site", withoutSite, theme, culture, "", heading),
            withBrokenConfiguration: async heading =>
            {
                var broken = await browser.NewTabAsync();
                await PrepareAsync(broken.Page, theme, culture);
                await broken.Page.RouteAsync(new Uri(Host.Admin, "config.json").AbsoluteUri, route => route.FulfillAsync(new RouteFulfillOptions { Status = 200, ContentType = "application/json", Body = "[]" }));
                await broken.Page.GotoAsync(Host.Admin.AbsoluteUri);
                await Expect(broken.Page.Locator("main.fluentcms-configuration-error h1")).ToHaveTextAsync(heading);
                return broken.Page;
            });

        var violations = new List<string>();
        foreach (var view in AccessibilityViews.All)
        {
            sweep.Checked = tab.Page;
            try
            {
                await view.OpenAsync(sweep);
                await sweep.LoadedAsync(sweep.Checked);
            }
            catch (PlaywrightException exception)
            {
                // A view the steps do not reach fails the sweep with the others, and the next view starts from the home page.
                violations.Add($"{view.Name}: not reached: {exception.Message.Split(Environment.NewLine)[0]}");
                await tab.Page.Keyboard.PressAsync("Escape");
                await sweep.GoAsync("", sweep.Heading(sweep.Admin("Home.Page.Heading")));
                continue;
            }

            violations.AddRange(await Axe.ViolationsAsync(sweep.Checked, view.Name));
            if (sweep.Checked != tab.Page)
            {
                await sweep.Checked.CloseAsync();
            }
            else if (view.Routes.Count == 0)
            {
                await CloseOverlaysAsync(tab.Page);
            }
        }

        violations.ShouldBeEmpty($"{violations.Count} violations in the {theme} theme{(culture is null ? "" : $" under {culture}")}:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
        browser.PolicyViolations.ShouldBeEmpty();
        Others.Values.SelectMany(other => other.Browser.PolicyViolations).ShouldBeEmpty();
    }

    // A tab of the user's browser in the sweep's theme and culture at the route, once its heading reads so. The browser is
    // made and signed in on the hosted page the first time a run asks for it; later tabs sign in silently on its session.
    private async Task<IPage> OtherUserAsync(string state, Editor user, string theme, string? culture, string route, string heading)
    {
        var known = Others.TryGetValue(state, out var other) && other.Host == Host;
        var browser = known ? other.Browser : await Host.OpenBrowserAsync();
        Others[state] = (Host, browser);
        var tab = await PreparedTabAsync(browser, theme, culture, route);
        if (!known)
        {
            await browser.SignInOnTheHostedPageAsync(tab, user);
        }

        await Expect(Sweep.HeadingOf(tab.Page, heading)).ToBeVisibleAsync();
        return tab.Page;
    }

    // A dialog, a drawer or a panel the view opened closes on Escape, as a keyboard user closes it.
    private static async Task CloseOverlaysAsync(IPage page)
    {
        await page.Keyboard.PressAsync("Escape");
        await Expect(page.GetByRole(AriaRole.Dialog)).ToHaveCountAsync(0);
    }

    // A tab whose admin starts in the theme, in the run's default culture when one is given, as a browser in a language the
    // admin does not offer does, and with reduced motion, so that no transition is caught half-way.
    private async Task<AdminTab> PreparedTabAsync(AdminBrowser browser, string theme, string? culture, string route)
    {
        var tab = await browser.NewTabAsync();
        await PrepareAsync(tab.Page, theme, culture);
        await tab.Page.GotoAsync(new Uri(Host.Admin, route).AbsoluteUri);
        return tab;
    }

    private static async Task PrepareAsync(IPage page, string theme, string? culture)
    {
        await page.EmulateMediaAsync(new PageEmulateMediaOptions { ReducedMotion = ReducedMotion.Reduce });
        await page.AddInitScriptAsync($"localStorage.setItem('fluentcms-admin/theme', '{theme}');");
        if (culture is not null)
        {
            await page.AddInitScriptAsync("""
                Object.defineProperty(Navigator.prototype, 'languages', { get: () => ['de-DE'] });
                Object.defineProperty(Navigator.prototype, 'language', { get: () => 'de-DE' });
                """);
        }
    }

    // A user whose one role grants nothing in a site, so that the admin finds no site for them.
    private async Task<Editor> UserWithoutSiteAsync()
    {
        var role = $"Nothing {Mark}";
        using (var management = await Host.ManagementClientAsync())
        {
            using var created = await management.PostAsJsonAsync(
                new Uri("api/management/v1/roles", UriKind.Relative),
                new JsonObject { ["name"] = role, ["siteId"] = Host.Sites[0].Id, ["grants"] = new JsonArray() },
                Token);
            created.EnsureSuccessStatusCode();
        }

        return await Host.CreateEditorAsync(role, site: "default");
    }

    private async Task<(SweepData Data, Editor Administrator, Editor WithoutSite)> DataAsync()
    {
        await Made.WaitAsync(Token);
        try
        {
            if (made is { } done && done.Host == Host)
            {
                return (done.Data, done.Administrator, done.WithoutSite);
            }

            var (data, administrator) = await MakeAsync();
            var withoutSite = await UserWithoutSiteAsync();
            made = (Host, data, administrator, withoutSite);
            return (data, administrator, withoutSite);
        }
        finally
        {
            Made.Release();
        }
    }

    // The data the views show: the Blog sample's welcome entry, a showcase entry with a value in each field and a preview
    // template, the Super Admin the sweep signs in as, a role, a key, a member and a member group, an image preset, a webhook
    // with the job of its first delivery, an MCP client and prompt.
    private async Task<(SweepData Data, Editor Administrator)> MakeAsync()
    {
        using var api = await Host.ManagementClientAsync();
        var mark = Mark;

        var articles = await GetAsync(api, "content/article?pageSize=100");
        var article = articles["items"]!.AsArray().OfType<JsonObject>()
            .First(entry => ((string?)entry["fields"]?["title"] ?? (string?)entry["title"]) == "Welcome to FluentCMS");

        await SetPreviewTemplateAsync(api);
        var showcase = await PostAsync(api, "content/showcase", new JsonObject
        {
            ["title"] = $"Showcase {mark}",
            ["rating"] = 4,
            ["featured"] = true,
            ["opensOn"] = "2026-12-01",
            ["contact"] = $"{mark}@showcase.test",
            ["notes"] = "Two lines," + Environment.NewLine + "of notes.",
            ["extra"] = new JsonObject { ["campaign"] = "winter", ["weight"] = 2 },
        });

        var administrator = await Host.CreateEditorAsync("Super Admin");
        var users = await GetAsync(api, $"users?search={Uri.EscapeDataString(administrator.Email)}");
        var user = users["items"]!.AsArray().OfType<JsonObject>().Single(item => (string?)item["email"] == administrator.Email);

        var role = (await GetAsync(api, "roles"))["items"]!.AsArray().OfType<JsonObject>().First(item => (string?)item["name"] == "Editor");
        var key = (await GetAsync(api, "api-keys"))["items"]!.AsArray().OfType<JsonObject>().First();
        var memberName = $"Member {mark}";
        var member = await PostAsync(api, "members", new JsonObject { ["email"] = $"{mark}@members.test", ["displayName"] = memberName });
        var reader = (await GetAsync(api, "roles"))["items"]!.AsArray().OfType<JsonObject>().First(item => (string?)item["name"] == "Reader");
        var groupName = $"group-{mark}";
        await PostAsync(api, "member-groups", new JsonObject { ["name"] = groupName, ["description"] = "A group of the accessibility sweep.", ["roleId"] = Id(reader).ToString("D") });
        using (var preset = await api.PutAsJsonAsync(new Uri($"api/management/v1/media/presets/sweep-{mark}", UriKind.Relative), new JsonObject { ["width"] = 320 }, Token))
        {
            preset.IsSuccessStatusCode.ShouldBeTrue(await preset.Content.ReadAsStringAsync(Token));
        }

        var webhookName = $"Sweep {mark}";
        var webhook = await PostAsync(api, "webhooks", new JsonObject
        {
            ["name"] = webhookName,
            ["url"] = $"https://{mark}.invalid/hooks",
            ["events"] = new JsonArray("content.draft_saved"),
        });
        await PostAsync(api, "content/article", new JsonObject { ["title"] = $"Sweep {mark}" });
        JsonObject? job = null;
        await Ui.UntilAsync(
            async () =>
            {
                job = (await GetAsync(api, "jobs?type=webhooks.deliver&pageSize=1"))["items"]!.AsArray().OfType<JsonObject>().FirstOrDefault();
                return job is not null;
            },
            "a delivery of the webhook is a job");

        await PostAsync(api, "mcp/clients", new JsonObject { ["name"] = $"Sweep {mark}", ["redirectUris"] = new JsonArray("http://127.0.0.1:41000/callback"), ["role"] = "Editor" });
        await PostAsync(api, "mcp/prompts", new JsonObject
        {
            ["name"] = $"sweep-{mark}",
            ["title"] = $"Sweep {mark}",
            ["description"] = "A prompt of the accessibility sweep.",
            ["arguments"] = new JsonArray(),
            ["template"] = "Summarise the entry.",
        });

        var site = Host.Sites[0];
        var data = new SweepData(
            Article: article["id"]!.GetValue<Guid>(),
            Showcase: Id(showcase),
            User: Id(user),
            UserName: (string?)user["userName"] ?? administrator.Email,
            Role: Id(role),
            RoleName: (string)role["name"]!,
            ApiKey: Id(key),
            ApiKeyName: (string)key["name"]!,
            Member: Id(member),
            MemberName: memberName,
            MemberGroupName: groupName,
            Job: Id(job!),
            JobType: (string)job!["type"]!,
            Webhook: Id(webhook),
            WebhookName: webhookName,
            Site: site.Id,
            SiteName: site.Name);
        return (data, administrator);
    }

    private static Guid Id(JsonObject item) => item["id"]!.GetValue<Guid>();

    private static async Task<JsonObject> GetAsync(HttpClient api, string path)
    {
        using var read = await api.GetAsync(new Uri("api/management/v1/" + path, UriKind.Relative), Token);
        var answer = await read.Content.ReadAsStringAsync(Token);
        read.StatusCode.ShouldBe(HttpStatusCode.OK, answer);
        return JsonNode.Parse(answer)!.AsObject();
    }

    private static async Task<JsonObject> PostAsync(HttpClient api, string path, JsonObject body)
    {
        using var created = await api.PostAsJsonAsync(new Uri("api/management/v1/" + path, UriKind.Relative), body, Token);
        var answer = await created.Content.ReadAsStringAsync(Token);
        created.IsSuccessStatusCode.ShouldBeTrue(answer);
        return JsonNode.Parse(answer)!.AsObject();
    }

    // The instance's preview template of the showcase type, the same that PreviewTests writes.
    private static async Task SetPreviewTemplateAsync(HttpClient api)
    {
        const string area = "api/management/v1/settings/preview/defaults";
        using var read = await api.GetAsync(new Uri(area, UriKind.Relative), Token);
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = new JsonObject { ["templates"] = new JsonObject { ["showcase"] = PreviewTemplate } }.ToJsonString();
        using var write = new HttpRequestMessage(HttpMethod.Put, new Uri(area, UriKind.Relative)) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        write.Headers.IfMatch.Add(read.Headers.ETag ?? new EntityTagHeaderValue("\"0\""));
        using var written = await api.SendAsync(write, Token);
        written.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
