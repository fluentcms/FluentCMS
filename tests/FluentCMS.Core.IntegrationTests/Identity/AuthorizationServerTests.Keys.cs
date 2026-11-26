using System.Data.Common;
using System.Net;
using System.Security.Cryptography;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Core.Identity.PreviewTokens;
using FluentCMS.Core.IntegrationTests.Diagnostics;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Jobs;
using FluentCMS.Sites;
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

    // The acceptance of #3313: a token issued before a rotation validates until it expires, the key set lists both keys
    // during the overlap, and the retired key is removed after it.
    [Fact]
    public async Task TheJobRotatesAKeyThatIsDueAndRemovesItOnceWhatItSignedHasExpired()
    {
        var logs = new LogCapture();
        await using var host = NewHost().WithServices(services =>
        {
            services.AddLogging(logging => logging.AddProvider(logs));
            services.Configure<SecurityOptions>(security => security.KeyRotation.Enabled = true);
        });
        var editor = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        await host.SignInAsync(editor);
        var started = host.Time.GetUtcNow();
        var first = SigningKeys(host).Single(key => key.Use == SigningKey.Signing);
        var firstEncryption = SigningKeys(host).Single(key => key.Use == SigningKey.Encryption);
        using var client = host.CreateSecureClient();

        // A key signs for the interval of 90 days, so its successor is made a publish lead of a day before that.
        host.Time.Set(first.ActiveFrom.AddDays(89).AddSeconds(-1));
        await RunKeysJobAsync(host);
        SigningKeys(host).Count.ShouldBe(2);

        host.Time.Advance(TimeSpan.FromSeconds(1));
        await RunKeysJobAsync(host);
        await RunKeysJobAsync(host);
        var made = host.Time.GetUtcNow();
        var keys = SigningKeys(host);
        keys.Count.ShouldBe(4);
        var second = keys.Single(key => key.Use == SigningKey.Signing && key.Id != first.Id);
        var secondEncryption = keys.Single(key => key.Use == SigningKey.Encryption && key.Id != firstEncryption.Id);
        var overlap = TimeSpan.FromDays(14) + TimeSpan.FromSeconds(30) + TimeSpan.FromMinutes(5);
        foreach (var (successor, predecessor) in new[] { (second, keys.Single(key => key.Id == first.Id)), (secondEncryption, keys.Single(key => key.Id == firstEncryption.Id)) })
        {
            successor.Sequence.ShouldBe(2);
            successor.CreatedAt.ShouldBe(made);
            successor.ActiveFrom.ShouldBe(made.AddDays(1));
            successor.RetiredAt.ShouldBeNull();
            predecessor.RetiredAt.ShouldBe(successor.ActiveFrom + overlap);
        }

        logs.Entries.Where(entry => entry.EventId.Id == OidcLog.KeyMadeId && (int)entry.State["Sequence"]! == 2).Select(entry => entry.State["KeyId"])
            .ShouldBe([second.Id, secondEncryption.Id], ignoreOrder: true);
        (await KeySetAsync(client)).ShouldBe([KeyId(first), KeyId(second)], ignoreOrder: true);

        // The last tokens of the first key, issued a second before the second one signs.
        host.Time.Set(second.ActiveFrom.AddSeconds(-1));
        var last = await host.SignInAsync(editor, "openid", "offline_access", "management");
        Header(last.AccessToken).GetProperty("kid").GetString().ShouldBe(KeyId(first));

        host.Time.Advance(TimeSpan.FromSeconds(1));
        Header((await host.SignInAsync(editor)).AccessToken).GetProperty("kid").GetString().ShouldBe(KeyId(second));

        // They are good until they expire: the access token after its hour less a minute, the refresh token after 14 days.
        host.Time.Set(second.ActiveFrom.AddMinutes(58));
        using (var api = Bearer(last.AccessToken, host))
        {
            (await WhoAmIAsync(api)).GetProperty("id").GetGuid().ShouldBe(editor.Id);
        }

        host.Time.Set(second.ActiveFrom.AddDays(14).AddMinutes(-1));
        await RunKeysJobAsync(host);
        (await KeySetAsync(client)).ShouldBe([KeyId(first), KeyId(second)], ignoreOrder: true);
        var refreshed = await TokenAsync(client, Refresh(last.RefreshToken!), Token);
        Header(refreshed.AccessToken).GetProperty("kid").GetString().ShouldBe(KeyId(second));

        // After the overlap the instance stops accepting the first keys, and the job's next run removes their rows.
        host.Time.Set(second.ActiveFrom + overlap);
        (await KeySetAsync(client)).ShouldBe([KeyId(second)]);
        SigningKeys(host).Count.ShouldBe(4);
        await RunKeysJobAsync(host);
        SigningKeys(host).Select(key => key.Id).ShouldBe([second.Id, secondEncryption.Id], ignoreOrder: true);
        var removed = logs.Entries.Where(entry => entry.EventId.Id == OidcLog.KeyRemovedId).ToList();
        removed.Select(entry => entry.State["KeyId"]).ShouldBe([first.Id, firstEncryption.Id], ignoreOrder: true);
        started.ShouldBeLessThan(made);
    }

    [Fact]
    public async Task WithRotationOffTheJobMakesNoSuccessorAndStillRemovesWhatRetired()
    {
        await using var host = NewHost();
        var editor = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        await host.SignInAsync(editor);
        var first = SigningKeys(host).Single(key => key.Use == SigningKey.Signing);

        host.Time.Advance(TimeSpan.FromDays(400));
        await RunKeysJobAsync(host);
        SigningKeys(host).Count.ShouldBe(2);

        var now = host.Time.GetUtcNow();
        var retired = AddKey(host, SigningKey.Signing, sequence: 0, now.AddDays(-500), retiredAt: now);
        var retiring = AddKey(host, SigningKey.Signing, sequence: -1, now.AddDays(-600), retiredAt: now.AddSeconds(1));
        await RunKeysJobAsync(host);

        SigningKeys(host).Where(key => key.Use == SigningKey.Signing).Select(key => key.Id).ShouldBe([first.Id, retiring.Id], ignoreOrder: true);
        retired.RetiredAt.ShouldBe(now);
    }

    [Fact]
    public async Task TheJobMovesARetirementLaterAndNeverEarlierAndGivesARevokedKeyNoMoreTime()
    {
        await using var host = NewHost();
        var editor = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        await host.SignInAsync(editor);
        var now = host.Time.GetUtcNow();
        var overlap = TimeSpan.FromDays(14) + TimeSpan.FromSeconds(30) + TimeSpan.FromMinutes(5);
        var encryption = SigningKeys(host).Single(key => key.Use == SigningKey.Encryption);
        var first = SigningKeys(host).Single(key => key.Use == SigningKey.Signing);

        // Made when the longest lifetime was three days: too short now.
        var second = AddKey(host, SigningKey.Signing, sequence: 2, now.AddDays(-2));
        Retire(host, first.Id, second.ActiveFrom.AddDays(3));
        // Given more than the overlap, which it keeps.
        var third = AddKey(host, SigningKey.Signing, sequence: 3, now.AddDays(-1));
        Retire(host, second.Id, third.ActiveFrom.AddDays(30));
        // Revoked by a machine whose clock is a minute ahead: retired when its successor became active.
        var fourth = AddKey(host, SigningKey.Signing, sequence: 4, now.AddMinutes(1));
        Retire(host, third.Id, fourth.ActiveFrom);

        await RunKeysJobAsync(host);

        var keys = SigningKeys(host).ToDictionary(key => key.Id);
        keys[first.Id].RetiredAt.ShouldBe(second.ActiveFrom + overlap);
        keys[second.Id].RetiredAt.ShouldBe(third.ActiveFrom.AddDays(30));
        keys[third.Id].RetiredAt.ShouldBe(fourth.ActiveFrom);
        keys[fourth.Id].RetiredAt.ShouldBeNull();
        keys[encryption.Id].RetiredAt.ShouldBeNull();
    }

    // A revocation inside the overlap of a rotation retires the retiring key and the one that was active at the same
    // moment, and the job runs on an instance whose clock is a minute behind the machine that revoked.
    [Fact]
    public async Task TheJobGivesNoKeyOfARevocationItsTimeBackWhateverItsPlace()
    {
        await using var host = NewHost();
        await host.SignInAsync(await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token));
        var now = host.Time.GetUtcNow();
        var first = SigningKeys(host).Single(key => key.Use == SigningKey.Signing);
        var second = AddKey(host, SigningKey.Signing, sequence: 2, now.AddDays(-1));
        var third = AddKey(host, SigningKey.Signing, sequence: 3, now.AddMinutes(1));
        Retire(host, first.Id, third.ActiveFrom);
        Retire(host, second.Id, third.ActiveFrom);

        await RunKeysJobAsync(host);

        var keys = SigningKeys(host).ToDictionary(key => key.Id);
        keys[first.Id].RetiredAt.ShouldBe(third.ActiveFrom);
        keys[second.Id].RetiredAt.ShouldBe(third.ActiveFrom);
        keys[third.Id].RetiredAt.ShouldBeNull();
    }

    // The revocation commits after the job read the row and before it writes it.
    [Fact]
    public async Task TheJobDoesNotOverwriteARevocationThatCameAfterItReadTheRow()
    {
        var database = new SigningKeyReadFailure();
        await using var host = NewHost().Configure(cms => cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(database)));
        await host.SignInAsync(await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token));
        var now = host.Time.GetUtcNow();
        var first = SigningKeys(host).Single(key => key.Use == SigningKey.Signing);
        var second = AddKey(host, SigningKey.Signing, sequence: 2, now.AddDays(-2));

        // Too short for the overlap of this moment, so the job means to move it later.
        Retire(host, first.Id, second.ActiveFrom.AddDays(3));
        database.BeforeUpdate = () => Retire(host, first.Id, now);

        await RunKeysJobAsync(host);

        database.BeforeUpdate.ShouldBeNull();
        SigningKeys(host).Single(key => key.Id == first.Id).RetiredAt.ShouldBe(now);
    }

    // A database that hangs must not hold a probe for its timeout.
    [Fact]
    public async Task AProbeNeitherReadsTheKeysNorWaitsForARead()
    {
        var failure = new SigningKeyReadFailure();
        var logs = new LogCapture();
        await using var host = NewHost()
            .WithServices(services => services.AddLogging(logging => logging.AddProvider(logs)))
            .Configure(cms => cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(failure)));
        using var client = host.CreateSecureClient();
        await KeySetAsync(client);
        failure.Fails = true;
        host.Time.Advance(SigningKeyRing.ReadInterval);

        using (var live = await client.GetAsync("/health/live", Token))
        {
            live.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        failure.Failed.ShouldBe(0);
        logs.Entries.ShouldNotContain(entry => entry.EventId.Id == OidcLog.KeysNotReadId);

        // The copy was 30 seconds old all along: the next request that is authenticated reads.
        await KeySetAsync(client);
        failure.Failed.ShouldBe(1);
        logs.Entries.Count(entry => entry.EventId.Id == OidcLog.KeysNotReadId).ShouldBe(1);
    }

    [Fact]
    public async Task AReadThatTakesLongToFailIsTriedAgain30SecondsAfterItEnded()
    {
        var failure = new SigningKeyReadFailure();
        await using var host = NewHost().Configure(cms => cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(failure)));
        using var client = host.CreateSecureClient();
        await KeySetAsync(client);

        // The read takes the 30 seconds of a database's timeout to fail.
        failure.Fails = true;
        failure.BeforeFailing = () => host.Time.Advance(SigningKeyRing.ReadInterval);
        host.Time.Advance(SigningKeyRing.ReadInterval);
        await KeySetAsync(client);
        failure.Failed.ShouldBe(1);

        await KeySetAsync(client);
        host.Time.Advance(SigningKeyRing.ReadInterval - TimeSpan.FromSeconds(1));
        await KeySetAsync(client);
        failure.Failed.ShouldBe(1);

        host.Time.Advance(TimeSpan.FromSeconds(1));
        await KeySetAsync(client);
        failure.Failed.ShouldBe(2);
    }

    [Fact]
    public async Task OfTwoThatRotateAtOnceOneMakesTheSuccessorAndTheOtherFindsIt()
    {
        await InDatabaseFileAsync(async file =>
        {
            await using var first = NewHost().UseSqlite(file);
            await first.SignInAsync(await first.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token));
            await using var second = NewHost().UseSqlite(file);
            _ = second.Services;

            var stores = new[] { first, second, first, second }.Select(host => host.Services.GetRequiredService<SigningKeyStore>()).ToArray();
            var rotations = await Task.WhenAll(stores.Select(store => Task.Run(() => store.RotateAsync(SigningKey.Signing, TimeSpan.FromDays(1), TimeSpan.FromDays(14), Token))));

            rotations.Count(rotation => rotation.Made).ShouldBe(1);
            rotations.Select(rotation => rotation.Key.Id).Distinct().ShouldHaveSingleItem();
            var keys = SigningKeys(first).Where(key => key.Use == SigningKey.Signing).OrderBy(key => key.Sequence).ToList();
            keys.Select(key => key.Sequence).ShouldBe([1, 2]);
            keys[0].RetiredAt.ShouldBe(keys[1].ActiveFrom.AddDays(14));
        });
    }

    // The acceptance of #3313: replacing the configured certificate with the previous one listed signs nobody out, and the
    // key set lists both. The new certificate expires before the old one, which OpenIddict's own order would let sign.
    [Fact]
    public async Task ReplacingTheSigningCertificateWithTheOldOneListedSignsNobodyOut()
    {
        await InDatabaseFileAsync(async file =>
        {
            var directory = Path.GetDirectoryName(file)!;
            var old = WriteCertificate(directory, "old.pfx", notAfter: DateTimeOffset.UtcNow.AddDays(60));
            var next = WriteCertificate(directory, "new.pfx", notAfter: DateTimeOffset.UtcNow.AddDays(30));
            TestUser editor;
            TestTokens before;
            TestTokens published;
            await using (var first = WithCertificate(NewHost().UseSqlite(file), old.Path))
            {
                editor = await first.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
                before = await first.SignInAsync(editor, "openid", "offline_access", "management");
                Header(before.AccessToken).GetProperty("kid").GetString().ShouldBe(old.Thumbprint);
            }

            // With several instances the new certificate is listed first, so each accepts it before any signs with it.
            await using (var publishing = WithCertificate(NewHost().UseSqlite(file), old.Path, next.Path))
            {
                using var client = publishing.CreateSecureClient();
                (await KeySetAsync(client)).ShouldBe([old.Thumbprint, next.Thumbprint], ignoreOrder: true);
                published = await publishing.SignInAsync(editor, "openid", "offline_access", "management");
                Header(published.AccessToken).GetProperty("kid").GetString().ShouldBe(old.Thumbprint);
            }

            await using (var replaced = WithCertificate(NewHost().UseSqlite(file), next.Path, old.Path))
            {
                foreach (var tokens in new[] { before, published })
                {
                    using var api = Bearer(tokens.AccessToken, replaced);
                    (await WhoAmIAsync(api)).GetProperty("id").GetGuid().ShouldBe(editor.Id);
                }

                using var client = replaced.CreateSecureClient();
                var after = await TokenAsync(client, Refresh(before.RefreshToken!), Token);
                Header(after.AccessToken).GetProperty("kid").GetString().ShouldBe(next.Thumbprint);
                Header(after.IdentityToken!).GetProperty("kid").GetString().ShouldBe(next.Thumbprint);
                (await KeySetAsync(client)).ShouldBe([next.Thumbprint, old.Thumbprint], ignoreOrder: true);

                // The old certificate is handed over without its private key, so it could not sign whatever its place.
                var options = replaced.Services.GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>().CurrentValue;
                options.SigningCredentials.Select(credentials => credentials.Key.KeyId).ShouldBe([next.Thumbprint, old.Thumbprint]);
                ((AsymmetricSecurityKey)options.SigningCredentials[1].Key).PrivateKeyStatus.ShouldBe(PrivateKeyStatus.DoesNotExist);
                SigningKeys(replaced).ShouldAllBe(key => key.Use == SigningKey.Encryption);
            }

            // Not listed, the old certificate's tokens are refused.
            await using var alone = WithCertificate(NewHost().UseSqlite(file), next.Path);
            using var refused = Bearer(published.AccessToken, alone);
            (await refused.GetAsync("/api/management/v1/test/whoami", Token)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        });
    }

    // What ADR 0008 left to be proven: a signature of a certificate that has since expired validates while it is listed.
    [Fact]
    public async Task WhatACertificateSignedBeforeItExpiredIsAcceptedWhileItIsListed()
    {
        await InDatabaseFileAsync(async file =>
        {
            var directory = Path.GetDirectoryName(file)!;
            var expired = WriteCertificate(directory, "expired.pfx", DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow.AddDays(-1));
            var next = WriteCertificate(directory, "new.pfx");
            TestUser editor;
            TestTokens before;
            var first = WithCertificate(NewHost().UseSqlite(file), expired.Path);
            first.Time.Set(DateTimeOffset.UtcNow.AddDays(-2));
            await using (first)
            {
                editor = await first.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
                before = await first.SignInAsync(editor, "openid", "offline_access", "management");
                Header(before.RefreshToken!).GetProperty("kid").GetString().ShouldNotBeNull();
            }

            await using var host = WithCertificate(NewHost().UseSqlite(file), next.Path, expired.Path);
            using var client = host.CreateSecureClient();

            var after = await TokenAsync(client, Refresh(before.RefreshToken!), Token);

            Header(after.AccessToken).GetProperty("kid").GetString().ShouldBe(next.Thumbprint);
            (await KeySetAsync(client)).ShouldBe([next.Thumbprint, expired.Thumbprint], ignoreOrder: true);
        });
    }

    [Fact]
    public async Task ASigningCertificateOutsideItsValidityPeriodStopsTheHost()
    {
        await InDatabaseFileAsync(async file =>
        {
            var directory = Path.GetDirectoryName(file)!;
            var expired = WriteCertificate(directory, "expired.pfx", DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow.AddDays(-1));
            var early = WriteCertificate(directory, "early.pfx", DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(30));
            foreach (var certificate in new[] { expired, early })
            {
                await using var host = WithCertificate(NewHost().UseSqlite(file), certificate.Path);

                var exception = Should.Throw<FluentCmsConfigurationException>(() => host.Services);

                exception.Code.ShouldBe("FCMS3022");
                exception.Message.ShouldStartWith($"FCMS3022: FluentCms:Security:SigningCertificate:Path is '{certificate.Path}', a certificate (thumbprint {certificate.Thumbprint}) that is valid from ");
                exception.Message.ShouldNotContain(CertificatePassword);
            }

            // Either may be listed as a certificate that only validates.
            var signs = WriteCertificate(directory, "signs.pfx");
            await using var listed = WithCertificate(NewHost().UseSqlite(file), signs.Path, expired.Path, early.Path);
            using var client = listed.CreateSecureClient();
            (await KeySetAsync(client)).ShouldBe([signs.Thumbprint, expired.Thumbprint, early.Thumbprint], ignoreOrder: true);
        });
    }

    [Fact]
    public async Task APreviousSigningCertificateThatCannotBeLoadedStopsTheHost()
    {
        await InDatabaseFileAsync(async file =>
        {
            var directory = Path.GetDirectoryName(file)!;
            var signs = WriteCertificate(directory, "signs.pfx");
            var missing = Path.Combine(directory, "missing.pfx");

            await using (var host = WithCertificate(NewHost().UseSqlite(file), signs.Path, signs.Path, missing))
            {
                var exception = Should.Throw<FluentCmsConfigurationException>(() => host.Services);
                exception.Code.ShouldBe("FCMS3022");
                exception.Message.ShouldStartWith($"FCMS3022: FluentCms:Security:PreviousSigningCertificates:1:Path is '{missing}', which cannot be read as a PKCS#12 file with this password");
                exception.Message.ShouldNotContain(CertificatePassword);
            }

            await using (var host = NewHost().UseSqlite(file).WithServices(services => services.Configure<SecurityOptions>(security =>
                security.PreviousSigningCertificates.Add(new SigningCertificateOptions { Password = CertificatePassword }))))
            {
                var exception = Should.Throw<FluentCmsConfigurationException>(() => host.Services);
                exception.Code.ShouldBe("FCMS3022");
                exception.Message.ShouldBe("FCMS3022: FluentCms:Security:PreviousSigningCertificates:0:Path is not set; each entry names its PKCS#12 file.");
            }

            // An entry with neither is absent, so the environment can blank one of a settings file.
            await using var blank = NewHost().UseSqlite(file).WithServices(services => services.Configure<SecurityOptions>(security =>
                security.PreviousSigningCertificates.Add(new SigningCertificateOptions { Path = "", Password = "" })));
            using var client = blank.CreateSecureClient();
            (await KeySetAsync(client)).ShouldHaveSingleItem();
        });
    }

    // Moving between the modes: the generated keys a certificate replaced stay accepted until the job has retired them, and
    // going back, the certificate is listed for the overlap while the newest generated key signs again.
    [Fact]
    public async Task ACertificateTakesOverFromAGeneratedKeyAndGivesBackToItWithoutSigningAnyoneOut()
    {
        await InDatabaseFileAsync(async file =>
        {
            var certificate = WriteCertificate(Path.GetDirectoryName(file)!, "signing.pfx");
            TestUser editor;
            TestTokens generated;
            TestTokens certified;
            SigningKey first;
            await using (var before = NewHost().UseSqlite(file))
            {
                editor = await before.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
                generated = await before.SignInAsync(editor, "openid", "offline_access", "management");
                first = SigningKeys(before).Single(key => key.Use == SigningKey.Signing);
            }

            await using (var host = WithCertificate(NewHost().UseSqlite(file), certificate.Path))
            {
                using (var api = Bearer(generated.AccessToken, host))
                {
                    (await WhoAmIAsync(api)).GetProperty("id").GetGuid().ShouldBe(editor.Id);
                }

                using var client = host.CreateSecureClient();
                certified = await TokenAsync(client, Refresh(generated.RefreshToken!), Token);
                Header(certified.AccessToken).GetProperty("kid").GetString().ShouldBe(certificate.Thumbprint);
                (await KeySetAsync(client)).ShouldBe([certificate.Thumbprint, KeyId(first)], ignoreOrder: true);

                // The job gives the generated key the overlap from its run, and makes no signing key while a certificate signs.
                var run = host.Time.GetUtcNow();
                await RunKeysJobAsync(host);
                var keys = SigningKeys(host);
                keys.Single(key => key.Use == SigningKey.Signing).RetiredAt.ShouldBe(run + DefaultOverlap);
                keys.Single(key => key.Use == SigningKey.Encryption).RetiredAt.ShouldBeNull();
                keys.Count.ShouldBe(2);
            }

            // Back to generated keys a day later: the certificate only validates now.
            await using var back = WithCertificate(NewHost().UseSqlite(file), null, certificate.Path);
            back.Time.Advance(TimeSpan.FromDays(1));
            using (var api = Bearer((await back.SignInAsync(editor)).AccessToken, back))
            {
                (await WhoAmIAsync(api)).GetProperty("id").GetGuid().ShouldBe(editor.Id);
            }

            using var again = back.CreateSecureClient();
            var regenerated = await TokenAsync(again, Refresh(certified.RefreshToken!), Token);
            Header(regenerated.AccessToken).GetProperty("kid").GetString().ShouldBe(KeyId(first));
            (await KeySetAsync(again)).ShouldBe([certificate.Thumbprint, KeyId(first)], ignoreOrder: true);

            // The key that signs again is to retire, so the job makes its successor, with rotation off, and keeps it until then.
            var now = back.Time.GetUtcNow();
            await RunKeysJobAsync(back);
            var signing = SigningKeys(back).Where(key => key.Use == SigningKey.Signing).OrderBy(key => key.Sequence).ToList();
            signing.Count.ShouldBe(2);
            signing[1].ActiveFrom.ShouldBe(now.AddDays(1));
            signing[0].RetiredAt.ShouldBe(signing[1].ActiveFrom + DefaultOverlap);
        });
    }

    // A cache that honours it holds no key that was revoked since.
    [Fact]
    public async Task TheKeySetIsNotToBeStored()
    {
        using var client = _host.CreateSecureClient();

        using var response = await client.GetAsync("/.well-known/jwks", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
        response.Headers.CacheControl.MaxAge.ShouldBeNull();
    }

    private static string KeyId(SigningKey key) => key.Id.ToString("N");

    private static async Task<List<string>> KeySetAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/.well-known/jwks", Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return [.. (await JsonAsync(response)).GetProperty("keys").EnumerateArray().Select(key => key.GetProperty("kid").GetString()!)];
    }

    private static async Task RunKeysJobAsync(FluentCmsTestHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var operation = new OperationContext(Principal.System, Origin.Job, new SiteId(DefaultSite.Id), DefaultSite.Locale, null, "keys", host.Time.GetUtcNow());
        await scope.ServiceProvider.GetRequiredService<OidcKeysJob>().ExecuteAsync(
            new OidcKeysPayload(),
            new JobContext(Guid.CreateVersion7(), OidcKeysJob.Type, "default", 1, 3, operation, Token));
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

    // Fails every read of cms_signing_key while it is told to, as a database that cannot be reached would, and lets a test
    // act right before the first update of the table, as another process that commits in between would.
    private sealed class SigningKeyReadFailure : DbCommandInterceptor
    {
        private int _failed;

        public bool Fails { get; set; }

        public int Failed => Volatile.Read(ref _failed);

        public Action? BeforeFailing { get; set; }

        public Action? BeforeUpdate { get; set; }

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

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (BeforeUpdate is { } act && command.CommandText.Contains("UPDATE", StringComparison.OrdinalIgnoreCase) && command.CommandText.Contains("cms_signing_key", StringComparison.Ordinal))
            {
                BeforeUpdate = null;
                act();
            }

            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Check(DbCommand command)
        {
            if (Fails && command.CommandText.Contains("cms_signing_key", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _failed);
                BeforeFailing?.Invoke();
                throw new InvalidOperationException("The read fails for the test.");
            }
        }
    }
}
