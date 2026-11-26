using System.Net;
using System.Security.Cryptography;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Core.Storage;
using FluentCMS.Testing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

    private static string KeyId(SigningKey key) => key.Id.ToString("N");

    private static async Task<List<string>> KeySetAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/.well-known/jwks", Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return [.. (await JsonAsync(response)).GetProperty("keys").EnumerateArray().Select(key => key.GetProperty("kid").GetString()!)];
    }

    // A row as SigningKeyStore writes one, in a state of the test's choosing.
    private static SigningKey AddKey(FluentCmsTestHost host, string use, int sequence, DateTimeOffset activeFrom, DateTimeOffset? retiredAt = null)
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
            ProtectedKey = Convert.ToBase64String(host.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("FluentCMS.SigningKeys").Protect(material)),
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
}
