using System.Text.Json.Nodes;
using FluentCMS.Core.Commands;
using FluentCMS.Core.Settings;
using FluentCMS.Core.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.KeyManagement.Internal;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Settings.SettingsDatabase;

namespace FluentCMS.Data.Tests.Settings;

public sealed class SettingsReprotectionTests
{
    private const string Secret = "sk-reprotect-5519-value";

    private static readonly SettingsDescriptor Media = new("media", typeof(MediaSettings));

    // The ring of the test, on the disk, whose default is the key activated last, as it is once a new key has propagated.
    [Fact]
    public async Task AfterThePassEveryProtectedSecretUnprotectsWithTheCurrentDefaultKeyOnly()
    {
        using var database = new SettingsDatabase();
        var directory = Directory.CreateTempSubdirectory("fluentcms-settings-ring-");
        try
        {
            IKey old;
            IKey current;
            await using (var ring = Ring(directory))
            {
                old = ring.GetRequiredService<IKeyManager>().CreateNewKey(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(90));
                var protection = Protection(ring);
                database.AddRow(SiteA.Value, "media", protection.Protect(Media, $$"""{"provider":"s3","apiKey":"{{Secret}}","storageAccount":null}""", SiteA.Value));
                database.AddRow(null, "media", protection.Protect(Media, $$"""{"apiKey":"{{Secret}}-default","passphrase":"pass"}""", null));
                current = ring.GetRequiredService<IKeyManager>().CreateNewKey(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(90));
            }

            database.AddRow(SiteB.Value, "media", $$"""{"apiKey":"{{Secret}}-plaintext"}""");
            database.AddRow(null, "seo", """{"title":"no secrets"}""");
            var seo = Snapshot(database).Single(row => row.Key == "seo");

            SecretSettingsReprotected result;
            await using (var ring = Ring(directory))
            {
                result = await RunAsync(database, Protection(ring));
                KeyIdOf(Stored(database, SiteA.Value, "apiKey")).ShouldBe(current.KeyId);
                ring.GetRequiredService<IKeyManager>().RevokeKey(old.KeyId);
            }

            (result.Documents, result.Left.Count).ShouldBe((3, 0));
            Snapshot(database).Single(row => row.Key == "seo").ShouldBe(seo, "a document without secrets is not written");
            database.Rows().Where(row => row.Key == "media").ShouldAllBe(row => row.Version == 2 && !row.Json.Contains(Secret));
            JsonNode.Parse(database.Rows().Single(row => row.SiteId == SiteA.Value).Json)!["storageAccount"].ShouldBeNull();

            await using var retired = Ring(directory);
            var only = Protection(retired);
            foreach (var row in database.Rows().Where(row => row.Key == "media"))
            {
                only.Inspect(Media, row.Json, row.SiteId).ShouldBe(StoredSecrets.Protected);
                foreach (var key in Media.SecretKeys.Where(key => JsonNode.Parse(row.Json)![key] is not null))
                {
                    KeyIdOf(Stored(database, row.SiteId, key)).ShouldBe(current.KeyId);
                }
            }

            JsonNode.Parse(only.Unprotect(Media, database.Rows().Single(row => row.SiteId == SiteA.Value).Json, SiteA.Value))!["apiKey"]!.GetValue<string>().ShouldBe(Secret);
            var instance = JsonNode.Parse(only.Unprotect(Media, database.Rows().Single(row => row.Key == "media" && row.SiteId == null).Json, null))!;
            (instance["apiKey"]!.GetValue<string>(), instance["passphrase"]!.GetValue<string>()).ShouldBe((Secret + "-default", "pass"));
            JsonNode.Parse(only.Unprotect(Media, database.Rows().Single(row => row.SiteId == SiteB.Value).Json, SiteB.Value))!["apiKey"]!.GetValue<string>().ShouldBe(Secret + "-plaintext");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ARowTheKeyRingCannotUnprotectIsLeftAsItIsAndReportedByAreaAndScopeWithoutItsValue()
    {
        using var database = new SettingsDatabase();
        var foreign = new SettingsProtection(new EphemeralDataProtectionProvider(), NullLogger<SettingsProtection>.Instance);
        database.AddRow(SiteA.Value, "media", foreign.Protect(Media, $$"""{"provider":"s3","apiKey":"{{Secret}}","passphrase":"pass"}""", SiteA.Value));
        database.AddRow(null, "gone", foreign.Protect(new SettingsDescriptor("gone", typeof(MediaSettings)), $$"""{"apiKey":"{{Secret}}-gone"}""", null));
        await database.SetDefaultAsync(new MediaSettings { ApiKey = Secret + "-readable" });
        var before = Snapshot(database).Where(row => row.Key != "media" || row.SiteId is not null).ToList();
        var payload = Stored(database, SiteA.Value, "apiKey");

        var (exitCode, output, error) = await CommandAsync(database, "settings", "reprotect");

        exitCode.ShouldBe(CommandExitCodes.Failure);
        output.ShouldContain("Protected the secrets of 1 stored settings documents again under the current key.");
        error.ShouldContain($"The settings area 'media' for site {SiteA.Value:D} is left as it is: the key ring cannot unprotect apiKey, passphrase.");
        error.ShouldContain("The settings area 'gone' for the instance is left as it is: no settings class of this host has that area");
        error.ShouldContain($"{CommandErrorCodes.SecretsNotReprotected}: 2 stored settings documents were left as they are");
        foreach (var text in new[] { output, error, database.Logs.All })
        {
            text.ShouldNotContain(Secret);
            text.ShouldNotContain(SettingsProtection.Prefix);
            text.ShouldNotContain(payload);
        }

        Snapshot(database).Where(row => row.Key != "media" || row.SiteId is not null).ToList().ShouldBe(before);
        database.Rows().Single(row => row.Key == "media" && row.SiteId == null).Version.ShouldBe(2);
        (await database.GetDefaultAsync()).ApiKey.ShouldBe(Secret + "-readable");
    }

    [Fact]
    public async Task APassWithNothingLeftEndsWithExitCodeZero()
    {
        using var database = new SettingsDatabase();
        await database.SetAsync(SiteA, new MediaSettings { ApiKey = Secret });

        var (exitCode, output, error) = await CommandAsync(database, "settings", "reprotect");

        (exitCode, error).ShouldBe((CommandExitCodes.Success, ""));
        output.ShouldContain("Protected the secrets of 1 stored settings documents again under the current key.");
        (await database.GetAsync(SiteA)).ApiKey.ShouldBe(Secret);
    }

    private static Task<SecretSettingsReprotected> RunAsync(SettingsDatabase database, SettingsProtection protection) =>
        database.AsAsync(SettingsDatabase.Admin, (IServiceProvider services) =>
            new SecretSettingsReprotection(services.GetRequiredService<SystemDbContext>(), services.GetRequiredService<SettingsRegistry>(), protection).RunAsync(Token));

    private static async Task<(int ExitCode, string Output, string Error)> CommandAsync(SettingsDatabase database, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await CommandRunner.RunAsync(database.Inner.Database.Services, [CommandRunner.Prefix, .. args], output, error, Token);
        return (exitCode!.Value, output.ToString(), error.ToString());
    }

    private static List<(string Key, Guid? SiteId, string Json, int Version)> Snapshot(SettingsDatabase database) =>
        [.. database.Rows().Select(row => (row.Key, row.SiteId, row.Json, row.Version))];

    private static SettingsProtection Protection(ServiceProvider ring) =>
        new(ring.GetRequiredService<IDataProtectionProvider>(), NullLogger<SettingsProtection>.Instance);

    private static string Stored(SettingsDatabase database, Guid? site, string key) =>
        JsonNode.Parse(database.Rows().Single(row => row.Key == "media" && row.SiteId == site).Json)![key]!.GetValue<string>();

    private static Guid KeyIdOf(string stored) => new(WebEncoders.Base64UrlDecode(stored[SettingsProtection.Prefix.Length..]).AsSpan(4, 16));

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
}
