using System.Text.RegularExpressions;
using FluentCMS.Admin.PseudoLocalization;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// A view of the accessibility sweep: its name in a failure, the route templates of the app it covers, and the steps that
// bring it to a state with content, from wherever the view before it left the tab. A view without routes is a state of a
// page, such as an open dialog, or a state without a page of its own.
public sealed record AccessibilityView(string Name, IReadOnlyList<string> Routes, Func<Sweep, Task> OpenAsync);

// Every view of the admin, in the order the sweep walks them (spec 4.18). AccessibilityViewsTests compares their routes
// with the app's.
public static class AccessibilityViews
{
    private const string Media = "plugins/FluentCMS.Plugins.Media.Admin/MediaResources.resx";
    private const string Webhooks = "plugins/FluentCMS.Plugins.Webhooks.Admin/WebhooksResources.resx";
    private const string Mcp = "plugins/FluentCMS.Plugins.Mcp.Admin/McpResources.resx";
    private const string Ai = "plugins/FluentCMS.Plugins.AiAssist.Admin/AiAssistResources.resx";
    private const string MultiSite = "plugins/FluentCMS.Plugins.MultiSite.Admin/MultiSiteResources.resx";
    private const string Search = "plugins/FluentCMS.Plugins.Search.Admin/SearchResources.resx";

    public static readonly IReadOnlyList<AccessibilityView> All =
    [
        new("home", ["/"], s => s.GoAsync("", s.Heading(s.Admin("Home.Page.Heading")))),
        new("not found", ["/not-found"], s => s.GoAsync("not-found", s.Heading(s.Admin("Shell.NotFound.Heading")))),
        new("account", ["/account"], s => s.GoAsync("account", s.Heading(s.AreaText("Account", "Account.Page.Heading")))),

        new("content list", ["/content/{Type}"], async s =>
        {
            await s.GoAsync("content/article", s.Page.Locator("main [role=grid] tbody tr").First);
        }),
        new("bulk dialog", [], async s =>
        {
            await s.Page.Locator("main [role=grid] thead button[role=checkbox]").ClickAsync();
            await s.Page.Locator(".content-list-selection").GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = s.Admin("Content.Bulk.Menu"), Exact = true }).ClickAsync();
            await s.Page.GetByRole(AriaRole.Menuitem, new PageGetByRoleOptions { Name = s.Admin("Content.Bulk.Publish"), Exact = true }).ClickAsync();
            await s.DialogAsync();
        }),
        new("new entry", ["/content/{Type}/new"], async s =>
        {
            await s.GoAsync("content/landing/new", s.Page.Locator("main [data-field='sections']"));
            await Expect(s.Page.Locator("main .content-form-toolbar")).ToBeVisibleAsync();
        }),
        new("entry form", ["/content/{Type}/{Id:guid}"], async s =>
        {
            await s.GoAsync($"content/article/{s.Data.Article:D}", s.Page.Locator("main [data-field='body'] [role=textbox]"));
            await Expect(s.Page.Locator("main [data-field='title'] input")).Not.ToHaveValueAsync("");
            await Expect(s.Page.Locator("main [data-field='hero'] .media-field-actions")).ToBeVisibleAsync();
        }),
        new("rich text link panel", [], async s =>
        {
            await Ui.PressAsync(s.Page.Locator("main [data-field='body'] [role=textbox]"), "Control+k");
            await Expect(s.Page.GetByLabel(s.Mark(Repository.RichTextText("RichText.Link.Address")), new PageGetByLabelOptions { Exact = true })).ToBeVisibleAsync();
        }),
        new("relation picker", [], async s =>
        {
            await s.Page.Locator("[data-field='author'] .relation-field-actions fluent-button").Filter(Ui.Exactly(s.Admin("Content.Relation.ChooseEntry"))).ClickAsync();
            await s.DialogAsync();
            await Expect(s.Page.Locator("fluent-dialog .cms-data-grid tbody tr").First).ToBeVisibleAsync();
        }),
        new("media picker", [], async s =>
        {
            await s.Page.Locator("[data-field='hero'] .media-field-actions fluent-button").Filter(Ui.Exactly(s.Text(Media, "Media.Field.Choose"))).ClickAsync();
            await s.DialogAsync();
            await Expect(s.Page.Locator("fluent-dialog .media-tile-name").First).ToBeVisibleAsync();
        }),
        new("schedule dialog", [], async s =>
        {
            await Ui.PressAsync(Ui.ButtonIn(s.Page.Locator(".content-schedule"), s.Admin("Content.Schedule.Open")), "Enter");
            await s.DialogAsync();
        }),
        new("version panel", [], async s =>
        {
            await Ui.PressAsync(Ui.ButtonIn(s.Page.Locator(".content-form-history"), s.Admin("Content.Versions.Open")), "Enter");
            await Expect(s.Page.Locator($"fluent-drawer[aria-label=\"{s.Admin("Content.Versions.Title")}\"] .version-list tbody tr").First).ToBeVisibleAsync();
        }),
        // The test's showcase type has the field types the Blog sample has not: integer, boolean, date, email, long text and json.
        new("showcase form", [], async s =>
        {
            await s.GoAsync($"content/showcase/{s.Data.Showcase:D}", s.Page.Locator("main [data-field='extra']"));
            await Expect(s.Page.Locator("main [data-field='rating'] input")).Not.ToHaveValueAsync("");
        }),
        new("preview pane", [], async s =>
        {
            await Ui.PressAsync(Ui.ButtonIn(s.Page.Locator(".content-form-history"), s.Admin("Content.Preview.Open")), "Enter");
            await Expect(s.Page.Locator(".content-preview iframe")).ToHaveAttributeAsync("src", new Regex($@"/showcase/{s.Data.Showcase:D}\?preview="));
        }),

        new("media library", ["/media"], async s =>
        {
            await s.GoAsync("media", s.Heading(s.Text(Media, "Media.Page.Heading")));
            await Expect(s.Page.Locator("main .media-tile-name").First).ToBeVisibleAsync();
        }),
        new("asset detail", [], async s =>
        {
            await s.Page.Locator("main .media-tile-open").First.ClickAsync();
            await Expect(s.Page.Locator(".cms-drawer").GetByLabel(s.Text(Media, "Media.Form.Title"), new LocatorGetByLabelOptions { Exact = true })).ToBeVisibleAsync();
        }),
        new("media presets", ["/media/presets"], async s =>
        {
            await s.GoAsync("media/presets", s.Heading(s.Text(Media, "Media.Presets.Heading")));
            await Expect(s.Page.Locator("main .media-presets-table tbody tr").First).ToBeVisibleAsync();
        }),

        new("users", ["/users"], async s =>
        {
            await s.GoAsync("users", s.Heading(s.Area("Users")));
            await Expect(s.Page.Locator("main table tbody tr").First).ToBeVisibleAsync();
        }),
        new("user", ["/users/{Id:guid}"], s => s.GoAsync($"users/{s.Data.User:D}", s.Heading(s.Data.UserName))),
        new("roles", ["/roles"], async s =>
        {
            await s.GoAsync("roles", s.Heading(s.Area("Roles")));
            await Expect(s.Page.Locator("main tbody tr").First).ToBeVisibleAsync();
        }),
        new("new role", ["/roles/new"], s => s.GoAsync("roles/new", s.Heading(s.AreaText("Roles", "Roles.Editor.NewHeading")))),
        new("role", ["/roles/{Id:guid}"], async s =>
        {
            await s.GoAsync($"roles/{s.Data.Role:D}", s.Heading(s.Data.RoleName));
            await Expect(s.Page.Locator("main [data-grant]").First).ToBeVisibleAsync();
        }),
        new("api keys", ["/api-keys"], async s =>
        {
            await s.GoAsync("api-keys", s.Heading(s.Area("ApiKeys")));
            await Expect(s.Page.Locator("main tbody tr").First).ToBeVisibleAsync();
        }),
        new("new api key", ["/api-keys/new"], s => s.GoAsync("api-keys/new", s.Heading(s.AreaText("ApiKeys", "ApiKeys.New.Heading")))),
        new("api key", ["/api-keys/{Id:guid}"], s => s.GoAsync($"api-keys/{s.Data.ApiKey:D}", s.Heading(s.Data.ApiKeyName))),
        new("members", ["/members"], async s =>
        {
            await s.GoAsync("members", s.Heading(s.Area("Members")));
            await Expect(s.Page.Locator("main table tbody tr").First).ToBeVisibleAsync();
        }),
        new("member groups", ["/members/groups"], async s =>
        {
            await s.GoAsync("members/groups", s.Heading(s.AreaText("Members", "Members.GroupsPage.Heading")));
            await Expect(s.Page.Locator("main li").Filter(new LocatorFilterOptions { HasText = s.Data.MemberGroupName }).First).ToBeVisibleAsync();
        }),
        new("member", ["/members/{Id:guid}"], s => s.GoAsync($"members/{s.Data.Member:D}", s.Heading(s.Data.MemberName))),
        new("sites", ["/sites"], async s =>
        {
            await s.GoAsync("sites", s.Heading(s.Area("Sites")));
            await Expect(s.Page.Locator("main table.sites-locales tbody th").First).ToBeVisibleAsync();
        }),
        new("plugins", ["/plugins"], s => s.GoAsync("plugins", s.Heading(s.Area("Plugins")))),
        new("jobs", ["/jobs"], async s =>
        {
            await s.GoAsync("jobs", s.Heading(s.Area("Jobs")));
            await Expect(s.Page.Locator("main [role=grid] tbody tr").First).ToBeVisibleAsync();
        }),
        new("job", ["/jobs/{Id:guid}"], async s =>
        {
            await s.GoAsync($"jobs/{s.Data.Job:D}", s.Heading(s.Data.JobType));
            await Expect(s.Page.Locator("main dl.jobs-facts")).ToBeVisibleAsync();
        }),
        new("diagnostics", ["/system/diagnostics"], async s =>
        {
            await s.GoAsync("system/diagnostics", s.Heading(s.Area("Diagnostics")));
            await Expect(s.Page.GetByRole(AriaRole.Region, new PageGetByRoleOptions { Name = s.AreaText("Diagnostics", "Diagnostics.Health.Heading"), Exact = true }).Locator("tbody tr").First).ToBeVisibleAsync();
        }),
        new("settings", ["/settings"], async s =>
        {
            await s.GoAsync("settings", s.Heading(s.Area("Settings")));
            await Expect(s.Page.Locator("main ul.settings-areas li").First).ToBeVisibleAsync();
        }),
        new("settings area", ["/settings/{Area}"], async s =>
        {
            await s.GoAsync("settings/blog", s.Scope(s.AreaText("Settings", "Settings.Area.Site")));
            await Expect(s.Page.Locator("main .settings-source").First).ToBeVisibleAsync();
        }),
        new("settings defaults", ["/settings/{Area}/defaults"], async s =>
        {
            await s.GoAsync("settings/blog/defaults", s.Scope(s.AreaText("Settings", "Settings.Area.Defaults")));
            await Expect(s.Page.Locator("main .settings-source").First).ToBeVisibleAsync();
        }),

        new("webhooks", ["/webhooks"], async s =>
        {
            await s.GoAsync("webhooks", s.Heading(s.Text(Webhooks, "Webhooks.Page.Heading")));
            await Expect(s.Page.Locator("main table.webhooks-table tbody tr").First).ToBeVisibleAsync();
        }),
        new("webhook", ["/webhooks/{Id:guid}"], async s =>
        {
            await s.GoAsync($"webhooks/{s.Data.Webhook:D}", s.Heading(s.Data.WebhookName));
            await Expect(s.Page.Locator("main table.webhooks-log tbody tr").First).ToBeVisibleAsync();
        }),
        new("mcp clients", ["/mcp/clients"], async s =>
        {
            await s.GoAsync("mcp/clients", s.Heading(s.Text(Mcp, "Mcp.Clients.Page.Heading")));
            await Expect(s.Page.Locator("main table.mcp-clients tbody tr").First).ToBeVisibleAsync();
        }),
        new("mcp instructions", ["/mcp/instructions"], s => s.GoAsync("mcp/instructions", s.Heading(s.Text(Mcp, "Mcp.Instructions.Page.Heading")))),
        new("mcp prompts", ["/mcp/prompts"], async s =>
        {
            await s.GoAsync("mcp/prompts", s.Heading(s.Text(Mcp, "Mcp.Prompts.Page.Heading")));
            await Expect(s.Page.Locator("main table.mcp-prompts tbody tr").First).ToBeVisibleAsync();
        }),
        new("mcp activity", ["/mcp/activity"], s => s.GoAsync("mcp/activity", s.Heading(s.Text(Mcp, "Mcp.Activity.Page.Heading")))),
        new("ai usage", ["/ai/usage"], s => s.GoAsync("ai/usage", s.Heading(s.Text(Ai, "Ai.Usage.Page.Heading")))),
        new("multi-site sites", ["/multi-site/sites"], async s =>
        {
            await s.GoAsync("multi-site/sites", s.Heading(s.Text(MultiSite, "MultiSite.Sites.Page.Heading")));
            await Expect(s.Page.Locator("main table.multi-site-table tbody tr").First).ToBeVisibleAsync();
        }),
        new("multi-site hosts", ["/multi-site/sites/{Id:guid}/hosts"], s =>
            s.GoAsync($"multi-site/sites/{s.Data.Site}/hosts", s.Heading(Ui.Format(s.Text(MultiSite, "MultiSite.Hosts.HeadingOf"), s.Data.SiteName)))),
        new("search", ["/search"], s => s.GoAsync("search", s.Heading(s.Text(Search, "Search.Page.Heading")))),

        new("component gallery", ["/system/components"], s => s.GoAsync("system/components", s.Heading(s.Mark(Repository.AdministrationText("Gallery", "System.Components.Heading"))))),
        new("plugin problems", ["/system/plugins"], s => s.GoAsync("system/plugins", s.Heading(s.Admin("Plugins.Problems.Heading")))),

        new("no access", [], s => s.AsEditorAsync("users", s.Admin("Shell.NoAccess.Heading"))),
        new("no site", [], s => s.AsUserWithoutSiteAsync(s.Admin("Shell.NoSite.Heading"))),
        // In English: the default culture is a member of the config.json the admin could not read.
        new("configuration error", [], s => s.WithBrokenConfigurationAsync(Repository.AdminText("Configuration.Error.Title"))),

        // Last: the callback ends the sign-in of the tab, which has none to end, and so shows its failure.
        new("sign-in callback", ["/authentication/login-callback"], s =>
            s.GoAsync("authentication/login-callback", s.Heading(s.Admin("Authentication.Callback.FailedHeading")))),
    ];
}

// The tab a sweep walks the views in, signed in as a Super Admin, with the culture whose marks the texts carry (none for
// English) and the data of the host the views show.
public sealed class Sweep(AdminTab tab, SweepData data, string? culture, Func<string, string, Task<IPage>> asEditor, Func<string, Task<IPage>> asUserWithoutSite, Func<string, Task<IPage>> withBrokenConfiguration)
{
    public AdminTab Tab { get; } = tab;

    public IPage Page => Tab.Page;

    public SweepData Data { get; } = data;

    public string Mark(string text) => culture is null ? text : PseudoCulture.Transform(text, culture);

    public string Admin(string key) => Mark(Repository.AdminText(key));

    public string Text(string file, string key) => Mark(Repository.Text(file, key));

    public string Area(string area) => Mark(Repository.AdministrationHeading(area));

    public string AreaText(string area, string key) => Mark(Repository.AdministrationText(area, key));

    // The page the view is checked on: the sweep's tab, unless the view showed a tab of its own.
    public IPage Checked { get; set; } = tab.Page;

    // The link of a settings area's scope that names the current page.
    public ILocator Scope(string text) => Page.Locator("main .settings-scopes a[aria-current=page]").Filter(Ui.Exactly(text));

    public ILocator Heading(string text) => HeadingOf(Page, text);

    // The route by the app's own navigation, as a link does, so that the app is not started again; the view is there once
    // its own element shows, since the page before it may still be drawn while a lazy assembly loads.
    public async Task GoAsync(string route, ILocator ready)
    {
        await Page.EvaluateAsync("route => Blazor.navigateTo(route)", route);
        await Expect(ready).ToBeVisibleAsync();
    }

    // Once no text the admin shows while it waits for the API is left on the page, in a dialog either.
    public async Task LoadedAsync(IPage page) => await Expect(page.Locator("body").GetByText(loading).Filter(new LocatorFilterOptions { Visible = true })).ToHaveCountAsync(0);

    private readonly Regex loading = new(
        $@"^\s*({string.Join('|', Repository.LoadingTexts.Select(text => Regex.Escape(culture is null ? text : PseudoCulture.Transform(text, culture))))})\s*$");

    // The dialog of the library that has just opened, once it shows; its controls are the light children of its host.
    public async Task DialogAsync() => await Expect(Page.GetByRole(AriaRole.Dialog).Last).ToBeVisibleAsync();

    // A state another user sees, in a browser of that user, at the route, once the page's heading reads so.
    public async Task AsEditorAsync(string route, string heading) => Checked = await asEditor(route, heading);

    public async Task AsUserWithoutSiteAsync(string heading) => Checked = await asUserWithoutSite(heading);

    // The admin started with a config.json it cannot read, in a tab of its own.
    public async Task WithBrokenConfigurationAsync(string heading) => Checked = await withBrokenConfiguration(heading);

    // A heading of level 1 with the text, on any page.
    public static ILocator HeadingOf(IPage page, string text) =>
        page.Locator("main h1").Filter(new LocatorFilterOptions { HasTextRegex = new Regex($@"^\s*{Regex.Escape(text)}\s*$") });
}

// What the views show of the host, made or read once a run through the Management API.
public sealed record SweepData(
    Guid Article,
    Guid Showcase,
    Guid User,
    string UserName,
    Guid Role,
    string RoleName,
    Guid ApiKey,
    string ApiKeyName,
    Guid Member,
    string MemberName,
    string MemberGroupName,
    Guid Job,
    string JobType,
    Guid Webhook,
    string WebhookName,
    string Site,
    string SiteName);
