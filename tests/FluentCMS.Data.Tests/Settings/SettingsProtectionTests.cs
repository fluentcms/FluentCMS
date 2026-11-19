using System.Text.Json.Nodes;
using FluentCMS.Core.Settings;
using FluentCMS.Diagnostics;
using FluentCMS.Settings;
using FluentCMS.Sites;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Settings.SettingsDatabase;

namespace FluentCMS.Data.Tests.Settings;

public sealed class SettingsProtectionTests
{
    private const string Secret = "sk-at-rest-7731-value";

    private static readonly FluentCMS.Content.Principal Admin = SettingsDatabase.Admin;

    [Fact]
    public async Task ASecretIsProtectedInItsRowAndReadBackByTheClassOnly()
    {
        using var database = new SettingsDatabase();
        await database.SetAsync(SiteA, new MediaSettings { Provider = "s3", ApiKey = Secret, StorageAccount = Secret + "-account", Passphrase = "pass" });
        await database.SetDefaultAsync(new MediaSettings { ApiKey = Secret + "-default" });

        foreach (var row in database.Rows())
        {
            row.Json.ShouldNotContain(Secret);
            JsonNode.Parse(row.Json)!["apiKey"]!.GetValue<string>().ShouldStartWith(SettingsProtection.Prefix);
        }

        var site = JsonNode.Parse(database.Rows().Single(row => row.SiteId == SiteA.Value).Json)!;
        site["provider"]!.GetValue<string>().ShouldBe("s3");
        site["storageAccount"]!.GetValue<string>().ShouldStartWith(SettingsProtection.Prefix);
        site["passphrase"]!.GetValue<string>().ShouldStartWith(SettingsProtection.Prefix);

        var read = await database.GetAsync(SiteA);
        (read.ApiKey, read.StorageAccount, read.Passphrase).ShouldBe((Secret, Secret + "-account", "pass"));
        (await database.GetAsync(SiteB)).ApiKey.ShouldBe(Secret + "-default");

        var redacted = await database.ServiceAsync((service, descriptor) => service.ResolveRedactedAsync(descriptor, SiteA, Token));
        redacted.Json.ShouldNotContain(Secret);
        redacted.Json.ShouldNotContain(SettingsProtection.Prefix);
        redacted.SecretsSet.ShouldBe(["apiKey", "passphrase", "storageAccount"], ignoreOrder: true);

        var export = await database.ServiceAsync((service, _) => service.SiteDocumentsAsync(SiteA, Token));
        export.ShouldAllBe(document => !document.Json.Contains(Secret) && !document.Json.Contains(SettingsProtection.Prefix));
        (await database.TracesAsync()).ShouldNotContain(Secret);
    }

    [Fact]
    public async Task ASecretSetToNullStaysNullInTheRow()
    {
        using var database = new SettingsDatabase();
        await database.SetAsync(SiteA, new MediaSettings { ApiKey = null, StorageAccount = null });

        var row = JsonNode.Parse(database.Rows().ShouldHaveSingleItem().Json)!.AsObject();
        row["apiKey"].ShouldBeNull();
        row["storageAccount"].ShouldBeNull();
        (await database.GetAsync(SiteA)).StorageAccount.ShouldBeNull();
    }

    [Fact]
    public async Task AProtectedValueMovedToAnotherKeyOrAreaIsNotRead()
    {
        using var database = new SettingsDatabase(cms => cms.Settings.Add<NestedSettings>());
        await database.SetAsync(SiteA, new MediaSettings { ApiKey = Secret });

        var stored = JsonNode.Parse(database.Rows().ShouldHaveSingleItem().Json)!.AsObject();
        var protectedKey = stored["apiKey"]!.GetValue<string>();
        stored["storageAccount"] = protectedKey;
        database.EditRow(SiteA.Value, "media", stored.ToJsonString());
        database.AddRow(SiteA.Value, "nested", new JsonObject { ["token"] = protectedKey }.ToJsonString());

        var read = await database.GetAsync(SiteA);
        read.ApiKey.ShouldBe(Secret);
        read.StorageAccount.ShouldBe("default-account-8841");

        var nested = await database.AsAsync(Admin, async (IServiceProvider services) => await services.GetRequiredService<ISettings<NestedSettings>>().GetAsync(SiteA, Token));
        nested.Token.ShouldBe("root-token-default-31");

        database.Logs.All.ShouldContain("The stored value of the secret key storageAccount of area 'media'");
        database.Logs.All.ShouldContain("The stored value of the secret key token of area 'nested'");
        database.Logs.All.ShouldNotContain(Secret);
    }

    [Fact]
    public async Task APlaintextSecretOfAnOlderRowIsReadReportedAndProtectedByTheNextWrite()
    {
        using var database = new SettingsDatabase();
        database.AddRow(SiteA.Value, "media", $$"""{"provider":"s3","apiKey":"{{Secret}}"}""");
        database.AddRow(null, "seo", """{"title":"no secrets here"}""");

        (await database.GetAsync(SiteA)).ApiKey.ShouldBe(Secret);
        var found = (await ReportAsync(database)).ShouldHaveSingleItem();
        found.Id.ShouldBe(SecretSettingsReport.Unprotected);
        found.Message.ShouldContain("1 stored settings documents");
        found.Message.ShouldContain("areas media.");
        found.Message.ShouldNotContain(Secret);

        // The form of the admin sends the document back without its secrets, which keep what is stored.
        await WriteAsync(database, SiteA, """{"maxUploadMb":30}""");

        database.Rows().ShouldAllBe(row => !row.Json.Contains(Secret));
        var read = await database.GetAsync(SiteA);
        (read.ApiKey, read.MaxUploadMb).ShouldBe((Secret, 30));
        (await ReportAsync(database)).ShouldBeEmpty();
    }

    [Fact]
    public async Task AValueThatLooksProtectedIsProtectedLikeAnyOtherAndReadBackAsSent()
    {
        using var database = new SettingsDatabase();
        var forged = SettingsProtection.Prefix + "not-a-payload";
        await database.SetAsync(SiteA, new MediaSettings { ApiKey = forged });

        JsonNode.Parse(database.Rows().ShouldHaveSingleItem().Json)!["apiKey"]!.GetValue<string>().ShouldNotBe(forged);
        (await database.GetAsync(SiteA)).ApiKey.ShouldBe(forged);
        database.Logs.All.ShouldNotContain("cannot be unprotected");
    }

    [Fact]
    public async Task ASiteSecretAnotherKeyRingProtectedReadsAsItsCodeDefaultAndOutlivesAWriteThatLeavesItOut()
    {
        using var database = new SettingsDatabase();
        await database.SetDefaultAsync(new MediaSettings { ApiKey = "instance-key" });
        database.AddRow(SiteA.Value, "media", Foreign($$"""{"provider":"s3","apiKey":"{{Secret}}"}""", SiteA.Value));
        var payload = StoredApiKey(database, SiteA.Value);

        var read = await database.GetAsync(SiteA);
        (read.ApiKey, read.Provider).ShouldBe((null, "s3"));
        var redacted = await database.ServiceAsync((service, descriptor) => service.ResolveRedactedAsync(descriptor, SiteA, Token));
        redacted.SecretsInherited.ShouldNotContain("apiKey");
        database.Logs.All.ShouldContain($"The stored value of the secret key apiKey of area 'media' for site {SiteA.Value}");
        var found = (await ReportAsync(database)).ShouldHaveSingleItem();
        found.Id.ShouldBe(SecretSettingsReport.Unreadable);
        found.Message.ShouldContain("areas media.");

        await WriteAsync(database, SiteA, """{"maxUploadMb":30}""");
        StoredApiKey(database, SiteA.Value).ShouldBe(payload);
        (await database.GetAsync(SiteA)).MaxUploadMb.ShouldBe(30);

        await WriteAsync(database, SiteA, """{"apiKey":"set-again"}""");
        (await database.GetAsync(SiteA)).ApiKey.ShouldBe("set-again");
        (await ReportAsync(database)).ShouldBeEmpty();
    }

    [Fact]
    public async Task AnInstanceSecretAnotherKeyRingProtectedOutlivesAWriteThatLeavesItOut()
    {
        using var database = new SettingsDatabase();
        database.AddRow(null, "media", Foreign($$"""{"apiKey":"{{Secret}}"}""", null));
        var payload = StoredApiKey(database, null);

        (await database.GetDefaultAsync()).ApiKey.ShouldBeNull();
        await WriteAsync(database, null, """{"maxUploadMb":30}""");
        StoredApiKey(database, null).ShouldBe(payload);

        await database.SetDefaultAsync(new MediaSettings { ApiKey = "set-again" });
        (await database.GetDefaultAsync()).ApiKey.ShouldBe("set-again");
    }

    [Fact]
    public async Task AnInstanceSecretCopiedIntoASiteRowIsNotRead()
    {
        using var database = new SettingsDatabase();
        await database.SetDefaultAsync(new MediaSettings { ApiKey = Secret });
        database.AddRow(SiteA.Value, "media", new JsonObject { ["apiKey"] = StoredApiKey(database, null) }.ToJsonString());

        (await database.GetAsync(SiteA)).ApiKey.ShouldBeNull();
        (await database.GetDefaultAsync()).ApiKey.ShouldBe(Secret);
    }

    [Fact]
    public async Task APlaintextSecretOfTheInstanceDefaultsIsReadReportedAndProtectedByTheNextWrite()
    {
        using var database = new SettingsDatabase();
        database.AddRow(null, "media", $$"""{"apiKey":"{{Secret}}"}""");

        (await database.GetDefaultAsync()).ApiKey.ShouldBe(Secret);
        (await database.GetAsync(SiteA)).ApiKey.ShouldBe(Secret);
        (await ReportAsync(database)).ShouldHaveSingleItem().Id.ShouldBe(SecretSettingsReport.Unprotected);

        await WriteAsync(database, null, """{"maxUploadMb":30}""");
        database.Rows().ShouldAllBe(row => !row.Json.Contains(Secret));
        (await database.GetDefaultAsync()).ApiKey.ShouldBe(Secret);
        (await ReportAsync(database)).ShouldBeEmpty();
    }

    [Fact]
    public async Task AReportThatCannotReadTheTableLogsAndListsNothing()
    {
        using var database = new SettingsDatabase();
        database.AddRow(SiteA.Value, "media", $$"""{"apiKey":"{{Secret}}"}""");

        var found = await database.AsAsync(Admin, (IServiceProvider services) => Task.FromResult(new SecretSettingsReport(
            new FailingScopes(),
            services.GetRequiredService<SettingsRegistry>(),
            services.GetRequiredService<SettingsProtection>(),
            services.GetRequiredService<ILogger<SecretSettingsReport>>()).GetInsecureDefaults().ToList()));

        found.ShouldBeEmpty();
        database.Logs.All.ShouldContain("could not be checked for secrets that are not protected or that the key ring cannot unprotect (InvalidOperationException)");
    }

    private static string Foreign(string json, Guid? site) =>
        new SettingsProtection(new EphemeralDataProtectionProvider(), NullLogger<SettingsProtection>.Instance).Protect(new SettingsDescriptor("media", typeof(MediaSettings)), json, site);

    private static string StoredApiKey(SettingsDatabase database, Guid? site) =>
        JsonNode.Parse(database.Rows().Single(row => row.Key == "media" && row.SiteId == site).Json)!["apiKey"]!.GetValue<string>();

    private static Task<bool> WriteAsync(SettingsDatabase database, SiteId? site, string json) =>
        database.AsAsync(Admin, async (IServiceProvider services) =>
        {
            var service = services.GetRequiredService<SettingsService>();
            await service.WriteAsync(service.Require("media"), site, json, null, Token);
            return true;
        });

    private sealed class FailingScopes : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new InvalidOperationException("No scope.");
    }

    private static Task<List<InsecureDefault>> ReportAsync(SettingsDatabase database) =>
        database.AsAsync(Admin, (IServiceProvider services) => Task.FromResult(services.GetServices<IInsecureDefaultSource>().OfType<SecretSettingsReport>().Single().GetInsecureDefaults().ToList()));
}
