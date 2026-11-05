using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.ApiKeys;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Content;
using FluentCMS.Publishing;
using FluentCMS.Sites;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Identity;

// The edges of a key: what a request may carry, the FluentCms scheme, and the binding on every read and write path.
public sealed class ApiKeyBoundaryTests : IDisposable
{
    private readonly IdentityFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private Task<CreatedApiKey> CreateAsync(ApiKeyRequest request) =>
        _fixture.WithAsync(services => services.GetRequiredService<ApiKeyService>().CreateAsync(request, Token));

    private static Task<AuthenticateResult> AuthenticateAsync(IdentityFixture fixture, string scheme, Action<HttpRequest> present) =>
        fixture.WithAsync(services =>
        {
            var http = new DefaultHttpContext { RequestServices = services };
            present(http.Request);
            return http.AuthenticateAsync(scheme);
        });

    private Task<AuthenticateResult> AuthenticateAsync(Action<HttpRequest> present) =>
        AuthenticateAsync(_fixture, ApiKeyAuthentication.Scheme, present);

    private async Task<Principal> SignInAsync(ApiKeyRequest request)
    {
        var created = await CreateAsync(request);
        var result = await AuthenticateAsync(http => http.Headers[ApiKeyAuthentication.Header] = created.Secret);
        result.Succeeded.ShouldBeTrue(result.Failure?.Message);
        var claims = result.Principal!.Claims.ToDictionary(claim => claim.Type, claim => claim.Value);
        return new Principal(
            PrincipalKind.ApiKey,
            Guid.Parse(claims[CmsClaimTypes.Subject]),
            claims[CmsClaimTypes.Name],
            claims.Where(claim => claim.Key is not (CmsClaimTypes.Subject or CmsClaimTypes.Name or CmsClaimTypes.Kind)));
    }

    private Task<T> AsAsync<T>(Principal principal, Func<IContentService, Task<T>> work, SiteId? site = null) =>
        _fixture.Database.AsAsync(principal, services => work(services.GetRequiredService<IContentService>()), site: site);

    private static async Task ShouldBeHiddenAsync<T>(Func<Task<T>> work)
    {
        var exception = await Should.ThrowAsync<ValidationException>(work);
        exception.Errors.ShouldHaveSingleItem().Code.ShouldBe(QueryErrorCodes.UnknownField);
    }

    private Task<Principal> RootAsync() => SignInAsync(new ApiKeyRequest("root", ApiKeyClass.Management, BuiltInRoles.SuperAdmin));

    [Fact]
    public async Task AKeyWithAnyOtherCredentialIsRefused()
    {
        var a = await CreateAsync(new ApiKeyRequest("A", ApiKeyClass.Delivery, BuiltInRoles.Editor));
        var b = await CreateAsync(new ApiKeyRequest("B", ApiKeyClass.Management, BuiltInRoles.Editor));
        var cases = new Action<HttpRequest>[]
        {
            http =>
            {
                http.Headers.Authorization = $"ApiKey {a.Secret}";
                http.Headers[ApiKeyAuthentication.Header] = b.Secret;
            },
            http =>
            {
                http.Headers.Authorization = "Bearer some.jwt.token";
                http.Headers[ApiKeyAuthentication.Header] = b.Secret;
            },
            http =>
            {
                http.Headers.Authorization = "ApiKey garbage";
                http.Headers[ApiKeyAuthentication.Header] = b.Secret;
            },
            http => http.Headers[ApiKeyAuthentication.Header] = new StringValues([a.Secret, b.Secret]),
            http => http.Headers.Authorization = new StringValues([$"ApiKey {a.Secret}", $"ApiKey {b.Secret}"]),
        };

        foreach (var present in cases)
        {
            var result = await AuthenticateAsync(present);
            result.Succeeded.ShouldBeFalse();
            result.Failure!.Message.ShouldStartWith(ApiKeyErrorCodes.SeveralCredentials);
        }
    }

    [Fact]
    public async Task TheSchemeIsMatchedOnAnyWhitespaceAndAnEmptyKeyIsRefused()
    {
        var key = await CreateAsync(new ApiKeyRequest("A", ApiKeyClass.Delivery, BuiltInRoles.Editor));

        (await AuthenticateAsync(http => http.Headers.Authorization = $"ApiKey\t{key.Secret}")).Succeeded.ShouldBeTrue();
        (await AuthenticateAsync(http => http.Headers.Authorization = $"  APIKEY   {key.Secret} ")).Succeeded.ShouldBeTrue();

        foreach (var value in new[] { "ApiKey ", "ApiKey" })
        {
            var result = await AuthenticateAsync(http => http.Headers.Authorization = value);
            result.None.ShouldBeFalse(value);
            result.Failure!.Message.ShouldStartWith(ApiKeyErrorCodes.InvalidKey);
        }

        (await AuthenticateAsync(http => http.Headers[ApiKeyAuthentication.Header] = "")).Failure!.Message.ShouldStartWith(ApiKeyErrorCodes.InvalidKey);
    }

    [Fact]
    public async Task AKeyUsedConcurrentlySignsInEveryTimeAndWritesLastUsedAtOnce()
    {
        var key = await CreateAsync(new ApiKeyRequest("C", ApiKeyClass.Delivery, BuiltInRoles.Editor));
        (await AuthenticateAsync(http => http.Headers[ApiKeyAuthentication.Header] = key.Secret)).Succeeded.ShouldBeTrue();
        _fixture.Clock.Advance(ApiKeyService.LastUsedInterval + TimeSpan.FromMinutes(1));

        var results = await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => Task.Run(() => AuthenticateAsync(http => http.Headers[ApiKeyAuthentication.Header] = key.Secret))));

        results.ShouldAllBe(result => result.Succeeded);
        _fixture.Read(context => context.Set<ApiKey>().AsNoTracking().Single()).LastUsedAt.ShouldBe(_fixture.Clock.GetUtcNow());
    }

    [Fact]
    public async Task TheFluentCmsSchemeTakesAKeyAndOtherwiseLeavesTheRequestAnonymous()
    {
        var key = await CreateAsync(new ApiKeyRequest("A", ApiKeyClass.Delivery, BuiltInRoles.Editor));

        var signedIn = await AuthenticateAsync(_fixture, FluentCmsAuthentication.Scheme, http => http.Headers.Authorization = $"ApiKey {key.Secret}");
        signedIn.Succeeded.ShouldBeTrue();
        signedIn.Principal!.FindFirst(CmsClaimTypes.Subject)!.Value.ShouldBe(key.Key.Id.ToString("D"));

        (await AuthenticateAsync(_fixture, FluentCmsAuthentication.Scheme, http => http.Headers[ApiKeyAuthentication.Header] = "wrong")).Succeeded.ShouldBeFalse();
        (await AuthenticateAsync(_fixture, FluentCmsAuthentication.Scheme, http => http.Headers.Authorization = "Bearer token")).None.ShouldBeTrue();
        (await AuthenticateAsync(_fixture, FluentCmsAuthentication.Scheme, _ => { })).None.ShouldBeTrue();
    }

    [Fact]
    public async Task TheFluentCmsSchemeForwardsARequestWithoutAKeyToTheHostDefault()
    {
        using var fixture = new IdentityFixture(cms => cms.Services
            .AddAuthentication(options => options.DefaultAuthenticateScheme = HostHandler.Name)
            .AddScheme<AuthenticationSchemeOptions, HostHandler>(HostHandler.Name, configureOptions: null));
        var key = await fixture.WithAsync(services => services.GetRequiredService<ApiKeyService>().CreateAsync(new ApiKeyRequest("A", ApiKeyClass.Delivery, BuiltInRoles.Editor), Token));

        var host = await AuthenticateAsync(fixture, FluentCmsAuthentication.Scheme, http => http.Headers.Authorization = "Bearer token");
        host.Principal!.Identity!.Name.ShouldBe("host user");

        var byKey = await AuthenticateAsync(fixture, FluentCmsAuthentication.Scheme, http => http.Headers[ApiKeyAuthentication.Header] = key.Secret);
        byKey.Principal!.Identity!.AuthenticationType.ShouldBe(ApiKeyAuthentication.Scheme);
    }

    [Fact]
    public async Task ATypeBindingHoldsUnderPopulationFiltersCountsReadsAndBulk()
    {
        var root = await RootAsync();
        var author = IdOf(await AsAsync(root, content => content.CreateAsync("author", new ContentDocument { ["name"] = "Ann" }, Token)));
        var article = IdOf(await AsAsync(root, content => content.CreateAsync("article", new ContentDocument { ["title"] = "T", ["author"] = author }, Token)));
        await AsAsync(root, content => content.PublishAsync("author", author, cancellationToken: Token));
        await AsAsync(root, content => content.PublishAsync("article", article, cancellationToken: Token));
        var bound = await SignInAsync(new ApiKeyRequest("articles", ApiKeyClass.Management, BuiltInRoles.SuperAdmin) { TypeNames = ["article"] });

        var read = await AsAsync(bound, content => content.GetAsync("article", article, new GetOptions { Populate = ["author"] }, Token));
        read.ShouldNotBeNull()["author"].ShouldBeNull();

        await ShouldBeHiddenAsync(() => AsAsync(bound, content => content.QueryAsync("article", new ContentQuery { Filter = new FieldFilter("author.name", FilterOperator.Equal, "Ann") }, Token)));
        await ShouldBeHiddenAsync(() => AsAsync(bound, content => content.CountAsync("author", null, QueryStatus.Published, Token)));
        await ShouldBeHiddenAsync(() => AsAsync(bound, content => content.GetAsync("author", author, null, Token)));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => AsAsync(bound, content => content.BulkAsync("author", new BulkRequest { Operation = BulkOperation.Delete, Items = [new BulkItem(author)] }, Token)));
    }

    [Fact]
    public async Task ASiteBindingHoldsUnderQueriesCountsBulkSchedulesAndGlobalReads()
    {
        var root = await RootAsync();
        var inB = IdOf(await AsAsync(root, content => content.CreateAsync("article", new ContentDocument { ["title"] = "B" }, Token), SiteB));
        await AsAsync(root, content => content.PublishAsync("article", inB, cancellationToken: Token), SiteB);
        var tag = IdOf(await AsAsync(root, content => content.CreateAsync("tag", new ContentDocument { ["name"] = "net" }, Token)));
        await AsAsync(root, content => content.PublishAsync("tag", tag, cancellationToken: Token));
        var bound = await SignInAsync(new ApiKeyRequest("A only", ApiKeyClass.Management, BuiltInRoles.SuperAdmin) { SiteIds = [SiteA.Value] });

        (await AsAsync(bound, content => content.QueryAsync("article", new ContentQuery(), Token), SiteA)).Items.ShouldBeEmpty();
        await Should.ThrowAsync<ValidationException>(() => AsAsync(bound, content => content.QueryAsync("article", new ContentQuery { Site = SiteB }, Token), SiteA));
        await ShouldBeHiddenAsync(() => AsAsync(bound, content => content.CountAsync("article", null, QueryStatus.Published, Token), SiteB));
        var bulk = await AsAsync(bound, content => content.BulkAsync("article", new BulkRequest { Operation = BulkOperation.Unpublish, Items = [new BulkItem(inB)] }, Token), SiteA);
        bulk.Items.ShouldHaveSingleItem().Status.ShouldBe(BulkItemStatus.Failed);
        await Should.ThrowAsync<ContentAccessDeniedException>(() => AsAsync(bound, content => content.ScheduleAsync("article", inB, new ScheduleRequest(ScheduleAction.Unpublish, IdentityFixture.Start.UtcDateTime.AddDays(1)), cancellationToken: Token), SiteB));

        (await AsAsync(bound, content => content.GetAsync("tag", tag, null, Token), SiteA)).ShouldNotBeNull();
        await ShouldBeHiddenAsync(() => AsAsync(bound, content => content.GetAsync("tag", tag, null, Token), SiteB));
        _fixture.Database.Document(inB).Status.ShouldBe(EntryStatus.Published);
    }

    [Theory]
    [InlineData("Delivery")]
    [InlineData("Preview")]
    public async Task AReadingKeyOfAnAdminRoleStillCannotWrite(string keyClass)
    {
        var root = await RootAsync();
        var id = IdOf(await AsAsync(root, content => content.CreateAsync("article", new ContentDocument { ["title"] = "x" }, Token)));
        var key = await SignInAsync(new ApiKeyRequest(keyClass, Enum.Parse<ApiKeyClass>(keyClass), BuiltInRoles.SuperAdmin));

        await Should.ThrowAsync<ContentAccessDeniedException>(() => AsAsync(key, content => content.CreateAsync("article", new ContentDocument { ["title"] = "x" }, Token)));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => AsAsync(key, content => content.PublishAsync("article", id, cancellationToken: Token)));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => AsAsync(key, content => content.PatchAsync("article", id, new ContentDocument { ["title"] = "y" }, 1, Token)));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => AsAsync(key, content => content.BulkAsync("article", new BulkRequest { Operation = BulkOperation.Delete, Items = [new BulkItem(id)] }, Token)));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => AsAsync(key, content => content.ScheduleAsync("article", id, new ScheduleRequest(ScheduleAction.Publish, IdentityFixture.Start.UtcDateTime.AddDays(1)), cancellationToken: Token)));
        _fixture.Database.Document(id).Status.ShouldBe(EntryStatus.Draft);
    }

    [Theory]
    [InlineData(CmsClaimTypes.Sites, "")]
    [InlineData(CmsClaimTypes.Sites, "   ")]
    [InlineData(CmsClaimTypes.Types, "")]
    [InlineData(CmsClaimTypes.Types, "   ")]
    [InlineData(CmsClaimTypes.Types, "author")]
    public async Task APrincipalOfAnyKindCarryingABindingClaimIsHeldToIt(string claim, string value)
    {
        var user = new Principal(PrincipalKind.ServiceAccount, Guid.CreateVersion7(), "ci", new Dictionary<string, string>
        {
            [CmsClaimTypes.Roles] = BuiltInRoles.SuperAdmin.ToString(),
            [CmsClaimTypes.Scope] = TokenScopes.Management,
            [claim] = value,
        });
        var unbound = new Principal(PrincipalKind.ServiceAccount, Guid.CreateVersion7(), "ci", user.Claims.Where(pair => pair.Key != claim));

        await Should.ThrowAsync<ContentAccessDeniedException>(() => AsAsync(user, content => content.CreateAsync("article", new ContentDocument { ["title"] = "x" }, Token)));
        (await AsAsync(unbound, content => content.CreateAsync("article", new ContentDocument { ["title"] = "x" }, Token))).ShouldNotBeNull();
    }

    private sealed class HostHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Name = "Host";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "host user")], Name)), Name)));
    }
}
