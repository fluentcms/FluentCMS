using System.Text.Json.Nodes;
using FluentCMS.Core.Commands;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Core.Settings;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Migrations;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Plugins;
using FluentCMS.Plugins.Media;
using FluentCMS.Plugins.Webhooks;
using FluentCMS.Security;
using FluentCMS.Settings;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.KeyManagement.Internal;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;
using MediaKey = FluentCMS.Plugins.Media.SigningKey;
using TokenKey = FluentCMS.Core.Identity.Oidc.SigningKey;

namespace FluentCMS.Data.Tests.Settings;

// `fluentcms keyring reprotect` (#3584) on the provider of the run, in a host with the webhooks and media plugins whose key
// ring is a folder the test writes with a ring of its own, so that it can make an older key, protect under it, and take it
// away again. The default key is the one activated last, as it is once a new key has propagated.
public sealed class KeyringReprotectionTests : IDisposable
{
    private const string Secret = "whsec_keyring-reprotect-7731";
    private const string Previous = "whsec_keyring-previous-7731";
    private const string Header = "Bearer keyring-header-7731";
    private const string ApiKey = "sk-keyring-settings-7731";

    private static readonly SettingsDescriptor Vault = new("vault", typeof(VaultSettings));
    private static readonly EphemeralDataProtectionProvider Foreign = new();
    private static readonly byte[] MediaBytes = [.. Enumerable.Range(1, MediaSigning.KeyLength).Select(value => (byte)value)];

    private readonly TestDatabase _database = TestDatabase.Create();
    private readonly DirectoryInfo _ring = Directory.CreateTempSubdirectory("fluentcms-keyring-");
    private readonly DirectoryInfo _media = Directory.CreateTempSubdirectory("fluentcms-keyring-media-");
    private IHost? _host;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _host?.Dispose();
        _database.Dispose();
        _ring.Delete(recursive: true);
        _media.Delete(recursive: true);
    }

    [Fact]
    public async Task AfterThePassEveryKindOfValueUnprotectsWithTheCurrentDefaultKeyAlone()
    {
        IKey old;
        IKey current;
        Stored stored;
        await using (var ring = Ring())
        {
            old = ring.GetRequiredService<IKeyManager>().CreateNewKey(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(90));
            stored = Protect(ring.GetRequiredService<IDataProtectionProvider>());
            current = ring.GetRequiredService<IKeyManager>().CreateNewKey(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(90));
        }

        var webhook = await StartAsync(stored);

        var (exitCode, output, error) = await CommandAsync("keyring", "reprotect");

        (exitCode, error).ShouldBe((CommandExitCodes.Success, ""));
        output.ShouldContain($"Protected the secrets of 1 stored settings documents and 4 other stored values again under the default key {current.KeyId:D} of the key ring.");
        var after = await ReadAsync(webhook);
        foreach (var value in (string[])[after.Secret, after.PreviousSecret!, after.Headers, after.MediaKey!])
        {
            KeyIdOf(value).ShouldBe(current.KeyId);
        }

        KeyIdOf(JsonNode.Parse(after.Settings!)!["apiKey"]!.GetValue<string>()[SettingsProtection.Prefix.Length..]).ShouldBe(current.KeyId);

        // The older key is gone from the ring; what the pass wrote is read with the current one alone.
        File.Delete(Path.Combine(_ring.FullName, $"key-{old.KeyId:D}.xml"));
        await using var retired = Ring();
        var only = retired.GetRequiredService<IDataProtectionProvider>();
        retired.GetRequiredService<IKeyManager>().GetAllKeys().ShouldHaveSingleItem().KeyId.ShouldBe(current.KeyId);
        var signing = new WebhookSigning(only);
        (signing.Unprotect(after.Secret), signing.Unprotect(after.PreviousSecret!)).ShouldBe((Secret, Previous));
        signing.UnprotectHeaders(after.Headers).ShouldBe(new Dictionary<string, string> { ["Authorization"] = Header });
        Convert.FromBase64String(only.CreateProtector(MediaSigning.Purpose).Unprotect(after.MediaKey!)).ShouldBe(MediaBytes);
        var settings = JsonNode.Parse(new SettingsProtection(only, NullLogger<SettingsProtection>.Instance).Unprotect(Vault, after.Settings!, null))!;
        settings["apiKey"]!.GetValue<string>().ShouldBe(ApiKey);
    }

    [Fact]
    public async Task AValueAlreadyUnderTheDefaultKeyIsNotWritten()
    {
        await using (var ring = Ring())
        {
            ring.GetRequiredService<IKeyManager>().CreateNewKey(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(90));
        }

        await using var same = Ring();
        var webhook = await StartAsync(Protect(same.GetRequiredService<IDataProtectionProvider>()));
        var before = await ReadAsync(webhook);

        var (exitCode, output, error) = await CommandAsync("keyring", "reprotect");

        (exitCode, error).ShouldBe((CommandExitCodes.Success, ""));
        output.ShouldContain("Protected the secrets of 0 stored settings documents and 0 other stored values again under the default key ");
        (await ReadAsync(webhook)).ShouldBe(before);
    }

    [Fact]
    public async Task AValueTheKeyRingCannotUnprotectIsLeftAsItIsAndNamedByTableAndIdWithoutItsContent()
    {
        var foreign = Protect(Foreign);
        var webhook = await StartAsync(foreign with { Settings = null });
        var before = await ReadAsync(webhook);

        var (exitCode, output, error) = await CommandAsync("keyring", "reprotect");

        exitCode.ShouldBe(CommandExitCodes.Failure);
        output.ShouldContain("Protected the secrets of 0 stored settings documents and 0 other stored values again under the default key ");
        error.ShouldContain($"The value 'secret' of {WebhookModel.WebhookTable} row {webhook:D} is left as it is: the key ring cannot unprotect it.");
        error.ShouldContain($"The value 'previous_secret' of {WebhookModel.WebhookTable} row {webhook:D} is left as it is");
        error.ShouldContain($"The value 'headers' of {WebhookModel.WebhookTable} row {webhook:D} is left as it is");
        error.ShouldContain($"The value 'key' of {MediaModel.SigningKeyTable} row {MediaSigning.KeyId} is left as it is: the key ring cannot unprotect it.");
        error.ShouldContain($"{CommandErrorCodes.SecretsNotReprotected}: 4 other stored values still need the key that protected them; 4 other stored values are under a key the key ring cannot unprotect: restore the key or certificate that protected them, or set those values again; then run 'fluentcms keyring reprotect' again before retiring a key");
        foreach (var text in new[] { output, error })
        {
            foreach (var value in (string[])[Secret, Previous, Header, foreign.Secret!, foreign.PreviousSecret!, foreign.Headers!, foreign.MediaKey!])
            {
                text.ShouldNotContain(value);
            }
        }

        (await ReadAsync(webhook)).ShouldBe(before);
    }

    // Another writer replaces the secret while the pass protects what it read, so the pass's write misses the value it read.
    [Fact]
    public async Task AValueThatChangedSinceItWasReadIsNotOverwritten()
    {
        var webhook = await StartAsync(new Stored(Secret: Secret, PreviousSecret: null, Headers: """{"X-Plain":"yes"}""", MediaKey: null, Settings: null, TokenKeys: []));
        var own = new WebhookSigning(_host!.Services.GetRequiredService<IDataProtectionProvider>());
        await WriteWebhookAsync(webhook, own.Protect(Secret));
        var interrupting = new InterruptingProvider(_host.Services.GetRequiredService<IDataProtectionProvider>(), () => WriteWebhookAsync(webhook, own.Protect(Secret + "-newer")).GetAwaiter().GetResult());

        ProtectedValuesReprotected result;
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            result = await new ProtectedValueReprotection(interrupting, services.GetServices<IProtectedValueSource>(), services.GetRequiredService<SystemDbContext>(), TimeProvider.System).RunAsync(Token);
        }

        (result.Values, result.Left.Count).ShouldBe((1, 0));
        var after = await ReadAsync(webhook);
        own.Unprotect(after.Secret).ShouldBe(Secret + "-newer");
        after.Headers.ShouldBe("""{"X-Plain":"yes"}""", "plain headers are under no key and not the pass's to protect");
    }

    [Fact]
    public async Task ASecondRunChangesNothingAndEndsAsTheFirstDid()
    {
        Stored stored;
        await using (var ring = Ring())
        {
            ring.GetRequiredService<IKeyManager>().CreateNewKey(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(90));
            stored = Protect(ring.GetRequiredService<IDataProtectionProvider>()) with { MediaKey = Foreign.CreateProtector(MediaSigning.Purpose).Protect(Convert.ToBase64String(MediaBytes)) };
            ring.GetRequiredService<IKeyManager>().CreateNewKey(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(90));
        }

        var webhook = await StartAsync(stored);

        var first = await CommandAsync("keyring", "reprotect");
        var after = await ReadAsync(webhook);
        var second = await CommandAsync("keyring", "reprotect");

        (await ReadAsync(webhook)).ShouldBe(after);
        (second.ExitCode, second.Error).ShouldBe((first.ExitCode, first.Error));
        first.ExitCode.ShouldBe(CommandExitCodes.Failure);
        first.Output.ShouldContain("Protected the secrets of 1 stored settings documents and 3 other stored values again");
        second.Output.ShouldContain("Protected the secrets of 0 stored settings documents and 0 other stored values again");
        after.MediaKey.ShouldBe(stored.MediaKey);
    }

    // ADR 0008 leaves cms_signing_key to rotation, so a key under an older ring key is only counted, and the previous
    // certificate is still needed for it: the command fails until the rotation is through. A retired key needs nothing.
    [Fact]
    public async Task TheTokenKeysUnderAnOlderKeyAreCountedAndNotRewrittenAndTheCommandFails()
    {
        IKey current;
        Stored stored;
        await using (var ring = Ring())
        {
            ring.GetRequiredService<IKeyManager>().CreateNewKey(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(90));
            var older = ring.GetRequiredService<IDataProtectionProvider>().CreateProtector(SigningKeyStore.Purpose);
            stored = new Stored(Secret: null, PreviousSecret: null, Headers: null, MediaKey: null, Settings: null, TokenKeys:
            [
                ProtectedTokenRow(TokenKey.Signing, 1, older, retiredAt: null),
                ProtectedTokenRow(TokenKey.Encryption, 1, older, retiredAt: DateTimeOffset.UtcNow.AddDays(1)),
                ProtectedTokenRow(TokenKey.Signing, 0, older, retiredAt: DateTimeOffset.UtcNow.AddDays(-1)),
                ProtectedTokenRow(TokenKey.Encryption, 0, Foreign.CreateProtector(SigningKeyStore.Purpose), retiredAt: null),
            ]);
            current = ring.GetRequiredService<IKeyManager>().CreateNewKey(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(90));
        }

        await StartAsync(stored);
        var before = await TokenKeysAsync();

        var (exitCode, output, error) = await CommandAsync("keyring", "reprotect");

        exitCode.ShouldBe(CommandExitCodes.Failure);
        output.ShouldContain($"Protected the secrets of 0 stored settings documents and 0 other stored values again under the default key {current.KeyId:D} of the key ring.");
        error.ShouldContain($"2 accepted keys of {StorageNames.SigningKey} are under an older key of the key ring; they are not protected again in place but replaced by rotation.");
        error.ShouldContain($"1 accepted keys of {StorageNames.SigningKey} are under a key the key ring cannot unprotect.");
        error.ShouldContain($"{CommandErrorCodes.SecretsNotReprotected}: 3 token keys still need the key that protected them; 2 token keys leave the older key by rotation: run 'fluentcms keys rotate' and keep the older key until 'fluentcms keys list' shows no key made before that run; 1 token keys are under a key the key ring cannot unprotect: restore the key or certificate that protected them, or run 'fluentcms keys revoke --force'");
        (await TokenKeysAsync()).ShouldBe(before);
        foreach (var key in before)
        {
            output.ShouldNotContain(key);
            error.ShouldNotContain(key);
        }
    }

    // The sibling command takes nothing of settings reprotect: in a host with the plugins it still covers the settings alone.
    [Fact]
    public async Task SettingsReprotectLeavesThePluginValuesAsTheyAre()
    {
        var webhook = await StartAsync(Protect(Foreign) with { Settings = $$"""{"apiKey":"{{ApiKey}}"}""" });
        var before = await ReadAsync(webhook);

        var (exitCode, output, error) = await CommandAsync("settings", "reprotect");

        (exitCode, error).ShouldBe((CommandExitCodes.Success, ""));
        output.ShouldStartWith("Protected the secrets of 1 stored settings documents again under the default key ");
        var after = await ReadAsync(webhook);
        (after with { Settings = before.Settings, SettingsVersion = before.SettingsVersion }).ShouldBe(before);
        after.Settings!.ShouldNotContain(ApiKey);
    }

    private static TokenRow ProtectedTokenRow(string use, int sequence, IDataProtector protector, DateTimeOffset? retiredAt) =>
        new(use, sequence, Convert.ToBase64String(protector.Protect(Enumerable.Range(0, 32).Select(value => (byte)(value + sequence)).ToArray())), retiredAt);

    private static Stored Protect(IDataProtectionProvider provider)
    {
        var signing = new WebhookSigning(provider);
        return new Stored(
            signing.Protect(Secret),
            signing.Protect(Previous),
            signing.ProtectHeaders(new Dictionary<string, string> { ["Authorization"] = Header }),
            provider.CreateProtector(MediaSigning.Purpose).Protect(Convert.ToBase64String(MediaBytes)),
            new SettingsProtection(provider, NullLogger<SettingsProtection>.Instance).Protect(Vault, $$"""{"apiKey":"{{ApiKey}}"}""", null),
            []);
    }

    private static Guid KeyIdOf(string stored) => new(WebEncoders.Base64UrlDecode(stored).AsSpan(4, 16));

    // A ring of the test's own on the host's folder, under the host's application name, so that it protects as the host does.
    private ServiceProvider Ring()
    {
        var services = new ServiceCollection().AddDataProtection().SetApplicationName(AuthorizationServerRegistration.ApplicationName).PersistKeysToFileSystem(_ring).Services;
        services.AddSingleton<IDefaultKeyResolver, NewestKeyResolver>();
        return services.BuildServiceProvider();
    }

    // Builds and migrates the host, which reads the ring only now, and stores the values given; the id of the webhook.
    private async Task<Guid> StartAsync(Stored stored)
    {
        _host = _database.CreateHost(
            ArticleSchemas.NoTypes,
            "Production",
            settings: new Dictionary<string, string?> { ["FluentCms:Media:Local:Root"] = _media.FullName },
            services: services =>
            {
                services.AddDataProtection().PersistKeysToFileSystem(_ring);
                services.AddSingleton<IDefaultKeyResolver, NewestKeyResolver>();
            },
            configure: cms =>
            {
                cms.Settings.Add<VaultSettings>();
                cms.AddPlugin<WebhooksPlugin>();
                cms.AddPlugin<MediaPlugin>();
            });
        await _host.Services.GetRequiredService<MigrationRunner>().MigrateAsync(Token);

        var now = DateTimeOffset.UtcNow;
        var id = Guid.CreateVersion7();
        await using var scope = _host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        if (stored.Secret is { } secret)
        {
            var webhooks = services.GetRequiredService<PluginDbContext<WebhooksPlugin>>();
            webhooks.Add(new Webhook
            {
                Id = id, SiteId = DefaultSite.Id, Name = "builds", Url = "https://example.com/hook", Secret = secret, PreviousSecret = stored.PreviousSecret,
                PreviousSecretExpiresAt = stored.PreviousSecret is null ? null : now.AddDays(1), Headers = stored.Headers ?? "{}", Enabled = true,
                CreatedAt = now, UpdatedAt = now,
            });
            await webhooks.SaveChangesAsync(Token);
        }

        if (stored.MediaKey is { } key)
        {
            var media = services.GetRequiredService<PluginDbContext<MediaPlugin>>();
            media.Add(new MediaKey { Id = MediaSigning.KeyId, Key = key, CreatedAt = now });
            await media.SaveChangesAsync(Token);
        }

        var system = services.GetRequiredService<SystemDbContext>();
        if (stored.Settings is { } json)
        {
            system.Add(new SettingRow { Id = Guid.CreateVersion7(), Key = Vault.Area, Json = json, Version = 1, UpdatedAt = now });
        }

        system.AddRange(stored.TokenKeys.Select(row => new TokenKey
        {
            Id = Guid.NewGuid(), Use = row.Use, Sequence = row.Sequence + 1, Algorithm = row.Use == TokenKey.Signing ? "RS256" : "A256KW",
            ProtectedKey = row.ProtectedKey, CreatedAt = now.AddDays(-2), ActiveFrom = now.AddDays(-2), RetiredAt = row.RetiredAt,
        }));
        await system.SaveChangesAsync(Token);
        return id;
    }

    private async Task WriteWebhookAsync(Guid id, string secret)
    {
        await using var scope = _host!.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PluginDbContext<WebhooksPlugin>>().Set<Webhook>()
            .Where(webhook => webhook.Id == id)
            .ExecuteUpdateAsync(set => set.SetProperty(webhook => webhook.Secret, secret), Token);
    }

    private async Task<Read> ReadAsync(Guid webhook)
    {
        await using var scope = _host!.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var row = await services.GetRequiredService<PluginDbContext<WebhooksPlugin>>().Set<Webhook>().AsNoTracking().SingleAsync(item => item.Id == webhook, Token);
        var media = await services.GetRequiredService<PluginDbContext<MediaPlugin>>().Set<MediaKey>().AsNoTracking().Where(item => item.Id == MediaSigning.KeyId).Select(item => item.Key).SingleOrDefaultAsync(Token);
        var settings = await services.GetRequiredService<SystemDbContext>().Set<SettingRow>().AsNoTracking().Where(item => item.Key == Vault.Area).Select(item => item.Json + "|" + item.Version).SingleOrDefaultAsync(Token);
        return new Read(row.Secret, row.PreviousSecret, row.Headers, media, settings?.Split('|')[0], settings?.Split('|')[1]);
    }

    private async Task<List<string>> TokenKeysAsync()
    {
        await using var scope = _host!.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<TokenKey>().AsNoTracking()
            .OrderBy(key => key.Use).ThenBy(key => key.Sequence)
            .Select(key => key.ProtectedKey)
            .ToListAsync(Token);
    }

    private async Task<(int ExitCode, string Output, string Error)> CommandAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await CommandRunner.RunAsync(_host!.Services, [CommandRunner.Prefix, .. args], output, error, Token);
        return (exitCode!.Value, output.ToString(), error.ToString());
    }

    private sealed record Stored(string? Secret, string? PreviousSecret, string? Headers, string? MediaKey, string? Settings, IReadOnlyList<TokenRow> TokenKeys);

    private sealed record TokenRow(string Use, int Sequence, string ProtectedKey, DateTimeOffset? RetiredAt);

    private sealed record Read(string Secret, string? PreviousSecret, string Headers, string? MediaKey, string? Settings, string? SettingsVersion);

    [Settings("vault")]
    private sealed class VaultSettings
    {
        [SettingsSecret]
        public string? ApiKey { get; set; }
    }

    // Runs the action once, the first time it protects a value, between the pass's read of a value and its write.
    private sealed class InterruptingProvider(IDataProtectionProvider inner, Action action) : IDataProtectionProvider
    {
        private Action? _pending = action;

        public IDataProtector CreateProtector(string purpose) => new Protector(inner.CreateProtector(purpose), this);

        private void Fire() => Interlocked.Exchange(ref _pending, null)?.Invoke();

        private sealed class Protector(IDataProtector inner, InterruptingProvider owner) : IDataProtector
        {
            public IDataProtector CreateProtector(string purpose) => new Protector(inner.CreateProtector(purpose), owner);

            public byte[] Protect(byte[] plaintext)
            {
                owner.Fire();
                return inner.Protect(plaintext);
            }

            public byte[] Unprotect(byte[] protectedData) => inner.Unprotect(protectedData);
        }
    }

    private sealed class NewestKeyResolver : IDefaultKeyResolver
    {
        public DefaultKeyResolution ResolveDefaultKeyPolicy(DateTimeOffset now, IEnumerable<IKey> allKeys) => new()
        {
            DefaultKey = allKeys.Where(key => !key.IsRevoked && key.ActivationDate <= now && key.ExpirationDate > now).MaxBy(key => key.ActivationDate),
        };
    }
}
