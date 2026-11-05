using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Events;
using FluentCMS.Core.Identity.ApiKeys;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Sites;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Identity;

public sealed class ApiKeyTests : IDisposable
{
    private readonly IdentityFixture _fixture = new(activity: true);

    public void Dispose() => _fixture.Dispose();

    private Task<CreatedApiKey> CreateAsync(ApiKeyRequest request) =>
        _fixture.WithAsync(services => services.GetRequiredService<ApiKeyService>().CreateAsync(request, Token));

    private Task<CreatedApiKey> CreateAsync(ApiKeyClass keyClass, Guid? role = null) =>
        CreateAsync(new ApiKeyRequest($"{keyClass} key", keyClass, role ?? BuiltInRoles.Editor));

    private Task<bool> RevokeAsync(Guid id) =>
        _fixture.WithAsync(services => services.GetRequiredService<ApiKeyService>().RevokeAsync(id, Token));

    private ApiKey Row(Guid id) => _fixture.Read(context => context.Set<ApiKey>().AsNoTracking().Single(key => key.Id == id));

    private Task<AuthenticateResult> AuthenticateAsync(Action<HttpRequest> present) =>
        _fixture.WithAsync(services =>
        {
            var http = new DefaultHttpContext { RequestServices = services };
            present(http.Request);
            return http.AuthenticateAsync(ApiKeyAuthentication.Scheme);
        });

    private Task<AuthenticateResult> AuthenticateAsync(string secret) =>
        AuthenticateAsync(request => request.Headers.Authorization = $"ApiKey {secret}");

    // What the API layer builds from the handler's claims (#2783): the kind, id and name from their claims, the rest as they are.
    private static Principal PrincipalOf(ClaimsPrincipal user)
    {
        var claims = user.Claims.ToDictionary(claim => claim.Type, claim => claim.Value);
        return new Principal(
            Enum.Parse<PrincipalKind>(claims[CmsClaimTypes.Kind]),
            Guid.Parse(claims[CmsClaimTypes.Subject]),
            claims[CmsClaimTypes.Name],
            claims.Where(claim => claim.Key is not (CmsClaimTypes.Subject or CmsClaimTypes.Name or CmsClaimTypes.Kind)));
    }

    private async Task<Principal> SignInAsync(CreatedApiKey key)
    {
        var result = await AuthenticateAsync(key.Secret);
        result.Succeeded.ShouldBeTrue(result.Failure?.Message);
        return PrincipalOf(result.Principal!);
    }

    private Task<T> AsAsync<T>(Principal principal, Func<IContentService, Task<T>> work, SiteId? site = null) =>
        _fixture.Database.AsAsync(principal, services => work(services.GetRequiredService<IContentService>()), site: site);

    private static ContentDocument Article(string title) => new() { ["title"] = title };

    [Theory]
    [InlineData("Delivery", "fcms_dk_")]
    [InlineData("Preview", "fcms_pk_")]
    [InlineData("Management", "fcms_mk_")]
    public async Task ACreatedKeyIsReturnedOnceAndOnlyItsHashIsStored(string keyClass, string marker)
    {
        var created = await CreateAsync(Enum.Parse<ApiKeyClass>(keyClass));

        created.Secret.ShouldStartWith(marker);
        created.Secret.Length.ShouldBe(marker.Length + 43);
        created.ToString().ShouldNotContain(created.Secret);

        var row = Row(created.Key.Id);
        row.KeyHash.ShouldBe(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(created.Secret))));
        row.KeyPrefix.ShouldBe(created.Secret[..16]);
        _fixture.Read(context => context.Set<ApiKey>().AsNoTracking().Select(key => key.Name + key.KeyHash + key.KeyPrefix + key.SiteIds + key.TypeNames).Single())
            .ShouldNotContain(created.Secret[16..]);

        var listed = await _fixture.WithAsync(services => services.GetRequiredService<ApiKeyService>().ListAsync(Token));
        listed.ShouldHaveSingleItem().ShouldBe(created.Key);
        typeof(ApiKeyInfo).GetProperties().Select(property => property.Name).ShouldNotContain("Secret");
        typeof(ApiKeyInfo).GetProperties().Select(property => property.Name).ShouldNotContain("KeyHash");
    }

    [Fact]
    public async Task AKeyIsCreatedOnlyWithWhatExists()
    {
        var exception = await Should.ThrowAsync<ValidationException>(() => CreateAsync(new ApiKeyRequest(" ", ApiKeyClass.Management, Guid.NewGuid())
        {
            SiteIds = [Guid.NewGuid()],
            TypeNames = ["wizard"],
            ExpiresAt = IdentityFixture.Start,
            RateLimit = 0,
        }));

        exception.Errors.Select(error => error.Code).ShouldBe(
            [ApiKeyErrorCodes.InvalidName, "FCMS3004", ApiKeyErrorCodes.UnknownSite, ApiKeyErrorCodes.UnknownType, ApiKeyErrorCodes.InvalidExpiry, ApiKeyErrorCodes.InvalidRateLimit],
            ignoreOrder: true);
        _fixture.Read(context => context.Set<ApiKey>().Count()).ShouldBe(0);
    }

    [Fact]
    public async Task AnEmptyListOfSitesOrTypesIsRefusedRatherThanReadAsEvery()
    {
        var exception = await Should.ThrowAsync<ValidationException>(() => CreateAsync(new ApiKeyRequest("empty", ApiKeyClass.Management, BuiltInRoles.Editor)
        {
            SiteIds = [],
            TypeNames = [],
        }));

        exception.Errors.Select(error => (error.Path, error.Code)).ShouldBe([("siteIds", ApiKeyErrorCodes.EmptyBinding), ("typeNames", ApiKeyErrorCodes.EmptyBinding)], ignoreOrder: true);
        _fixture.Read(context => context.Set<ApiKey>().Count()).ShouldBe(0);
    }

    [Fact]
    public async Task TheHandlerAcceptsBothHeaderFormsAndSetsTheClaimsOfTheContract()
    {
        var created = await CreateAsync(new ApiKeyRequest("Site A articles", ApiKeyClass.Management, BuiltInRoles.Editor)
        {
            SiteIds = [SiteA.Value, SiteB.Value],
            TypeNames = ["article", "author"],
            RateLimit = 120,
        });

        var byAuthorization = await AuthenticateAsync(request => request.Headers.Authorization = $"apikey {created.Secret}");
        var byHeader = await AuthenticateAsync(request => request.Headers[ApiKeyAuthentication.Header] = created.Secret);

        foreach (var result in new[] { byAuthorization, byHeader })
        {
            result.Succeeded.ShouldBeTrue();
            var claims = result.Principal!.Claims.ToDictionary(claim => claim.Type, claim => claim.Value);
            claims.ShouldBe(new Dictionary<string, string>
            {
                ["sub"] = created.Key.Id.ToString("D"),
                ["name"] = "Site A articles",
                ["fcms_kind"] = "ApiKey",
                ["roles"] = BuiltInRoles.Editor.ToString("D"),
                ["scope"] = "management",
                ["fcms_sites"] = $"{SiteA.Value:D} {SiteB.Value:D}",
                ["fcms_types"] = "article author",
                ["fcms_rate_limit"] = "120",
            });
            result.Principal.Identity!.Name.ShouldBe("Site A articles");
            result.Principal.Identity.AuthenticationType.ShouldBe(ApiKeyAuthentication.Scheme);
        }
    }

    [Theory]
    [InlineData("Delivery", "delivery")]
    [InlineData("Preview", "preview")]
    public async Task AKeyWithNoBindingOrLimitHasNoneOfThoseClaims(string keyClass, string scope)
    {
        var created = await CreateAsync(Enum.Parse<ApiKeyClass>(keyClass));

        var claims = (await AuthenticateAsync(created.Secret)).Principal!.Claims.Select(claim => claim.Type + "=" + claim.Value).ToList();

        claims.ShouldBe(
            [$"sub={created.Key.Id:D}", $"name={keyClass} key", "fcms_kind=ApiKey", $"roles={BuiltInRoles.Editor:D}", $"scope={scope}"],
            ignoreOrder: true);
    }

    [Fact]
    public async Task ARequestWithoutAKeyIsLeftToOtherSchemes()
    {
        (await AuthenticateAsync(_ => { })).None.ShouldBeTrue();
        (await AuthenticateAsync(request => request.Headers.Authorization = "Bearer abc")).None.ShouldBeTrue();
    }

    [Fact]
    public async Task AWrongExpiredOrRevokedKeyIsRefusedWithoutNamingIt()
    {
        var created = await CreateAsync(ApiKeyClass.Delivery);
        var expiring = await CreateAsync(new ApiKeyRequest("short", ApiKeyClass.Delivery, BuiltInRoles.Editor) { ExpiresAt = IdentityFixture.Start.AddHours(1) });
        var revoked = await CreateAsync(ApiKeyClass.Preview);
        (await RevokeAsync(revoked.Key.Id)).ShouldBeTrue();
        _fixture.Clock.Advance(TimeSpan.FromHours(1));

        // Same prefix, other secret: found by the prefix, refused by the hash.
        var wrong = created.Secret[..16] + new string('A', created.Secret.Length - 16);
        var cases = new (string Secret, string Code)[]
        {
            (wrong, ApiKeyErrorCodes.InvalidKey),
            ("fcms_dk_short", ApiKeyErrorCodes.InvalidKey),
            ("not a key at all", ApiKeyErrorCodes.InvalidKey),
            (expiring.Secret, ApiKeyErrorCodes.Expired),
            (revoked.Secret, ApiKeyErrorCodes.Revoked),
        };

        foreach (var (secret, code) in cases)
        {
            var result = await AuthenticateAsync(secret);
            result.Succeeded.ShouldBeFalse(secret);
            result.Failure!.Message.ShouldStartWith(code);
            result.Failure.Message.ShouldNotContain(secret);
        }

        (await AuthenticateAsync(created.Secret)).Succeeded.ShouldBeTrue();
        _fixture.Logs.Messages.ShouldNotContain(message => message.Contains(expiring.Secret, StringComparison.Ordinal) || message.Contains(revoked.Secret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AUsedKeyUpdatesLastUsedAtAtMostOncePerInterval()
    {
        var created = await CreateAsync(ApiKeyClass.Delivery);
        Row(created.Key.Id).LastUsedAt.ShouldBeNull();

        await SignInAsync(created);
        Row(created.Key.Id).LastUsedAt.ShouldBe(IdentityFixture.Start);

        _fixture.Clock.Advance(ApiKeyService.LastUsedInterval - TimeSpan.FromSeconds(1));
        await SignInAsync(created);
        Row(created.Key.Id).LastUsedAt.ShouldBe(IdentityFixture.Start);

        _fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        await SignInAsync(created);
        Row(created.Key.Id).LastUsedAt.ShouldBe(IdentityFixture.Start + ApiKeyService.LastUsedInterval);
    }

    [Fact]
    public async Task ADeliveryKeyReadsPublishedAndIsRefusedDraftsWhileAPreviewKeyReadsDrafts()
    {
        var editor = await SignInAsync(await CreateAsync(ApiKeyClass.Management));
        var published = IdOf(await AsAsync(editor, content => content.CreateAsync("article", Article("Out"), Token)));
        await AsAsync(editor, content => content.PublishAsync("article", published, cancellationToken: Token));
        await AsAsync(editor, content => content.CreateAsync("article", Article("Draft"), Token));
        var delivery = await SignInAsync(await CreateAsync(ApiKeyClass.Delivery));
        var preview = await SignInAsync(await CreateAsync(ApiKeyClass.Preview));
        var drafts = new ContentQuery { Status = QueryStatus.Draft };

        var read = await AsAsync(delivery, content => content.QueryAsync("article", new ContentQuery(), Token));
        read.Items.Select(IdOf).ShouldBe([published]);
        await Should.ThrowAsync<ValidationException>(() => AsAsync(delivery, content => content.QueryAsync("article", drafts, Token)));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => AsAsync(delivery, content => content.CreateAsync("article", Article("Nope"), Token)));

        (await AsAsync(preview, content => content.QueryAsync("article", drafts, Token))).Items.Count.ShouldBe(2);
        await Should.ThrowAsync<ContentAccessDeniedException>(() => AsAsync(preview, content => content.CreateAsync("article", Article("Nope"), Token)));
    }

    [Fact]
    public async Task AManagementKeyIsBoundedByItsRoleItsSitesAndItsTypes()
    {
        var role = _fixture.Database.Database.AddRoleForTest("writer", ("content.article.create", null), ("content.article.read", null), ("content.author.create", null), ("content.tag.create", null));
        var key = await SignInAsync(await CreateAsync(new ApiKeyRequest("A articles", ApiKeyClass.Management, role) { SiteIds = [SiteA.Value], TypeNames = ["article", "tag"] }));

        var created = await AsAsync(key, content => content.CreateAsync("article", Article("In A"), Token), SiteA);
        (await AsAsync(key, content => content.GetAsync("article", IdOf(created), new GetOptions { Status = QueryStatus.Draft }, Token), SiteA)).ShouldNotBeNull();

        await Should.ThrowAsync<ContentAccessDeniedException>(() => AsAsync(key, content => content.CreateAsync("article", Article("In B"), Token), SiteB));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => AsAsync(key, content => content.CreateAsync("author", new ContentDocument { ["name"] = "Ann" }, Token), SiteA));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => AsAsync(key, async content =>
        {
            await content.DeleteAsync("article", IdOf(created), Token);
            return true;
        }, SiteA));

        // A global type is shared by every site, so a key bound to some of them cannot write it, whatever its role grants.
        await Should.ThrowAsync<ContentAccessDeniedException>(() => AsAsync(key, content => content.CreateAsync("tag", new ContentDocument { ["name"] = "net" }, Token), SiteA));
    }

    [Fact]
    public async Task ABindingClaimThatNamesNothingUsableReachesNothing()
    {
        var created = await CreateAsync(ApiKeyClass.Management);
        var stored = await SignInAsync(created);
        var damaged = new Principal(stored.Kind, stored.Id, stored.Name, new Dictionary<string, string>(stored.Claims) { [CmsClaimTypes.Sites] = "not-a-site" });

        await Should.ThrowAsync<ContentAccessDeniedException>(() => AsAsync(damaged, content => content.CreateAsync("article", Article("Nope"), Token)));
        (await AsAsync(stored, content => content.CreateAsync("article", Article("Fine"), Token))).ShouldNotBeNull();
    }

    [Fact]
    public async Task AKeyRevokedOrExpiredAfterItSignedInHoldsNoRole()
    {
        var revoked = await CreateAsync(ApiKeyClass.Management);
        var expired = await CreateAsync(ApiKeyClass.Management);
        var revokedPrincipal = await SignInAsync(revoked);
        var expiredPrincipal = await SignInAsync(expired);

        await RevokeAsync(revoked.Key.Id);
        await _fixture.WithAsync(async services =>
        {
            await services.GetRequiredService<ApiKeyService>().ExpireAsync(expired.Key.Id, cancellationToken: Token);
            return true;
        });

        (await _fixture.RolesOfAsync(revokedPrincipal)).ShouldBeEmpty();
        (await _fixture.RolesOfAsync(expiredPrincipal)).ShouldBeEmpty();
        await Should.ThrowAsync<ContentAccessDeniedException>(() => AsAsync(revokedPrincipal, content => content.CreateAsync("article", Article("Nope"), Token)));
    }

    [Fact]
    public async Task AnExpiryIsOnlyBroughtForward()
    {
        var created = await CreateAsync(new ApiKeyRequest("dated", ApiKeyClass.Delivery, BuiltInRoles.Editor) { ExpiresAt = IdentityFixture.Start.AddDays(30) });
        Task Expire(DateTimeOffset? at) => _fixture.WithAsync(async services =>
        {
            await services.GetRequiredService<ApiKeyService>().ExpireAsync(created.Key.Id, at, Token);
            return true;
        });

        var exception = await Should.ThrowAsync<ValidationException>(() => Expire(IdentityFixture.Start.AddDays(31)));
        exception.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiKeyErrorCodes.InvalidExpiry);

        await Expire(IdentityFixture.Start.AddDays(1));
        Row(created.Key.Id).ExpiresAt.ShouldBe(IdentityFixture.Start.AddDays(1));
        (await AuthenticateAsync(created.Secret)).Succeeded.ShouldBeTrue();

        await Expire(null);
        (await AuthenticateAsync(created.Secret)).Failure!.Message.ShouldStartWith(ApiKeyErrorCodes.Expired);
    }

    [Fact]
    public async Task CreatingExpiringAndRevokingAKeyAreEventsAndActivityRowsWithoutTheSecretOrItsHash()
    {
        var created = await CreateAsync(ApiKeyClass.Preview);
        await _fixture.WithAsync(async services =>
        {
            await services.GetRequiredService<ApiKeyService>().ExpireAsync(created.Key.Id, IdentityFixture.Start.AddDays(2), Token);
            return true;
        });
        (await RevokeAsync(created.Key.Id)).ShouldBeTrue();
        (await RevokeAsync(created.Key.Id)).ShouldBeFalse();
        var hash = Row(created.Key.Id).KeyHash;

        var outbox = _fixture.Read(context => context.Set<OutboxMessage>().AsNoTracking().Select(message => message.EventType + message.Payload).ToList());
        await _fixture.WithAsync(services => services.GetRequiredService<OutboxDrainer>().DrainAsync(Token));
        var rows = _fixture.ActivityRows();

        rows.Select(row => row.Action).ShouldBe(["api_key.created", "api_key.expiry_changed", "api_key.revoked"], ignoreOrder: true);
        rows.Single(row => row.Action == "api_key.expiry_changed").Details.ShouldNotBeNull().ShouldContain("2027-03-03T08:00:00");
        var revoked = rows.Single(row => row.Action == "api_key.revoked");
        revoked.TargetType.ShouldBe("api_key");
        revoked.TargetId.ShouldBe(created.Key.Id.ToString("D"));
        revoked.TargetLabel.ShouldBe("Preview key");
        revoked.SiteId.ShouldBeNull();
        rows.Single(row => row.Action == "api_key.created").Details.ShouldNotBeNull().ShouldContain("preview");

        var random = created.Secret[8..16];
        List<string> written = [.. outbox, .. rows.Select(row => $"{row.TargetLabel} {row.Details}"), .. _fixture.Logs.Messages];
        written.ShouldNotContain(text => text.Contains(random, StringComparison.Ordinal) || text.Contains(hash, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RevokingAKeyThatIsNotThereIsNotFound()
    {
        var exception = await Should.ThrowAsync<ValidationException>(() => RevokeAsync(Guid.NewGuid()));

        exception.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiKeyErrorCodes.NotFound);
    }
}
