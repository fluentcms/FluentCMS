using System.Data.Common;
using System.Net;
using System.Security.Cryptography;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Core.Identity.PreviewTokens;
using FluentCMS.Core.IntegrationTests.Diagnostics;
using FluentCMS.Core.Storage;
using FluentCMS.Testing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Identity;

// The keys of cms_signing_key by state, and what replaces them without signing anyone out (ADR 0008).
public sealed partial class AuthorizationServerTests
{
    [Fact]
    public async Task OnlyTheActiveKeyOfAUseSignsAndEveryKeyThatHasNotRetiredIsAccepted()
    {
        await InDatabaseFileAsync(async file =>
        {
            TestUser editor;
            TestTokens before;
            await using (var first = NewHost().UseSqlite(file))
            {
                editor = await first.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
                before = await first.SignInAsync(editor, "openid", "offline_access", "management");
            }

            await using var host = NewHost().UseSqlite(file);
            var now = host.Time.GetUtcNow();
            var retiring = SigningKeys(host).Single(key => key.Use == SigningKey.Signing);
            var decrypts = SigningKeys(host).Single(key => key.Use == SigningKey.Encryption);
            Retire(host, retiring.Id, now.AddDays(14));
            Retire(host, decrypts.Id, now.AddDays(14));
            AddKey(host, SigningKey.Signing, sequence: 0, now.AddDays(-30), retiredAt: now.AddDays(-1));
            var signs = AddKey(host, SigningKey.Signing, sequence: 2, now.AddMinutes(-1));
            var upcoming = AddKey(host, SigningKey.Signing, sequence: 3, now.AddDays(1));
            var encrypts = AddKey(host, SigningKey.Encryption, sequence: 2, now.AddMinutes(-1));
            var nextEncryption = AddKey(host, SigningKey.Encryption, sequence: 3, now.AddDays(1));

            // What the retiring keys signed and encrypted is still good.
            using (var api = Bearer(before.AccessToken, host))
            {
                (await WhoAmIAsync(api)).GetProperty("id").GetGuid().ShouldBe(editor.Id);
            }

            using var client = host.CreateSecureClient();
            var after = await TokenAsync(client, Refresh(before.RefreshToken!), Token);

            Header(before.AccessToken).GetProperty("kid").GetString().ShouldBe(KeyId(retiring));
            Header(after.AccessToken).GetProperty("kid").GetString().ShouldBe(KeyId(signs));
            Header(after.IdentityToken!).GetProperty("kid").GetString().ShouldBe(KeyId(signs));
            Header(after.RefreshToken!).GetProperty("kid").GetString().ShouldBe(KeyId(encrypts));
            (await KeySetAsync(client)).ShouldBe([KeyId(retiring), KeyId(signs), KeyId(upcoming)], ignoreOrder: true);

            // OpenIddict signs and encrypts with the first credential, and only that one could sign at all.
            var options = host.Services.GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>().CurrentValue;
            options.SigningCredentials.Select(credentials => credentials.Key.KeyId).ShouldBe([KeyId(signs), KeyId(retiring), KeyId(upcoming)]);
            options.SigningCredentials.Where(credentials => ((AsymmetricSecurityKey)credentials.Key).PrivateKeyStatus != PrivateKeyStatus.DoesNotExist)
                .ShouldHaveSingleItem().Key.KeyId.ShouldBe(KeyId(signs));
            options.EncryptionCredentials.Select(credentials => credentials.Key.KeyId).ShouldBe([KeyId(encrypts), KeyId(decrypts), KeyId(nextEncryption)]);
        });
    }

    [Fact]
    public async Task ARetiredKeyIsNoLongerAcceptedAndAUseWithNoneLeftGetsANewOne()
    {
        await InDatabaseFileAsync(async file =>
        {
            TestUser editor;
            TestTokens before;
            await using (var first = NewHost().UseSqlite(file))
            {
                editor = await first.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
                before = await first.SignInAsync(editor, "openid", "offline_access", "management");
            }

            await using var host = NewHost().UseSqlite(file);
            var retired = SigningKeys(host).Single(key => key.Use == SigningKey.Signing);
            Retire(host, retired.Id, host.Time.GetUtcNow().AddMinutes(-1));

            using (var api = Bearer(before.AccessToken, host))
            {
                using var refused = await api.GetAsync("/api/management/v1/test/whoami", Token);
                refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            }

            // The refresh token is still under the encryption key, and its signature is the retired key's.
            using var client = host.CreateSecureClient();
            using (var refresh = await PostTokenAsync(client, Refresh(before.RefreshToken!)))
            {
                refresh.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
                (await ErrorAsync(refresh)).ShouldBe("invalid_grant");
            }

            var made = SigningKeys(host).Where(key => key.Use == SigningKey.Signing && key.Id != retired.Id).ShouldHaveSingleItem();
            made.Sequence.ShouldBe(retired.Sequence + 1);
            made.ActiveFrom.ShouldBe(made.CreatedAt);
            made.RetiredAt.ShouldBeNull();
            (await KeySetAsync(client)).ShouldBe([KeyId(made)]);
            Header((await host.SignInAsync(editor)).AccessToken).GetProperty("kid").GetString().ShouldBe(KeyId(made));
        });
    }

    [Fact]
    public async Task TheFirstKeyOfAUseIsActiveAtOnceUnderARandomId()
    {
        await using var host = NewHost();
        await host.SignInAsync(await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token));
        await using var other = NewHost();
        await other.SignInAsync(await other.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token));

        var keys = SigningKeys(host);

        keys.Select(key => key.Use).ShouldBe(SigningKey.Uses, ignoreOrder: true);
        keys.ShouldAllBe(key => key.Sequence == 1 && key.ActiveFrom == key.CreatedAt && key.RetiredAt == null);
        keys.Select(key => key.Id).Intersect(SigningKeys(other).Select(key => key.Id)).ShouldBeEmpty();
    }

    // What ADR 0008 left to be proven first: OpenIddict takes the options that a change token had built again, those of
    // the server and those of the validation, which holds a copy of the server's keys.
    [Fact]
    public async Task ARunningInstanceLearnsANewKeyWithin30SecondsAndMovesItThroughItsStatesByItsClock()
    {
        var logs = new LogCapture();
        await using var host = NewHost().WithServices(services => services.AddLogging(logging => logging.AddProvider(logs)));
        var editor = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        var before = await host.SignInAsync(editor, "openid", "offline_access", "management");
        var otherSession = await host.SignInAsync(editor, "openid", "offline_access", "management");
        var now = host.Time.GetUtcNow();
        var first = SigningKeys(host).Single(key => key.Use == SigningKey.Signing);
        var firstEncryption = SigningKeys(host).Single(key => key.Use == SigningKey.Encryption);
        Retire(host, first.Id, now.AddDays(1));
        Retire(host, firstEncryption.Id, now.AddDays(1));
        var second = AddKey(host, SigningKey.Signing, sequence: 2, now.AddMinutes(10));
        var secondEncryption = AddKey(host, SigningKey.Encryption, sequence: 2, now.AddMinutes(10));
        using var client = host.CreateSecureClient();

        host.Time.Advance(TimeSpan.FromSeconds(29));
        (await KeySetAsync(client)).ShouldBe([KeyId(first)]);

        // Read again: the new key is published before it signs.
        host.Time.Advance(TimeSpan.FromSeconds(1));
        (await KeySetAsync(client)).ShouldBe([KeyId(first), KeyId(second)], ignoreOrder: true);
        var published = await TokenAsync(client, Refresh(before.RefreshToken!), Token);
        Header(published.AccessToken).GetProperty("kid").GetString().ShouldBe(KeyId(first));
        Header(published.RefreshToken!).GetProperty("kid").GetString().ShouldBe(KeyId(firstEncryption));
        logs.Entries.ShouldNotContain(entry => entry.EventId.Id == OidcLog.KeyActiveId);

        // Its time comes with no change of a row, and the validation accepts what it signs.
        host.Time.Advance(TimeSpan.FromMinutes(10));
        var rotated = await TokenAsync(client, Refresh(published.RefreshToken!), Token);
        Header(rotated.AccessToken).GetProperty("kid").GetString().ShouldBe(KeyId(second));
        Header(rotated.RefreshToken!).GetProperty("kid").GetString().ShouldBe(KeyId(secondEncryption));
        foreach (var accessToken in new[] { rotated.AccessToken, before.AccessToken })
        {
            using var api = Bearer(accessToken, host);
            (await WhoAmIAsync(api)).GetProperty("id").GetGuid().ShouldBe(editor.Id);
        }

        (await KeySetAsync(client)).ShouldBe([KeyId(first), KeyId(second)], ignoreOrder: true);
        logs.Entries.Where(entry => entry.EventId.Id == OidcLog.KeyActiveId).Select(entry => entry.State["KeyId"])
            .ShouldBe([second.Id, secondEncryption.Id], ignoreOrder: true);
        logs.Entries.ShouldNotContain(entry => entry.EventId.Id == OidcLog.KeyRetiredId);

        // The first key retires, with what it signed and encrypted.
        host.Time.Advance(TimeSpan.FromDays(1));
        (await KeySetAsync(client)).ShouldBe([KeyId(second)]);
        using (var refused = await PostTokenAsync(client, Refresh(otherSession.RefreshToken!)))
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await ErrorAsync(refused)).ShouldBe("invalid_grant");
        }

        (await TokenAsync(client, Refresh(rotated.RefreshToken!), Token)).AccessToken.ShouldNotBeNullOrEmpty();
        var retired = logs.Entries.Where(entry => entry.EventId.Id == OidcLog.KeyRetiredId).ToList();
        retired.Select(entry => entry.State["KeyId"]).ShouldBe([first.Id, firstEncryption.Id], ignoreOrder: true);
        retired.ShouldAllBe(entry => entry.Level == LogLevel.Information);
        logs.Entries.ShouldNotContain(entry => entry.Message.Contains(first.ProtectedKey, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnInstanceThatCannotReadKeepsItsKeysAndStillMovesThemByItsClock()
    {
        var failure = new SigningKeyReadFailure();
        var logs = new LogCapture();
        await using var host = NewHost()
            .WithServices(services => services.AddLogging(logging => logging.AddProvider(logs)))
            .Configure(cms => cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(failure)));
        var editor = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        var before = await host.SignInAsync(editor, "openid", "offline_access", "management");
        var now = host.Time.GetUtcNow();
        var first = SigningKeys(host).Single(key => key.Use == SigningKey.Signing);
        Retire(host, first.Id, now.AddDays(1));
        var second = AddKey(host, SigningKey.Signing, sequence: 2, now.AddMinutes(10));
        using var client = host.CreateSecureClient();
        host.Time.Advance(SigningKeyRing.ReadInterval);
        (await KeySetAsync(client)).ShouldBe([KeyId(first), KeyId(second)], ignoreOrder: true);

        failure.Fails = true;
        host.Time.Advance(TimeSpan.FromMinutes(10));
        var rotated = await TokenAsync(client, Refresh(before.RefreshToken!), Token);
        (await KeySetAsync(client)).ShouldBe([KeyId(first), KeyId(second)], ignoreOrder: true);

        Header(rotated.AccessToken).GetProperty("kid").GetString().ShouldBe(KeyId(second));
        var failed = logs.Entries.Where(entry => entry.EventId.Id == OidcLog.KeysNotReadId).ShouldHaveSingleItem();
        failed.Level.ShouldBe(LogLevel.Error);

        // The next read is 30 seconds later, not on the next request.
        host.Time.Advance(SigningKeyRing.ReadInterval);
        (await KeySetAsync(client)).Count.ShouldBe(2);
        logs.Entries.Count(entry => entry.EventId.Id == OidcLog.KeysNotReadId).ShouldBe(2);

        failure.Fails = false;
        host.Time.Advance(TimeSpan.FromDays(1));
        (await KeySetAsync(client)).ShouldBe([KeyId(second)]);
        logs.Entries.Count(entry => entry.EventId.Id == OidcLog.KeysNotReadId).ShouldBe(2);
    }

    [Fact]
    public async Task AKeyTheKeyRingCannotReadIsLeftOutAndNamedAndAnActiveOneStopsTheServer()
    {
        var logs = new LogCapture();
        await using var host = NewHost().WithServices(services => services.AddLogging(logging => logging.AddProvider(logs)));
        var editor = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        var before = await host.SignInAsync(editor, "openid", "offline_access", "management");
        var now = host.Time.GetUtcNow();
        var first = SigningKeys(host).Single(key => key.Use == SigningKey.Signing);
        var unreadable = AddKey(host, SigningKey.Signing, sequence: 2, now.AddMinutes(10), protectedKey: Convert.ToBase64String(RandomNumberGenerator.GetBytes(256)));
        using var client = host.CreateSecureClient();

        host.Time.Advance(SigningKeyRing.ReadInterval);
        (await KeySetAsync(client)).ShouldBe([KeyId(first)]);
        host.Time.Advance(SigningKeyRing.ReadInterval);
        var refreshed = await TokenAsync(client, Refresh(before.RefreshToken!), Token);

        Header(refreshed.AccessToken).GetProperty("kid").GetString().ShouldBe(KeyId(first));
        var named = logs.Entries.Where(entry => entry.EventId.Id == OidcLog.KeyUnreadableId).ShouldHaveSingleItem();
        named.Level.ShouldBe(LogLevel.Error);
        named.State["KeyId"].ShouldBe(unreadable.Id);
        named.Message.ShouldNotContain(unreadable.ProtectedKey);

        // Once it is the active key nothing is signed with another in its place, and it is not replaced.
        host.Time.Advance(TimeSpan.FromMinutes(10));
        using (var keys = await client.GetAsync("/.well-known/jwks", Token))
        {
            keys.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        }

        using (var refused = await PostTokenAsync(client, Refresh(refreshed.RefreshToken!)))
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        }

        SigningKeys(host).Where(key => key.Use == SigningKey.Signing).Select(key => key.Id).ShouldBe([first.Id, unreadable.Id], ignoreOrder: true);
        logs.Entries.ShouldContain(entry => entry.EventId.Id == OidcLog.KeysNotAppliedId);
    }

    [Fact]
    public async Task APreviewTokenIsSignedByTheActiveKeyAndGoodUntilItsKeyRetires()
    {
        await using var host = NewHost();
        var issuer = host.Services.GetRequiredService<PreviewTokenIssuer>();
        using var client = host.CreateSecureClient();
        await KeySetAsync(client);
        var now = host.Time.GetUtcNow();
        var first = SigningKeys(host).Single(key => key.Use == SigningKey.Signing);
        Dictionary<string, object> claims = new() { [CmsClaimTypes.Subject] = Guid.NewGuid().ToString("D") };
        var token = issuer.Create(claims, now, now.AddDays(1));
        Retire(host, first.Id, now.AddHours(1));
        var second = AddKey(host, SigningKey.Signing, sequence: 2, now.AddMinutes(1));

        host.Time.Advance(TimeSpan.FromMinutes(1));
        await KeySetAsync(client);

        Header(token).GetProperty("kid").GetString().ShouldBe(KeyId(first));
        Header(issuer.Create(claims, now, now.AddDays(1))).GetProperty("kid").GetString().ShouldBe(KeyId(second));
        (await issuer.ValidateAsync(token, PreviewTokenAuthentication.Scheme)).Failure.ShouldBe(PreviewTokenFailure.None);

        host.Time.Advance(TimeSpan.FromHours(1));
        await KeySetAsync(client);
        (await issuer.ValidateAsync(token, PreviewTokenAuthentication.Scheme)).Failure.ShouldBe(PreviewTokenFailure.Invalid);
    }

    private static string KeyId(SigningKey key) => key.Id.ToString("N");

    private static async Task<List<string>> KeySetAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/.well-known/jwks", Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return [.. (await JsonAsync(response)).GetProperty("keys").EnumerateArray().Select(key => key.GetProperty("kid").GetString()!)];
    }

    // A row as SigningKeyStore writes one, in a state of the test's choosing.
    private static SigningKey AddKey(FluentCmsTestHost host, string use, int sequence, DateTimeOffset activeFrom, DateTimeOffset? retiredAt = null, string? protectedKey = null)
    {
        byte[] material;
        if (use == SigningKey.Signing)
        {
            using var rsa = RSA.Create(2048);
            material = rsa.ExportRSAPrivateKey();
        }
        else
        {
            material = RandomNumberGenerator.GetBytes(32);
        }

        var key = new SigningKey
        {
            Id = Guid.NewGuid(),
            Use = use,
            Sequence = sequence,
            Algorithm = use == SigningKey.Signing ? SecurityAlgorithms.RsaSha256 : SecurityAlgorithms.Aes256KW,
            ProtectedKey = protectedKey ?? Convert.ToBase64String(host.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("FluentCMS.SigningKeys").Protect(material)),
            CreatedAt = activeFrom,
            ActiveFrom = activeFrom,
            RetiredAt = retiredAt,
        };

        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        context.Add(key);
        context.SaveChanges();
        return key;
    }

    private static void Retire(FluentCmsTestHost host, Guid id, DateTimeOffset at)
    {
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        context.Set<SigningKey>().Single(key => key.Id == id).RetiredAt = at;
        context.SaveChanges();
    }

    // Fails every read and write of cms_signing_key while it is told to, as a database that cannot be reached would.
    private sealed class SigningKeyReadFailure : DbCommandInterceptor
    {
        public bool Fails { get; set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Check(command);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Check(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Check(DbCommand command)
        {
            if (Fails && command.CommandText.Contains("cms_signing_key", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The read fails for the test.");
            }
        }
    }
}
