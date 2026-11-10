using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity.PreviewTokens;
using FluentCMS.Core.Sites;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Identity;

public sealed class PreviewTokenTests : IAsyncLifetime
{
    private const string Management = "/api/management/v1/content/brief";
    private const string Delivery = "/api/delivery/v1/brief";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly FluentCmsTestHost _host = AuthorizationServerTests.NewHost(withServiceClient: false).WithContentTypes<Brief>();

    private HttpClient _editor = null!;
    private Guid _entry;
    private Guid _other;
    private Guid _otherSite;

    public async ValueTask InitializeAsync()
    {
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            using var system = SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), Origin.Seed, new SiteId(DefaultSite.Id));
            var sites = scope.ServiceProvider.GetRequiredService<SiteManager>();
            await sites.AddLocaleAsync(DefaultSite.Id, "fr", null, true, null, Token);
            _otherSite = (await sites.CreateAsync("Other", "other", "en", Token)).Id;
        }

        _editor = await _host.AsUserAsync(await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token));
        _entry = await CreateAsync("Draft only");
        _other = await CreateAsync("Another draft");

        using var french = new HttpRequestMessage(HttpMethod.Put, $"{Management}/{_entry}?locale=fr") { Content = JsonContent.Create(new { title = "Brouillon" }) };
        french.Headers.IfMatch.Add(new EntityTagHeaderValue("\"0\""));
        using var added = await _editor.SendAsync(french, Token);
        added.StatusCode.ShouldBe(HttpStatusCode.OK, await added.Content.ReadAsStringAsync(Token));
    }

    public async ValueTask DisposeAsync()
    {
        _editor.Dispose();
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task ATokenReadsTheDraftOfItsEntryAndCarriesItsBinding()
    {
        var minted = await MintAsync(_entry, new { minutes = 5 });

        minted["scheme"]!.GetValue<string>().ShouldBe("Preview");
        minted["type"]!.GetValue<string>().ShouldBe("brief");
        minted["id"]!.GetValue<Guid>().ShouldBe(_entry);
        minted["site"]!.GetValue<Guid>().ShouldBe(DefaultSite.Id);
        minted["locale"].ShouldBeNull();
        DateTimeOffset.Parse(minted["expiresAt"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture)
            .ShouldBe(_host.Time.GetUtcNow().AddMinutes(5), TimeSpan.FromSeconds(1));

        var (status, body) = await ReadAsync(minted, $"{Delivery}/{_entry}?status=draft");
        status.ShouldBe(HttpStatusCode.OK, body.ToJsonString());
        body["title"]!.GetValue<string>().ShouldBe("Draft only");

        (await ReadAsync(minted, $"{Delivery}/{_entry}?status=draft&locale=fr")).Body["title"]!.GetValue<string>().ShouldBe("Brouillon");

        using var anonymous = _host.CreateClient();
        using var refused = await anonymous.GetAsync(new Uri($"{Delivery}/{_entry}?status=draft", UriKind.Relative), Token);
        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ATokenIsRefusedForAnyOtherEntryRouteOrStatus()
    {
        var minted = await MintAsync(_entry);

        await ShouldBeOutsideAsync(minted, $"{Delivery}/{_other}?status=draft", "id");
        await ShouldBeOutsideAsync(minted, $"/api/delivery/v1/note/{_entry}?status=draft", "type");
        await ShouldBeOutsideAsync(minted, $"{Delivery}?status=draft", "id");
        await ShouldBeOutsideAsync(minted, $"{Delivery}/by/id/{_entry}?status=draft", "id");
        await ShouldBeOutsideAsync(minted, $"{Delivery}/{_entry}", "status");
    }

    [Fact]
    public async Task ATokenIsRefusedInAnotherSite()
    {
        var minted = await MintAsync(_entry);

        var (status, body) = await ReadAsync(minted, $"{Delivery}/{_entry}?status=draft&site={_otherSite}");

        status.ShouldBe(HttpStatusCode.Forbidden);
        body["code"]!.GetValue<string>().ShouldBe("FCMS3121");
    }

    [Fact]
    public async Task ATokenBoundToALocaleReadsThatLocaleOnly()
    {
        var minted = await MintAsync(_entry, new { locale = "fr" });
        minted["locale"]!.GetValue<string>().ShouldBe("fr");

        (await ReadAsync(minted, $"{Delivery}/{_entry}?status=draft&locale=fr")).Status.ShouldBe(HttpStatusCode.OK);
        await ShouldBeOutsideAsync(minted, $"{Delivery}/{_entry}?status=draft", "locale");
        await ShouldBeOutsideAsync(minted, $"{Delivery}/{_entry}?status=draft&locale=all", "locale");
    }

    [Fact]
    public async Task ATokenIsRefusedOnceItExpires()
    {
        var minted = await MintAsync(_entry, new { minutes = 1 });
        var path = $"{Delivery}/{_entry}?status=draft";

        _host.Time.Advance(TimeSpan.FromSeconds(58));
        (await ReadAsync(minted, path)).Status.ShouldBe(HttpStatusCode.OK);

        _host.Time.Advance(TimeSpan.FromSeconds(2));
        var (status, body) = await ReadAsync(minted, path);
        status.ShouldBe(HttpStatusCode.Unauthorized);
        body["code"]!.GetValue<string>().ShouldBe(PreviewTokenErrorCodes.Expired);
    }

    [Fact]
    public async Task ATokenCannotWriteNorMintNorPassAsABearerToken()
    {
        var minted = await MintAsync(_entry);
        var token = minted["token"]!.GetValue<string>();

        using var preview = _host.CreateSecureClient();
        preview.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Preview", token);
        using var write = new HttpRequestMessage(HttpMethod.Put, $"{Management}/{_entry}") { Content = JsonContent.Create(new { title = "Changed" }) };
        write.Headers.IfMatch.Add(new EntityTagHeaderValue("\"1\""));
        using var written = await preview.SendAsync(write, Token);
        written.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var mint = await preview.PostAsync(new Uri($"{Management}/{_entry}/preview-token", UriKind.Relative), null, Token);
        mint.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var bearer = _host.CreateSecureClient();
        bearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var asBearer = await bearer.GetAsync(new Uri($"{Delivery}/{_entry}?status=draft", UriKind.Relative), Token);
        asBearer.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ATokenThatIsNotOneOfOursIsRefused()
    {
        var minted = await MintAsync(_entry);
        var parts = minted["token"]!.GetValue<string>().Split('.');
        var forged = new JsonObject { ["token"] = $"{parts[0]}.{Base64Url(Encoding.UTF8.GetBytes($"{{\"fcms_entry\":\"{_other}\"}}"))}.{parts[2]}" };

        var (status, body) = await ReadAsync(forged, $"{Delivery}/{_other}?status=draft");

        status.ShouldBe(HttpStatusCode.Unauthorized);
        body["code"]!.GetValue<string>().ShouldBe(PreviewTokenErrorCodes.InvalidToken);
    }

    [Fact]
    public async Task MintingNeedsThePermissionToReadTheDraft()
    {
        await _host.CreateRoleAsync("Note reader", ["content.note.read"], Token);
        using var reader = await _host.AsUserAsync(await _host.CreateUserAsync("Note reader", cancellationToken: Token));

        using var refused = await reader.PostAsync(new Uri($"{Management}/{_entry}/preview-token", UriKind.Relative), null, Token);

        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await refused.Content.ReadFromJsonAsync<JsonObject>(Token))!["code"]!.GetValue<string>().ShouldBe("FCMS3104");

        using var unknown = await _editor.PostAsync(new Uri($"{Management}/{Guid.CreateVersion7()}/preview-token", UriKind.Relative), null, Token);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ATokenStopsReadingOnceItsUserLosesTheRead()
    {
        await _host.CreateRoleAsync("Brief reader", ["content.brief.read"], Token);
        var reader = await _host.CreateUserAsync("Brief reader", cancellationToken: Token);
        using (var client = await _host.AsUserAsync(reader))
        {
            using var response = await client.PostAsync(new Uri($"{Management}/{_entry}/preview-token", UriKind.Relative), null, Token);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var minted = (await response.Content.ReadFromJsonAsync<JsonObject>(Token))!;
            (await ReadAsync(minted, $"{Delivery}/{_entry}?status=draft")).Status.ShouldBe(HttpStatusCode.OK);

            await using (var scope = _host.Services.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<FluentCMS.Core.Storage.CmsDbContext>();
                context.RemoveRange(context.Set<FluentCMS.Core.Identity.CmsUserRole>().Where(membership => membership.UserId == reader.Id));
                await context.SaveChangesAsync(Token);
            }

            (await ReadAsync(minted, $"{Delivery}/{_entry}?status=draft")).Status.ShouldBe(HttpStatusCode.NotFound);
        }
    }

    // The binding is the engine's, so a read that does not come through Delivery reaches the one entry too.
    [Fact]
    public async Task ThePreviewPrincipalReachesItsEntryAloneWhateverReadsIt()
    {
        var editor = await _host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        var preview = editor with
        {
            Principal = new Principal(PrincipalKind.AdminUser, editor.Id, editor.Name, new Dictionary<string, string>
            {
                [CmsClaimTypes.Scope] = TokenScopes.Preview,
                [CmsClaimTypes.Sites] = DefaultSite.Id.ToString("D"),
                [CmsClaimTypes.Types] = "brief",
                [CmsClaimTypes.Entry] = _entry.ToString("D"),
            }),
        };

        var drafts = await _host.As(preview, Origin.Api).In("fr").Content().QueryAsync("brief", new ContentQuery { Status = QueryStatus.Draft, Site = new SiteId(DefaultSite.Id), Locale = "fr" }, Token);
        drafts.Items.Select(item => item.Get<Guid>("id")).ShouldBe([_entry]);

        (await _host.As(preview, Origin.Api).In("en").Content().GetAsync("brief", _entry, new GetOptions { Status = QueryStatus.Draft, Locale = "en" }, Token)).ShouldNotBeNull();
        (await _host.As(preview, Origin.Api).In("fr").Content().GetAsync("brief", _other, new GetOptions { Status = QueryStatus.Draft, Locale = "fr" }, Token)).ShouldBeNull();
    }

    [Fact]
    public async Task AManagementKeyMintsATokenThatReadsAsTheKey()
    {
        var key = await _host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.EditorName, cancellationToken: Token);
        using var client = _host.AsKey(key);

        using var response = await client.PostAsync(new Uri($"{Management}/{_entry}/preview-token", UriKind.Relative), null, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        var minted = (await response.Content.ReadFromJsonAsync<JsonObject>(Token))!;
        (await ReadAsync(minted, $"{Delivery}/{_entry}?status=draft")).Status.ShouldBe(HttpStatusCode.OK);

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FluentCMS.Core.Storage.CmsDbContext>();
            var stored = await context.Set<FluentCMS.Core.Identity.ApiKeys.ApiKey>().FindAsync([key.Id], Token);
            stored!.RevokedAt = _host.Time.GetUtcNow();
            await context.SaveChangesAsync(Token);
        }

        (await ReadAsync(minted, $"{Delivery}/{_entry}?status=draft")).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public async Task ALifetimeOutsideTheOptionsIsRefused(int minutes)
    {
        using var response = await _editor.PostAsJsonAsync(new Uri($"{Management}/{_entry}/preview-token", UriKind.Relative), new { minutes }, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync(Token)).ShouldContain(PreviewTokenErrorCodes.InvalidLifetime);
    }

    [Fact]
    public async Task ALocaleOfAnUnlocalizedTypeIsRefused()
    {
        using var created = await _editor.PostAsJsonAsync(new Uri("/api/management/v1/content/note", UriKind.Relative), new { title = "A note" }, Token);
        var note = (await created.Content.ReadFromJsonAsync<JsonObject>(Token))!["id"]!.GetValue<Guid>();

        using var response = await _editor.PostAsJsonAsync(new Uri($"/api/management/v1/content/note/{note}/preview-token", UriKind.Relative), new { locale = "en" }, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    private async Task<Guid> CreateAsync(string title)
    {
        using var response = await _editor.PostAsJsonAsync(new Uri(Management, UriKind.Relative), new { title }, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Token))!["id"]!.GetValue<Guid>();
    }

    private async Task<JsonObject> MintAsync(Guid entry, object? body = null)
    {
        var uri = new Uri($"{Management}/{entry}/preview-token", UriKind.Relative);
        using var response = body is null ? await _editor.PostAsync(uri, null, Token) : await _editor.PostAsJsonAsync(uri, body, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        return (await response.Content.ReadFromJsonAsync<JsonObject>(Token))!;
    }

    private async Task<(HttpStatusCode Status, JsonNode Body)> ReadAsync(JsonObject minted, string path)
    {
        using var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Preview", minted["token"]!.GetValue<string>());
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

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
