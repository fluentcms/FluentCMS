using System.Net;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity.PreviewTokens;
using FluentCMS.Core.IntegrationTests.Management;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Identity;

// The preview URL of a type, from the settings area "preview", in the answer of POST .../preview-token (#3099).
public sealed class PreviewUrlTests : IAsyncLifetime
{
    private const string Template = "https://www.example.com/{locale}/stories/{slug}?title={title}&preview={token}";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly FluentCmsTestHost _host = AuthorizationServerTests.NewHost(withServiceClient: false).WithContentTypes<Story>();

    private HttpClient _admin = null!;
    private HttpClient _editor = null!;
    private IContentService<Story> _stories = null!;

    public async ValueTask InitializeAsync()
    {
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            using var system = SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), scope.ServiceProvider.GetRequiredService<TimeProvider>(), Origin.Seed, new SiteId(DefaultSite.Id));
            await scope.ServiceProvider.GetRequiredService<SiteManager>().AddLocaleAsync(DefaultSite.Id, "fr", null, true, null, Token);
        }

        _admin = _host.AsKey(await _host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token));
        _editor = await _host.AsUserAsync(await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token));
        _stories = _host.As(await _host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token)).Content<Story>();
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        _editor.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task TheAnswerFillsTheTemplateWithTheDraftAndThePublishedEntry()
    {
        await SaveAsync("settings/preview/defaults", Template);
        var id = (await _stories.CreateAsync(new Story { Title = "Fish & chips/2?", Slug = "fish" }, Token)).Id;

        var minted = await MintAsync(_editor, "story", id);
        var token = minted["token"]!.GetValue<string>();
        minted["url"]!.GetValue<string>().ShouldBe($"https://www.example.com/en/stories/fish?title=Fish%20%26%20chips%2F2%3F&preview={token}");
        minted.ContainsKey("publishedUrl").ShouldBeTrue();
        minted["publishedUrl"].ShouldBeNull();

        // The published page is at the values the entry was published with, and has no token.
        await _stories.PublishAsync(id, cancellationToken: Token);
        var draft = (await _stories.GetAsync(id, new GetOptions { Status = QueryStatus.Draft }, Token))!;
        await _stories.UpdateAsync(id, new Story { Title = "Fish", Slug = "fish-2" }, draft.Version, Token);

        minted = await MintAsync(_editor, "story", id);
        minted["url"]!.GetValue<string>().ShouldBe($"https://www.example.com/en/stories/fish-2?title=Fish&preview={minted["token"]!.GetValue<string>()}");
        minted["publishedUrl"]!.GetValue<string>().ShouldBe("https://www.example.com/en/stories/fish?title=Fish%20%26%20chips%2F2%3F");

        // A token of one locale fills that locale, which has never been published.
        var french = await AdminFixture.SendAsync(_editor, HttpMethod.Put, $"content/story/{id}?locale=fr", """{ "title": "Poisson", "slug": "fish-2" }""", "\"0\"");
        french.Status.ShouldBe(HttpStatusCode.OK, french.Body?.ToJsonString());
        minted = await MintAsync(_editor, "story", id, """{ "locale": "fr" }""");
        minted["url"]!.GetValue<string>().ShouldBe($"https://www.example.com/fr/stories/fish-2?title=Poisson&preview={minted["token"]!.GetValue<string>()}");
        minted["publishedUrl"].ShouldBeNull();
    }

    // {locale} is the site's code of the locale whose row gave the values, and the published page is that locale's alone.
    [Fact]
    public async Task TheLocaleIsTheSitesCodeOfTheRowThatGaveTheValues()
    {
        await SaveAsync("settings/preview/defaults", Template);
        var english = (await _stories.CreateAsync(new Story { Title = "Fish", Slug = "fish" }, Token)).Id;
        await _stories.PublishAsync(english, cancellationToken: Token);

        var upper = await MintAsync(_editor, "story", english, """{ "locale": "EN" }""");
        upper["url"]!.GetValue<string>().ShouldBe($"https://www.example.com/en/stories/fish?title=Fish&preview={upper["token"]!.GetValue<string>()}");
        upper["publishedUrl"]!.GetValue<string>().ShouldBe("https://www.example.com/en/stories/fish?title=Fish");

        // Read in French, an entry with no French row is served by English, and so are both of its pages.
        var served = await MintAsync(_editor, "story", english, query: "?locale=fr");
        served["url"]!.GetValue<string>().ShouldBe($"https://www.example.com/en/stories/fish?title=Fish&preview={served["token"]!.GetValue<string>()}");
        served["publishedUrl"]!.GetValue<string>().ShouldBe("https://www.example.com/en/stories/fish?title=Fish");

        // An entry with a French draft has a French page, which is published only once French is.
        var bilingual = (await _stories.CreateAsync(new Story { Title = "Chips", Slug = "chips" }, Token)).Id;
        await _stories.PublishAsync(bilingual, cancellationToken: Token);
        (await AdminFixture.SendAsync(_editor, HttpMethod.Put, $"content/story/{bilingual}?locale=fr", """{ "title": "Frites", "slug": "chips" }""", "\"0\"")).Status.ShouldBe(HttpStatusCode.OK);

        var french = await MintAsync(_editor, "story", bilingual, query: "?locale=fr");
        french["url"]!.GetValue<string>().ShouldBe($"https://www.example.com/fr/stories/chips?title=Frites&preview={french["token"]!.GetValue<string>()}");
        french["publishedUrl"].ShouldBeNull();

        var publish = await AdminFixture.SendAsync(_editor, HttpMethod.Post, $"content/story/{bilingual}/publish?locale=fr");
        publish.Status.ShouldBe(HttpStatusCode.OK, publish.Body?.ToJsonString());
        (await MintAsync(_editor, "story", bilingual, query: "?locale=fr"))["publishedUrl"]!.GetValue<string>().ShouldBe("https://www.example.com/fr/stories/chips?title=Frites");
    }

    [Fact]
    public async Task ATypeWithoutATemplateHasNoUrl()
    {
        var story = (await _stories.CreateAsync(new Story { Title = "Untitled", Slug = "untitled" }, Token)).Id;
        var before = await MintAsync(_editor, "story", story);
        (before.ContainsKey("url"), before["url"], before["publishedUrl"]).ShouldBe((true, null, null));

        await SaveAsync("settings/preview/defaults", Template);
        var created = await AdminFixture.SendAsync(_editor, HttpMethod.Post, "content/note", """{ "title": "A note" }""");
        var note = await MintAsync(_editor, "note", created.Json["id"]!.GetValue<Guid>());

        (note.ContainsKey("url"), note["url"], note["publishedUrl"]).ShouldBe((true, null, null));
    }

    // A site's document replaces the instance's, as in every area.
    [Fact]
    public async Task ASiteTemplateReplacesTheTemplateOfTheDefaults()
    {
        await SaveAsync("settings/preview/defaults", Template);
        await SaveAsync("settings/preview", "https://site.example.com/{slug}?t={token}");
        var id = (await _stories.CreateAsync(new Story { Title = "Fish", Slug = "fish" }, Token)).Id;

        var minted = await MintAsync(_editor, "story", id);

        minted["url"]!.GetValue<string>().ShouldBe($"https://site.example.com/fish?t={minted["token"]!.GetValue<string>()}");
    }

    [Fact]
    public async Task AnEditorGetsTheUrlWithoutManagingTheArea()
    {
        await SaveAsync("settings/preview/defaults", Template);
        var id = (await _stories.CreateAsync(new Story { Title = "Fish", Slug = "fish" }, Token)).Id;

        (await MintAsync(_editor, "story", id))["url"].ShouldNotBeNull();
        (await AdminFixture.SendAsync(_editor, HttpMethod.Get, "settings/preview")).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await AdminFixture.SendAsync(_editor, HttpMethod.Put, "settings/preview", Document(Template), "\"0\"")).Status.ShouldBe(HttpStatusCode.Forbidden);

        var area = (await AdminFixture.SendAsync(_admin, HttpMethod.Get, "settings")).Json["items"]!.AsArray().Single(item => item!["area"]!.GetValue<string>() == "preview")!;
        (area["plugin"], area["site"]!.GetValue<bool>(), area["defaults"]!.GetValue<bool>()).ShouldBe((null, true, true));
    }

    [Theory]
    [InlineData("story", "ftp://www.example.com/{slug}?preview={token}", PreviewTokenErrorCodes.InvalidUrlTemplate)]
    [InlineData("story", "/stories/{slug}?preview={token}", PreviewTokenErrorCodes.InvalidUrlTemplate)]
    [InlineData("story", "https://{slug}.example.com/?preview={token}", PreviewTokenErrorCodes.InvalidUrlTemplate)]
    [InlineData("story", "https://www.example.com/{token}/{slug}", PreviewTokenErrorCodes.InvalidUrlTemplate)]
    [InlineData("story", "https://www.example.com/.%{slug}?preview={token}", PreviewTokenErrorCodes.InvalidUrlTemplate)]
    [InlineData("story", "https://www.example.com/{body}?preview={token}", PreviewTokenErrorCodes.UnknownTemplateName)]
    [InlineData("note", "https://www.example.com/{locale}?preview={token}", PreviewTokenErrorCodes.UnknownTemplateName)]
    [InlineData("nope", "https://www.example.com/?preview={token}", PreviewTokenErrorCodes.UnknownTemplateName)]
    public async Task ATemplateThatIsNotAnHttpUrlOrNamesWhatTheTypeLacksIsRefused(string type, string template, string code)
    {
        foreach (var path in new[] { "settings/preview/defaults", "settings/preview" })
        {
            var refused = await AdminFixture.SendAsync(_admin, HttpMethod.Put, path, $$"""{ "templates": { "{{type}}": "{{template}}" } }""", "\"0\"");

            refused.Status.ShouldBe(HttpStatusCode.UnprocessableEntity, refused.Body?.ToJsonString());
            refused.Json["code"]!.GetValue<string>().ShouldBe(code);
            refused.Json["errors"]![0]!["path"]!.GetValue<string>().ShouldBe($"templates.{type}");
            (await AdminFixture.SendAsync(_admin, HttpMethod.Get, path)).Json["source"]!.GetValue<string>().ShouldBe("code");
        }
    }

    // The URL is filled from the caller's own read, so a field it may not read, or one that is empty, leaves no URL.
    [Fact]
    public async Task AFieldTheCallerMayNotReadOrThatIsEmptyLeavesNoUrl()
    {
        await SaveAsync("settings/preview/defaults", "https://www.example.com/stories/{code}?preview={token}");
        var id = (await _stories.CreateAsync(new Story { Title = "Fish", Slug = "fish", Code = "c-42" }, Token)).Id;
        var empty = (await _stories.CreateAsync(new Story { Title = "Chips", Slug = "chips" }, Token)).Id;

        var role = await _host.CreateRoleAsync("Story title reader", [], Token);
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            context.Set<RoleGrant>().Add(new RoleGrant { Id = Guid.CreateVersion7(), RoleId = role, Permission = "content.story.read", Constraints = """{"fields":["title","slug"]}""" });
            await context.SaveChangesAsync(Token);
        }

        using var reader = await _host.AsUserAsync(await _host.CreateUserAsync("Story title reader", cancellationToken: Token));
        var limited = await MintAsync(reader, "story", id);
        limited["token"].ShouldNotBeNull();
        limited["url"].ShouldBeNull();

        (await MintAsync(_editor, "story", id))["url"]!.GetValue<string>().ShouldStartWith("https://www.example.com/stories/c-42?preview=");
        (await MintAsync(_editor, "story", empty))["url"].ShouldBeNull();
    }

    // The schema is the code's, so a template saved for a field that a later version dropped stays in the row.
    [Fact]
    public async Task ATemplateTheSchemaNoLongerFitsGivesNoUrl()
    {
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
            context.Set<SettingRow>().Add(new SettingRow
            {
                Id = Guid.CreateVersion7(),
                Key = PreviewSettings.Area,
                Json = """{"templates":{"story":"https://www.example.com/{dropped}?preview={token}"}}""",
                Version = 1,
                UpdatedAt = _host.Time.GetUtcNow(),
            });
            await context.SaveChangesAsync(Token);
        }

        var id = (await _stories.CreateAsync(new Story { Title = "Fish", Slug = "fish" }, Token)).Id;

        var minted = await MintAsync(_editor, "story", id);

        minted["token"].ShouldNotBeNull();
        minted["url"].ShouldBeNull();
    }

    private static string Document(string template) => new JsonObject { ["templates"] = new JsonObject { ["story"] = template } }.ToJsonString();

    private async Task SaveAsync(string path, string template)
    {
        var saved = await AdminFixture.SendAsync(_admin, HttpMethod.Put, path, Document(template), "\"0\"");
        saved.Status.ShouldBe(HttpStatusCode.OK, saved.Body?.ToJsonString());
    }

    private static async Task<JsonObject> MintAsync(HttpClient client, string type, Guid id, string? body = null, string query = "")
    {
        var minted = await AdminFixture.SendAsync(client, HttpMethod.Post, $"content/{type}/{id}/preview-token{query}", body);
        minted.Status.ShouldBe(HttpStatusCode.OK, minted.Body?.ToJsonString());
        return minted.Json.AsObject();
    }
}
