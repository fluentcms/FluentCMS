using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Commands;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Events;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Core.IntegrationTests.Diagnostics;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Identity;

// 'fluentcms keys': a command runs in a process of its own, so here in a host of its own on the database of the instance.
public sealed partial class AuthorizationServerTests
{
    private const string CertificatePassword = "test-password";

    private static readonly TimeSpan DefaultOverlap = TimeSpan.FromDays(14) + TimeSpan.FromSeconds(30) + TimeSpan.FromMinutes(5);

    [Fact]
    public async Task KeysRotateMakesTheNextKeysWithTheLeadAndASecondRunMakesNone()
    {
        await InDatabaseFileAsync(async file =>
        {
            await using var host = NewHost().UseSqlite(file);
            var editor = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
            var before = await host.SignInAsync(editor, "openid", "offline_access", "management");
            var first = SigningKeys(host).Single(key => key.Use == SigningKey.Signing);
            var firstEncryption = SigningKeys(host).Single(key => key.Use == SigningKey.Encryption);
            await using var tool = NewHost().UseSqlite(file);

            var rotated = await tool.RunCommandAsync("keys", "rotate");

            rotated.ExitCode.ShouldBe(0, rotated.Error);
            var keys = SigningKeys(host);
            var second = keys.Single(key => key.Use == SigningKey.Signing && key.Id != first.Id);
            var secondEncryption = keys.Single(key => key.Use == SigningKey.Encryption && key.Id != firstEncryption.Id);
            foreach (var (made, followed) in new[] { (second, first), (secondEncryption, firstEncryption) })
            {
                made.Sequence.ShouldBe(2);
                made.ActiveFrom.ShouldBe(made.CreatedAt.AddDays(1));
                made.RetiredAt.ShouldBeNull();
                keys.Single(key => key.Id == followed.Id).RetiredAt.ShouldBe(made.ActiveFrom + DefaultOverlap);
            }

            rotated.Output.ShouldContain($"Made the signing key {second.Id:D}. It is accepted now and signs from ");
            rotated.Output.ShouldContain($"Made the encryption key {secondEncryption.Id:D}. It is accepted now and encrypts from ");

            var again = await tool.RunCommandAsync("keys", "rotate");
            again.ExitCode.ShouldBe(0, again.Error);
            again.Output.ShouldContain($"The signing key {second.Id:D} is upcoming already");
            again.Output.ShouldContain($"The encryption key {secondEncryption.Id:D} is upcoming already");
            SigningKeys(host).Count.ShouldBe(4);

            // The running instance learns the key within 30 seconds, signs with it after the lead, and signs nobody out.
            using var client = host.CreateSecureClient();
            host.Time.Advance(SigningKeyRing.ReadInterval);
            (await KeySetAsync(client)).ShouldBe([KeyId(first), KeyId(second)], ignoreOrder: true);
            host.Time.Set(second.ActiveFrom);
            var after = await TokenAsync(client, Refresh(before.RefreshToken!), Token);
            Header(after.AccessToken).GetProperty("kid").GetString().ShouldBe(KeyId(second));
            Header(after.RefreshToken!).GetProperty("kid").GetString().ShouldBe(KeyId(secondEncryption));
        });
    }

    [Fact]
    public async Task KeysRotateTakesOneUseAndRefusesAnotherWord()
    {
        await InDatabaseFileAsync(async file =>
        {
            await using var host = NewHost().UseSqlite(file);
            await host.SignInAsync(await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token));
            await using var tool = NewHost().UseSqlite(file);

            var refused = await tool.RunCommandAsync("keys", "rotate", "--use", "jwk");
            refused.ExitCode.ShouldBe(2);
            refused.Error.ShouldStartWith("FCMS3359: --use is sig");
            SigningKeys(host).Count.ShouldBe(2);

            var rotated = await tool.RunCommandAsync("keys", "rotate", "--use", "enc");
            rotated.ExitCode.ShouldBe(0, rotated.Error);
            rotated.Output.ShouldNotContain("signing key");
            SigningKeys(host).Select(key => (key.Use, key.Sequence)).ShouldBe([(SigningKey.Encryption, 1), (SigningKey.Encryption, 2), (SigningKey.Signing, 1)], ignoreOrder: true);
        });
    }

    [Fact]
    public async Task KeysRotateRefusesTheSigningKeyWhileACertificateSigns()
    {
        await InDatabaseFileAsync(async file =>
        {
            var certificate = WriteCertificate(Path.GetDirectoryName(file)!, "signing.pfx");
            await using var host = WithCertificate(NewHost().UseSqlite(file), certificate.Path);
            await host.SignInAsync(await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token));
            await using var tool = WithCertificate(NewHost().UseSqlite(file), certificate.Path);

            var refused = await tool.RunCommandAsync("keys", "rotate", "--use", "sig");
            refused.ExitCode.ShouldBe(2);
            refused.Error.ShouldStartWith("FCMS3390: FluentCms:Security:SigningCertificate signs the tokens");
            SigningKeys(host).ShouldHaveSingleItem().Use.ShouldBe(SigningKey.Encryption);

            var rotated = await tool.RunCommandAsync("keys", "rotate");
            rotated.ExitCode.ShouldBe(0, rotated.Error);
            rotated.Error.ShouldContain("only the encryption key is rotated");
            SigningKeys(host).Select(key => (key.Use, key.Sequence)).ShouldBe([(SigningKey.Encryption, 1), (SigningKey.Encryption, 2)], ignoreOrder: true);
        });
    }

    [Fact]
    public async Task KeysListShowsEachKeyWithItsStateAndDatesAndNeverTheKey()
    {
        await InDatabaseFileAsync(async file =>
        {
            await using var host = NewHost().UseSqlite(file);
            await host.SignInAsync(await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token));
            var now = host.Time.GetUtcNow();
            var retiring = SigningKeys(host).Single(key => key.Use == SigningKey.Signing);
            var encrypts = SigningKeys(host).Single(key => key.Use == SigningKey.Encryption);
            Retire(host, retiring.Id, now.AddDays(3));
            var retired = AddKey(host, SigningKey.Signing, sequence: 0, now.AddDays(-30), retiredAt: now.AddDays(-1));
            var active = AddKey(host, SigningKey.Signing, sequence: 2, now.AddDays(-1));
            var upcoming = AddKey(host, SigningKey.Signing, sequence: 3, now.AddDays(1));
            await using var tool = NewHost().UseSqlite(file);

            var listed = await tool.RunCommandAsync("keys", "list");

            listed.ExitCode.ShouldBe(0, listed.Error);
            var lines = listed.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            lines[0].ShouldStartWith("ID");
            KeyColumns().Split(lines[0]).ShouldBe(["ID", "USE", "ALGORITHM", "STATE", "CREATED", "ACTIVE FROM", "RETIRES"]);

            Row(lines, retired).ShouldContain(" sig ");
            Row(lines, retired).ShouldContain(" retired ");
            Row(lines, retiring).ShouldContain(" retiring ");
            Row(lines, retiring).TrimEnd().ShouldEndWith(now.AddDays(3).UtcDateTime.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture));
            Row(lines, active).ShouldContain(" active ");
            Row(lines, active).ShouldContain(" RS256 ");
            Row(lines, active).TrimEnd().ShouldEndWith(" -");
            Row(lines, upcoming).ShouldContain(" upcoming ");
            Row(lines, upcoming).ShouldContain(now.AddDays(1).UtcDateTime.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture));
            Row(lines, encrypts).ShouldContain(" enc ");
            Row(lines, encrypts).ShouldContain(" active ");
            lines.Length.ShouldBe(6);
            foreach (var key in SigningKeys(host))
            {
                listed.Output.ShouldNotContain(key.ProtectedKey);
            }

            // Listing makes no key and changes none.
            SigningKeys(host).Count.ShouldBe(5);
        });
    }

    [Fact]
    public async Task KeysListNamesTheSigningCertificateAndShowsAGeneratedKeyItReplacedAsRetiring()
    {
        await InDatabaseFileAsync(async file =>
        {
            await using (var generated = NewHost().UseSqlite(file))
            {
                await generated.SignInAsync(await generated.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token));
            }

            var certificate = WriteCertificate(Path.GetDirectoryName(file)!, "signing.pfx");
            var previous = WriteCertificate(Path.GetDirectoryName(file)!, "previous.pfx", DateTimeOffset.UtcNow.AddDays(-90), DateTimeOffset.UtcNow.AddDays(-1));
            await using var tool = WithCertificate(NewHost().UseSqlite(file), certificate.Path, previous.Path, certificate.Path, previous.Path);

            var listed = await tool.RunCommandAsync("keys", "list");

            listed.ExitCode.ShouldBe(0, listed.Error);
            var lines = listed.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            Row(lines, SigningKeys(tool).Single(key => key.Use == SigningKey.Signing)).ShouldContain(" retiring ");
            var named = lines.Single(line => line.StartsWith(certificate.Thumbprint, StringComparison.Ordinal));
            named.ShouldContain(" signs ");
            named.ShouldContain(certificate.NotBefore.UtcDateTime.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture));
            named.ShouldEndWith(certificate.NotAfter.UtcDateTime.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture));
            lines.ShouldContain(line => line.StartsWith("CERTIFICATE", StringComparison.Ordinal) && line.Contains("NOT AFTER", StringComparison.Ordinal));

            // A certificate that only validates is listed once, however often the configuration names it.
            var validates = lines.Where(line => line.StartsWith(previous.Thumbprint, StringComparison.Ordinal)).ShouldHaveSingleItem();
            validates.ShouldContain(" validates ");
            validates.ShouldEndWith(previous.NotAfter.UtcDateTime.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture));
            lines.Count(line => line.StartsWith(certificate.Thumbprint, StringComparison.Ordinal)).ShouldBe(1);
        });
    }

    [Fact]
    public async Task KeysRevokeWithoutForceChangesNothingAndSaysWhatItWouldEnd()
    {
        await InDatabaseFileAsync(async file =>
        {
            await using var host = NewHost().UseSqlite(file);
            var editor = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
            var tokens = await host.SignInAsync(editor, "openid", "offline_access", "management");
            var before = SigningKeys(host);
            var valid = await ValidTokensAsync(host);
            await using var tool = NewHost().UseSqlite(file);

            var refused = await tool.RunCommandAsync("keys", "revoke");

            refused.ExitCode.ShouldBe(1);
            refused.Output.ShouldBeEmpty();
            refused.Error.ShouldStartWith($"FCMS3391: nothing was changed. With --force this retires 2 keys (use sig and enc) at once, makes 2 that are active at once, and revokes {valid} valid tokens and 1 sessions of every user, member and client");
            SigningKeys(host).Select(key => (key.Id, key.RetiredAt)).ShouldBe(before.Select(key => (key.Id, key.RetiredAt)), ignoreOrder: true);
            (await ValidTokensAsync(host)).ShouldBe(valid);
            using var api = Bearer(tokens.AccessToken, host);
            (await WhoAmIAsync(api)).GetProperty("id").GetGuid().ShouldBe(editor.Id);
            (await ActivityActionsAsync(host)).ShouldNotContain(KeysRevokeCommand.Action);
        });
    }

    [Fact]
    public async Task KeysRevokeRetiresEveryKeyAtOnceAndEndsEveryTokenAndSessionWithOneActivityRow()
    {
        await InDatabaseFileAsync(async file =>
        {
            await using var host = NewHost().UseSqlite(file);
            var editor = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
            var other = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
            var tokens = await host.SignInAsync(editor, "openid", "offline_access", "management");
            await host.SignInAsync(other, "openid", "offline_access", "management");
            var cookie = await host.SignInCookieAsync(editor);
            var consent = await AddConsentAsync(host, editor);
            var logs = new LogCapture();
            await using var tool = NewHost().UseSqlite(file).WithServices(services => services.AddLogging(logging => logging.AddProvider(logs)));
            (await tool.RunCommandAsync("keys", "rotate")).ExitCode.ShouldBe(0);
            var old = SigningKeys(host);
            old.Count.ShouldBe(4);
            using var client = host.CreateSecureClient();
            (await KeySetAsync(client)).Count.ShouldBe(1);

            var revoked = await tool.RunCommandAsync("keys", "revoke", "--force");

            revoked.ExitCode.ShouldBe(0, revoked.Error);
            var keys = SigningKeys(host);
            var made = keys.Where(key => old.TrueForAll(before => before.Id != key.Id)).ToList();
            made.Select(key => (key.Use, key.Sequence)).ShouldBe([(SigningKey.Signing, 3), (SigningKey.Encryption, 3)], ignoreOrder: true);
            var at = made[0].ActiveFrom;
            made.ShouldAllBe(key => key.ActiveFrom == at && key.CreatedAt == at && key.RetiredAt == null);
            keys.Where(key => old.Exists(before => before.Id == key.Id)).ShouldAllBe(key => key.RetiredAt == at);
            var signs = made.Single(key => key.Use == SigningKey.Signing);
            revoked.Output.ShouldContain("Retired 4 keys (use sig and enc)");
            revoked.Output.ShouldContain($"Made the signing key {signs.Id:D}, which signs now.");
            revoked.Output.ShouldContain("Restart every instance now.");

            // Every token and every session, the consent kept.
            (await ValidTokensAsync(host)).ShouldBe(0);
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var authorizations = await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().AsNoTracking().ToListAsync(Token);
                authorizations.Where(authorization => authorization.Status == OpenIddictConstants.Statuses.Valid).ShouldHaveSingleItem().Id.ShouldBe(consent);
                authorizations.Count(authorization => authorization.Type == OpenIddictConstants.AuthorizationTypes.AdHoc).ShouldBeGreaterThanOrEqualTo(2);
            }

            // One row for the instance, by the command, and none per user.
            var actions = await ActivityActionsAsync(host);
            actions.Count(action => action == KeysRevokeCommand.Action).ShouldBe(1);
            actions.ShouldNotContain("user.sessions_ended");
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var row = await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<ActivityRow>().AsNoTracking().SingleAsync(row => row.Action == KeysRevokeCommand.Action, Token);
                row.SiteId.ShouldBeNull();
                row.Origin.ShouldBe(Origin.Cli);
                row.ActorType.ShouldBe(PrincipalKind.System);
                row.Details.ShouldNotBeNull().ShouldContain(signs.Id.ToString("D"));
                foreach (var key in keys)
                {
                    row.Details.ShouldNotContain(key.ProtectedKey);
                }
            }

            var logged = logs.Entries.Where(entry => entry.EventId.Id == OidcLog.KeysRevokedId).ShouldHaveSingleItem();
            logged.Level.ShouldBe(LogLevel.Warning);
            logged.State["Keys"].ShouldBe(4);

            // The instance that was not restarted has not read yet: it still lists the old key, and refuses the token by its entry.
            (await KeySetAsync(client)).ShouldBe([KeyId(old.Single(key => key.Use == SigningKey.Signing && key.Sequence == 1))]);
            using (var api = Bearer(tokens.AccessToken, host))
            {
                (await api.GetAsync("/api/management/v1/test/whoami", Token)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            }

            // Within 30 seconds it has only the new keys.
            host.Time.Advance(SigningKeyRing.ReadInterval);
            (await KeySetAsync(client)).ShouldBe([KeyId(signs)]);
            using (var refresh = await PostTokenAsync(client, Refresh(tokens.RefreshToken!)))
            {
                refresh.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
                (await ErrorAsync(refresh)).ShouldBe("invalid_grant");
            }

            // The sign-in cookie of the hosted pages is not under these keys, so the browser gets new tokens without a prompt.
            (await AuthorizeAsync(host, cookie)).ShouldStartWith(FluentCmsTestHost.OAuthRedirectUri + "?code=");
            Header((await host.SignInAsync(editor)).AccessToken).GetProperty("kid").GetString().ShouldBe(KeyId(signs));

            // An instance that restarts has them at once, and the job removes what was revoked without giving it more time.
            await using var restarted = NewHost().UseSqlite(file);
            using var restartedClient = restarted.CreateSecureClient();
            (await KeySetAsync(restartedClient)).ShouldBe([KeyId(signs)]);
            await RunKeysJobAsync(restarted);
            SigningKeys(restarted).Select(key => key.Id).ShouldBe(made.Select(key => key.Id), ignoreOrder: true);
        });
    }

    [Fact]
    public async Task KeysRevokeWorksOnAKeyTheKeyRingCannotRead()
    {
        await InDatabaseFileAsync(async file =>
        {
            TestUser editor;
            await using (var host = NewHost().UseSqlite(file))
            {
                editor = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
                await host.SignInAsync(editor);
                AddKey(host, SigningKey.Signing, sequence: 2, host.Time.GetUtcNow().AddMinutes(-1), protectedKey: Convert.ToBase64String(RandomNumberGenerator.GetBytes(256)));
            }

            await using (var stopped = NewHost().UseSqlite(file))
            {
                using var client = stopped.CreateSecureClient();
                (await client.GetAsync("/.well-known/jwks", Token)).StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
            }

            await using var tool = NewHost().UseSqlite(file);
            var revoked = await tool.RunCommandAsync("keys", "revoke", "--force");
            revoked.ExitCode.ShouldBe(0, revoked.Error);

            await using var restarted = NewHost().UseSqlite(file);
            var signs = SigningKeys(restarted).Single(key => key.Use == SigningKey.Signing && key.RetiredAt == null);
            signs.Sequence.ShouldBe(3);
            Header((await restarted.SignInAsync(editor)).AccessToken).GetProperty("kid").GetString().ShouldBe(KeyId(signs));
        });
    }

    [Fact]
    public async Task KeysRevokeLeavesTheSigningCertificateAndTakesOneUse()
    {
        await InDatabaseFileAsync(async file =>
        {
            var certificate = WriteCertificate(Path.GetDirectoryName(file)!, "signing.pfx");
            await using var host = WithCertificate(NewHost().UseSqlite(file), certificate.Path);
            var editor = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
            var tokens = await host.SignInAsync(editor, "openid", "offline_access", "management");
            var encryption = SigningKeys(host).ShouldHaveSingleItem();
            await using var tool = WithCertificate(NewHost().UseSqlite(file), certificate.Path);

            // The signing keys alone: there is none to retire or to make, and the tokens are revoked all the same.
            var signing = await tool.RunCommandAsync("keys", "revoke", "--use", "sig", "--force");
            signing.ExitCode.ShouldBe(0, signing.Error);
            signing.Output.ShouldContain("Retired 0 keys (use sig)");
            signing.Error.ShouldContain("FluentCms:Security:SigningCertificate signs the tokens and this command does not change it.");
            SigningKeys(host).ShouldHaveSingleItem().RetiredAt.ShouldBeNull();
            (await ValidTokensAsync(host)).ShouldBe(0);
            using (var api = Bearer(tokens.AccessToken, host))
            {
                (await api.GetAsync("/api/management/v1/test/whoami", Token)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            }

            var both = await tool.RunCommandAsync("keys", "revoke", "--force");
            both.ExitCode.ShouldBe(0, both.Error);
            var keys = SigningKeys(host);
            keys.ShouldAllBe(key => key.Use == SigningKey.Encryption);
            keys.Single(key => key.Id == encryption.Id).RetiredAt.ShouldNotBeNull();
            keys.Single(key => key.Id != encryption.Id).Sequence.ShouldBe(2);
            (await ActivityActionsAsync(host)).Count(action => action == KeysRevokeCommand.Action).ShouldBe(2);
        });
    }

    private static async Task<int> ValidTokensAsync(FluentCmsTestHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<OpenIddictEntityFrameworkCoreToken<Guid>>()
            .CountAsync(token => token.Status == OpenIddictConstants.Statuses.Valid, Token);
    }

    private static async Task<List<string>> ActivityActionsAsync(FluentCmsTestHost host)
    {
        while (await host.Services.GetRequiredService<OutboxDrainer>().DrainAsync(Token) > 0)
        {
        }

        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<ActivityRow>().AsNoTracking().Select(row => row.Action).ToListAsync(Token);
    }

    // A consent, as the consent page stores one: a permanent authorization of no session.
    private static async Task<Guid> AddConsentAsync(FluentCmsTestHost host, TestUser user)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var application = await applications.FindByClientIdAsync(TestClientId, Token);
        var authorizations = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        var consent = await authorizations.CreateAsync(
            new OpenIddictAuthorizationDescriptor
            {
                ApplicationId = await applications.GetIdAsync(application!, Token),
                Subject = user.Id.ToString("D"),
                Status = OpenIddictConstants.Statuses.Valid,
                Type = OpenIddictConstants.AuthorizationTypes.Permanent,
            },
            Token);
        return Guid.Parse((await authorizations.GetIdAsync(consent, Token))!);
    }

    [GeneratedRegex(" {2,}")]
    private static partial Regex KeyColumns();

    private static string Row(string[] lines, SigningKey key) => lines.Single(line => line.StartsWith(key.Id.ToString("D"), StringComparison.Ordinal)) + " ";

    private static FluentCmsTestHost WithCertificate(FluentCmsTestHost host, string? path, params string[] previous) =>
        host.WithServices(services => services.Configure<SecurityOptions>(security =>
        {
            security.SigningCertificate.Path = path;
            security.SigningCertificate.Password = path is null ? null : CertificatePassword;
            foreach (var validates in previous)
            {
                security.PreviousSigningCertificates.Add(new SigningCertificateOptions { Path = validates, Password = CertificatePassword });
            }
        }));

    // A self-signed certificate with its RSA key in a PKCS#12 file, valid from yesterday for a month unless told otherwise.
    private static (string Path, string Thumbprint, DateTimeOffset NotBefore, DateTimeOffset NotAfter) WriteCertificate(string directory, string name, DateTimeOffset? notBefore = null, DateTimeOffset? notAfter = null)
    {
        using var rsa = RSA.Create(2048);
        using var certificate = new CertificateRequest($"CN=FluentCMS test {name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(notBefore ?? DateTimeOffset.UtcNow.AddDays(-1), notAfter ?? DateTimeOffset.UtcNow.AddDays(30));
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pkcs12, CertificatePassword));
        return (path, certificate.Thumbprint, certificate.NotBefore.ToUniversalTime(), certificate.NotAfter.ToUniversalTime());
    }
}
