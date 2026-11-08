using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentCMS.Media;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.KeyManagement.Internal;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Media.Tests.MediaHost;
using static FluentCMS.Plugins.Media.Tests.Transforms;

namespace FluentCMS.Plugins.Media.Tests;

public sealed class MediaSigningTests
{
    [Fact]
    public async Task OneKeyIsGeneratedStoredProtectedAndSharedByInstancesThatStartTogether()
    {
        await using var media = new MediaHost();
        var services = media.Services;
        var first = ActivatorUtilities.CreateInstance<MediaSigning>(services);
        var second = ActivatorUtilities.CreateInstance<MediaSigning>(services);

        var keys = await Task.WhenAll(first.KeyAsync(Token), second.KeyAsync(Token));
        var later = await ActivatorUtilities.CreateInstance<MediaSigning>(services).KeyAsync(Token);

        keys[0].Length.ShouldBe(MediaSigning.KeyLength);
        keys[1].ShouldBe(keys[0]);
        later.ShouldBe(keys[0]);
        await using var scope = services.CreateAsyncScope();
        var row = await scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>().Set<SigningKey>().AsNoTracking().SingleAsync(Token);
        row.Key.ShouldNotContain(Convert.ToBase64String(keys[0]));
        var unprotected = services.GetRequiredService<IDataProtectionProvider>().CreateProtector("FluentCMS.Plugins.Media.SigningKey").Unprotect(row.Key);
        Convert.FromBase64String(unprotected).ShouldBe(keys[0]);
    }

    // A key ring of its own on the disk, read again by an instance started after the ring gained a newer key, whose default
    // is then the key activated last, as it is once a new key has propagated. The host's ring makes no second key while its
    // default is new.
    [Fact]
    public async Task SignedUrlsStillWorkAfterTheDataProtectionKeyRingRotates()
    {
        await using var media = new MediaHost();
        var ringDirectory = Directory.CreateTempSubdirectory("fluentcms-media-ring-");
        try
        {
            await media.Services.GetRequiredService<MediaSigning>().KeyAsync(Token);
            await using (var scope = media.Services.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>().Set<SigningKey>().ExecuteDeleteAsync(Token);
            }

            var canonical = $"{Guid.CreateVersion7():D}?w=10";
            IKey before;
            string signature;
            await using (var ring = Ring(ringDirectory))
            {
                before = ring.GetRequiredService<IKeyManager>().CreateNewKey(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(90));
                signature = await ActivatorUtilities.CreateInstance<MediaSigning>(media.Services, ring.GetRequiredService<IDataProtectionProvider>()).SignAsync(canonical, Token);
                ring.GetRequiredService<IKeyManager>().CreateNewKey(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(90));
            }

            await using var rotated = Ring(ringDirectory);
            var protection = rotated.GetRequiredService<IDataProtectionProvider>();
            var restarted = ActivatorUtilities.CreateInstance<MediaSigning>(media.Services, protection);
            await using var check = media.Services.CreateAsyncScope();
            var row = await check.ServiceProvider.GetRequiredService<PluginDbContext<MediaPlugin>>().Set<SigningKey>().AsNoTracking().SingleAsync(Token);

            KeyIdOf(protection.CreateProtector("rotation").Protect("probe")).ShouldNotBe(before.KeyId);
            KeyIdOf(row.Key).ShouldBe(before.KeyId);
            (await restarted.VerifyAsync(canonical, signature, Token)).ShouldBeTrue();
        }
        finally
        {
            ringDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ASignedUrlIsServedAgainAfterTheKeyIsReadFromTheDatabase()
    {
        await using var media = new MediaHost();
        using var admin = await media.AdminAsync();
        using var anonymous = media.Host.CreateClient();
        var asset = await UploadAsync(admin, Bands(40, 20));
        var url = await UrlAsync(media, asset, new MediaImageOptions { Width = 10 });

        var signature = Uri.UnescapeDataString(url[(url.IndexOf("sig=", StringComparison.Ordinal) + 4)..]);
        var restarted = ActivatorUtilities.CreateInstance<MediaSigning>(media.Services);
        using var response = await FetchAsync(anonymous, url);

        (await restarted.VerifyAsync($"{asset.Id:D}?w=10", signature, Token)).ShouldBeTrue();
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AConfiguredKeyWinsAndReplacingItInvalidatesTheUrlsSignedBefore()
    {
        var configured = RandomNumberGenerator.GetBytes(MediaSigning.KeyLength);
        await using var media = new MediaHost();
        media.Host.WithServices(services => services.PostConfigure<MediaOptions>(options => options.SigningKey = Convert.ToBase64String(configured)));
        using var admin = await media.AdminAsync();
        var asset = await UploadAsync(admin, Bands(40, 20));

        var url = await UrlAsync(media, asset, new MediaImageOptions { Width = 10, Fit = ImageFit.Cover });
        var signature = Uri.UnescapeDataString(url[(url.IndexOf("sig=", StringComparison.Ordinal) + 4)..]);
        var replaced = new MediaOptions { SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(MediaSigning.KeyLength)) };
        var other = ActivatorUtilities.CreateInstance<MediaSigning>(media.Services, Options.Create(replaced));

        var expected = WebEncoders.Base64UrlEncode(HMACSHA256.HashData(configured, Encoding.UTF8.GetBytes($"{asset.Id:D}?fit=cover&w=10")));
        signature.ShouldBe(expected);
        (await other.VerifyAsync($"{asset.Id:D}?fit=cover&w=10", signature, Token)).ShouldBeFalse();
    }

    [Fact]
    public void TheCanonicalStringSortsAndEscapesTheParametersAndLeavesTheSignatureOut()
    {
        var id = Guid.Parse("0193a0c2-5f4e-7c1d-9a3b-2b8f6e1d4c70");
        var (parameters, signature) = ImageQuery.Split([new("W", "10"), new("fit", "cover"), new("sig", "abc"), new("site", "main"), new("preset", "a&b=c")]);

        signature.ShouldBe("abc");
        ImageQuery.Canonical(id, parameters).ShouldBe("0193a0c2-5f4e-7c1d-9a3b-2b8f6e1d4c70?fit=cover&preset=a%26b%3Dc&w=10");
        ImageQuery.Canonical(id, [.. parameters.Reverse()]).ShouldBe(ImageQuery.Canonical(id, parameters));
    }

    [Theory]
    [InlineData(null, "public, max-age=86400", true)]
    [InlineData("AAAA", "public, max-age=86400", false)]
    [InlineData("not base64!", "public, max-age=86400", false)]
    [InlineData("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=", "public, max-age=86400", true)]
    [InlineData(null, "", false)]
    public void TheOptionsRefuseAShortKeyAndAnEmptyCacheControl(string? key, string cacheControl, bool valid) =>
        new MediaOptions { SigningKey = key, CacheControl = cacheControl }.IsValid().ShouldBe(valid);

    private static ServiceProvider Ring(DirectoryInfo directory)
    {
        var services = new ServiceCollection().AddDataProtection().PersistKeysToFileSystem(directory).Services;
        services.AddSingleton<IDefaultKeyResolver, NewestKeyResolver>();
        return services.BuildServiceProvider();
    }

    private sealed class NewestKeyResolver : IDefaultKeyResolver
    {
        public DefaultKeyResolution ResolveDefaultKeyPolicy(DateTimeOffset now, IEnumerable<IKey> allKeys) => new()
        {
            DefaultKey = allKeys.Where(key => !key.IsRevoked && key.ActivationDate <= now && key.ExpirationDate > now).MaxBy(key => key.ActivationDate),
        };
    }

    // A protected payload starts with a four-byte magic header, then the id of the key that protected it.
    private static Guid KeyIdOf(string protectedValue) => new(WebEncoders.Base64UrlDecode(protectedValue).AsSpan(4, 16));
}
