using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity.PreviewTokens;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Schema;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Identity;

// What a preview token reads beyond its entry: the targets of the entry's relations and rich text links, published, that
// the principal that minted it may read at the time of the request. It reads none of them directly.
public sealed class PreviewTokenTargetTests : IAsyncLifetime
{
    private const string Delivery = "/api/delivery/v1/dispatch";
    private const string Management = "/api/management/v1/content/dispatch";
    private const string Previewer = "Previewer";
    private const string NewsDesk = "news-desk";
    private const string Populate = "populate=reporter,contributors,beats";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;
    private static readonly string[] KeyTypes = ["dispatch", "reporter"];

    private readonly FluentCmsTestHost _host = AuthorizationServerTests.NewHost(withServiceClient: false)
        .WithContentTypes<Dispatch, Reporter, Beat>()
        .Configure(cms => cms.Authorization.AddRowRule<Reporter>(NewsDesk, _ => reporter => reporter.Desk == "news"));

    private Guid _role;
    private TestUser _minter = null!;
    private Guid _dispatch;
    private Guid _newsReporter;
    private Guid _sportsReporter;
    private Guid _draftReporter;
    private Guid _foreignReporter;
    private Guid _beat;
    private Guid _draftBeat;

    public async ValueTask InitializeAsync()
    {
        Guid otherSite;
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            using var system = SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), Origin.Seed, new SiteId(DefaultSite.Id));
            otherSite = (await scope.ServiceProvider.GetRequiredService<SiteManager>().CreateAsync("Other", "other", "en", Token)).Id;
        }

        var admin = _host.As(await _host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token));
        var beats = admin.Content<Beat>();
        _beat = await PublishedAsync(beats, new Beat { Name = "Politics" });
        _draftBeat = (await beats.CreateAsync(new Beat { Name = "Unannounced" }, Token)).Id;

        // The news reporter is published, and its draft has changed since.
        var reporters = admin.Content<Reporter>();
        _newsReporter = await PublishedAsync(reporters, new Reporter { Name = "Ada", Desk = "news", Beat = new Ref<Beat>(_beat) });
        var published = (await reporters.GetAsync(_newsReporter, new GetOptions { Status = QueryStatus.Draft }, Token))!;
        await reporters.UpdateAsync(_newsReporter, new Reporter { Name = "Ada, not yet published", Desk = "news", Beat = new Ref<Beat>(_beat) }, published.Version, Token);
        _sportsReporter = await PublishedAsync(reporters, new Reporter { Name = "Linus", Desk = "sports" });
        _draftReporter = (await reporters.CreateAsync(new Reporter { Name = "Grace", Desk = "news" }, Token)).Id;
        _foreignReporter = await PublishedAsync(admin.In(new SiteId(otherSite)).Content<Reporter>(), new Reporter { Name = "Edsger", Desk = "news" });

        _dispatch = (await admin.Content<Dispatch>().CreateAsync(
            new Dispatch
            {
                Title = "Election night",
                Reporter = new Ref<Reporter>(_newsReporter),
                Contributors = [new Ref<Reporter>(_newsReporter), new Ref<Reporter>(_sportsReporter), new Ref<Reporter>(_draftReporter)],
                Beats = [new Ref<Beat>(_beat), new Ref<Beat>(_draftBeat)],
                Body = Links(("Ada", _newsReporter), ("Linus", _sportsReporter), ("Grace", _draftReporter)),
            },
            Token)).Id;

        // A write refuses a target of another site, so the reference to one is written as the store would hold it.
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            context.References.Add(new Reference
            {
                Id = Guid.CreateVersion7(),
                SourceType = "dispatch",
                SourceDocumentId = _dispatch,
                State = ContentState.Draft,
                Field = "contributors",
                Position = 3,
                TargetType = "reporter",
                TargetId = _foreignReporter,
            });
            await context.SaveChangesAsync(Token);
        }

        _role = await _host.CreateRoleAsync(Previewer, ["content.dispatch.read", "content.beat.read"], Token);
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            context.Set<RoleGrant>().Add(new RoleGrant { Id = Guid.CreateVersion7(), RoleId = _role, Permission = "content.reporter.read", Constraints = new PermissionConstraints { RowRule = NewsDesk }.ToJson() });
            await context.SaveChangesAsync(Token);
        }

        _minter = await _host.CreateUserAsync(Previewer, cancellationToken: Token);
    }

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task ATokenPopulatesThePublishedTargetsItsMinterMayRead()
    {
        var minted = await MintAsync(_minter);

        var (status, body) = await ReadAsync(minted, $"{Delivery}/{_dispatch}?status=draft&{Populate}");

        status.ShouldBe(HttpStatusCode.OK, body.ToJsonString());
        body["title"]!.GetValue<string>().ShouldBe("Election night");
        body["reporter"]!["name"]!.GetValue<string>().ShouldBe("Ada");
        Ids(body["contributors"]).ShouldBe([_newsReporter]);
        Ids(body["beats"]).ShouldBe([_beat]);

        // The minter's own read of the draft leaves out the same targets it may not read, and reads the drafts of the others.
        using var manager = await _host.AsUserAsync(_minter);
        using var managed = await manager.GetAsync(new Uri($"{Management}/{_dispatch}?status=draft&{Populate}", UriKind.Relative), Token);
        var draft = (await managed.Content.ReadFromJsonAsync<JsonObject>(Token))!;
        draft["reporter"]!["name"]!.GetValue<string>().ShouldBe("Ada, not yet published");
        Ids(draft["contributors"]).ShouldBe([_newsReporter, _draftReporter]);
        Ids(draft["beats"]).ShouldBe([_beat, _draftBeat]);
    }

    [Fact]
    public async Task ATokenPopulatesToTheDepthAskedWithinTheCap()
    {
        var minted = await MintAsync(_minter);

        var one = await ReadAsync(minted, $"{Delivery}/{_dispatch}?status=draft&populate=reporter");
        one.Body["reporter"]!["beat"]!.AsObject().Select(pair => pair.Key).ShouldBe(["id", "type"], ignoreOrder: true);

        var two = await ReadAsync(minted, $"{Delivery}/{_dispatch}?status=draft&populate=reporter.beat");
        two.Status.ShouldBe(HttpStatusCode.OK, two.Body.ToJsonString());
        two.Body["reporter"]!["beat"]!["name"]!.GetValue<string>().ShouldBe("Politics");

        (await ReadAsync(minted, $"{Delivery}/{_dispatch}?status=draft&populate=*&depth=2")).Body["reporter"]!["beat"]!["name"]!.GetValue<string>().ShouldBe("Politics");
        (await ReadAsync(minted, $"{Delivery}/{_dispatch}?status=draft&populate=*&depth=4")).Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ATokenKeepsTheLinksOfItsBodyToTheTargetsItsMinterMayRead()
    {
        var minted = await MintAsync(_minter);

        var json = (await ReadAsync(minted, $"{Delivery}/{_dispatch}?status=draft")).Body["body"]!.ToJsonString();
        json.ShouldContain(_newsReporter.ToString("D"));
        json.ShouldNotContain(_sportsReporter.ToString("D"));
        json.ShouldNotContain(_draftReporter.ToString("D"));

        var html = (await ReadAsync(minted, $"{Delivery}/{_dispatch}?status=draft&richText=html")).Body["body"]!.GetValue<string>();
        html.ShouldContain($"data-entry-id=\"{_newsReporter:D}\"");
        html.ShouldNotContain(_sportsReporter.ToString("D"));
        html.ShouldNotContain(_draftReporter.ToString("D"));
        html.ShouldContain("Linus");
    }

    [Fact]
    public async Task ATokenReadsNoTargetDirectly()
    {
        var minted = await MintAsync(_minter);

        await ShouldBeOutsideAsync(minted, $"/api/delivery/v1/reporter/{_newsReporter}?status=draft", "type");
        await ShouldBeOutsideAsync(minted, $"/api/delivery/v1/reporter/{_newsReporter}", "type");
        await ShouldBeOutsideAsync(minted, "/api/delivery/v1/beat?status=draft", "id");

        // Nor does it through any other surface: the engine reads targets for the entry only.
        var preview = _minter with
        {
            Principal = new Principal(PrincipalKind.AdminUser, _minter.Id, _minter.Name, new Dictionary<string, string>
            {
                [CmsClaimTypes.Scope] = TokenScopes.Preview,
                [CmsClaimTypes.Sites] = DefaultSite.Id.ToString("D"),
                [CmsClaimTypes.Types] = "dispatch",
                [CmsClaimTypes.Entry] = _dispatch.ToString("D"),
            }),
        };
        var content = _host.As(preview, Origin.Api).Content();
        var entry = await content.GetAsync("dispatch", _dispatch, new GetOptions { Status = QueryStatus.Draft, Populate = ["reporter"] }, Token);
        entry.ShouldNotBeNull()["reporter"].ShouldBeOfType<ContentDocument>()["name"].ShouldBe("Ada");
        await Should.ThrowAsync<ValidationException>(() => content.GetAsync("reporter", _newsReporter, null, Token));
        await Should.ThrowAsync<ValidationException>(() => content.QueryAsync("beat", new ContentQuery(), Token));
    }

    [Fact]
    public async Task ATokenStopsPopulatingATypeOnceItsMinterLosesTheRead()
    {
        var minted = await MintAsync(_minter);
        var path = $"{Delivery}/{_dispatch}?status=draft&populate=*&depth=2";

        var before = (await ReadAsync(minted, path)).Body;
        before["reporter"]!["beat"]!["name"]!.GetValue<string>().ShouldBe("Politics");
        Ids(before["beats"]).ShouldBe([_beat]);

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            context.RemoveRange(context.Set<RoleGrant>().Where(grant => grant.RoleId == _role && grant.Permission == "content.beat.read"));
            await context.SaveChangesAsync(Token);
        }

        var (status, after) = await ReadAsync(minted, path);
        status.ShouldBe(HttpStatusCode.OK, after.ToJsonString());
        after["reporter"]!["name"]!.GetValue<string>().ShouldBe("Ada");
        after["reporter"]!["beat"].ShouldBeNull();
        Ids(after["beats"]).ShouldBeEmpty();
    }

    [Fact]
    public async Task ATokenOfAKeyIsHeldToTheTypesTheKeyIsBoundTo()
    {
        var key = await _host.CreateApiKeyAsync(ApiKeyClass.Management, Previewer, cancellationToken: Token);
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            var stored = await context.Set<FluentCMS.Core.Identity.ApiKeys.ApiKey>().FindAsync([key.Id], Token);
            stored!.TypeNames = JsonSerializer.Serialize(KeyTypes);
            await context.SaveChangesAsync(Token);
        }

        using var client = _host.AsKey(key);
        using var response = await client.PostAsync(new Uri($"{Management}/{_dispatch}/preview-token", UriKind.Relative), null, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        var minted = (await response.Content.ReadFromJsonAsync<JsonObject>(Token))!;

        var (status, body) = await ReadAsync(minted, $"{Delivery}/{_dispatch}?status=draft&{Populate}");

        status.ShouldBe(HttpStatusCode.OK, body.ToJsonString());
        body["reporter"]!["name"]!.GetValue<string>().ShouldBe("Ada");
        Ids(body["contributors"]).ShouldBe([_newsReporter]);
        Ids(body["beats"]).ShouldBeEmpty();
    }

    [Fact]
    public async Task NoCacheKeepsWhatATokenReads()
    {
        var minted = await MintAsync(_minter);
        var path = $"{Delivery}/{_dispatch}?status=draft&{Populate}";

        for (var read = 0; read < 2; read++)
        {
            using var client = PreviewClient(minted);
            using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Token);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Headers.CacheControl!.NoStore.ShouldBeTrue();
            response.Headers.Age.ShouldBeNull();
            response.Headers.Vary.ShouldContain("Authorization");
        }

        using var anonymous = _host.CreateClient();
        using var refused = await anonymous.GetAsync(new Uri(path, UriKind.Relative), Token);
        refused.StatusCode.ShouldNotBe(HttpStatusCode.OK);
    }

    private static async Task<Guid> PublishedAsync<T>(IContentService<T> content, T entry)
        where T : ContentItem
    {
        var created = await content.CreateAsync(entry, Token);
        await content.PublishAsync(created.Id, cancellationToken: Token);
        return created.Id;
    }

    private static RichText Links(params (string Text, Guid Reporter)[] links) =>
        new(new JsonObject
        {
            ["type"] = "doc",
            ["content"] = new JsonArray(links.Select(link => (JsonNode)new JsonObject
            {
                ["type"] = "paragraph",
                ["content"] = new JsonArray(new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = link.Text,
                    ["marks"] = new JsonArray(new JsonObject
                    {
                        ["type"] = "entryLink",
                        ["attrs"] = new JsonObject { ["entryType"] = "reporter", ["entryId"] = link.Reporter.ToString("D") },
                    }),
                }),
            }).ToArray()),
        }.ToJsonString());

    private static List<Guid> Ids(JsonNode? items) => [.. items!.AsArray().Select(item => item!["id"]!.GetValue<Guid>())];

    private async Task<JsonObject> MintAsync(TestUser user)
    {
        using var client = await _host.AsUserAsync(user);
        using var response = await client.PostAsync(new Uri($"{Management}/{_dispatch}/preview-token", UriKind.Relative), null, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Token))!;
    }

    private HttpClient PreviewClient(JsonObject minted)
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Preview", minted["token"]!.GetValue<string>());
        return client;
    }

    private async Task<(HttpStatusCode Status, JsonNode Body)> ReadAsync(JsonObject minted, string path)
    {
        using var client = PreviewClient(minted);
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Token);
        return (response.StatusCode, JsonNode.Parse(await response.Content.ReadAsStringAsync(Token))!);
    }

    private async Task ShouldBeOutsideAsync(JsonObject minted, string path, string where)
    {
        var (status, body) = await ReadAsync(minted, path);
        status.ShouldBe(HttpStatusCode.Forbidden, body.ToJsonString());
        body["code"]!.GetValue<string>().ShouldBe(PreviewTokenErrorCodes.OutsideToken);
        body["errors"]![0]!["path"]!.GetValue<string>().ShouldBe(where);
    }
}
