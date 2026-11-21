using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Settings;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Diagnostics;
using FluentCMS.Settings;
using FluentCMS.Sites;
using FluentCMS.Transfer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
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

    [Fact]
    public async Task ANullSentOverAnUnreadableSecretClearsItForASiteAndTheInstance()
    {
        using var database = new SettingsDatabase();
        database.AddRow(SiteA.Value, "media", Foreign($$"""{"apiKey":"{{Secret}}"}""", SiteA.Value));
        database.AddRow(null, "media", Foreign($$"""{"apiKey":"{{Secret}}"}""", null));

        await WriteAsync(database, SiteA, """{"apiKey":null}""");
        await WriteAsync(database, null, """{"apiKey":null}""");

        database.Rows().ShouldAllBe(row => JsonNode.Parse(row.Json)!["apiKey"] == null);
        (await ReportAsync(database)).ShouldBeEmpty();
        (await database.GetAsync(SiteA)).ApiKey.ShouldBeNull();
    }

    [Fact]
    public async Task AnImportKeepsAnUnreadableSecretOfTheSiteAndAnExportShowsNothingOfIt()
    {
        using var database = new SettingsDatabase();
        database.AddRow(SiteA.Value, "media", Foreign($$"""{"provider":"s3","apiKey":"{{Secret}}"}""", SiteA.Value));
        var payload = StoredApiKey(database, SiteA.Value);

        var export = await database.ServiceAsync((service, _) => service.SiteDocumentsAsync(SiteA, Token));
        export.ShouldHaveSingleItem().Json.ShouldNotContain("apiKey");
        export.ShouldAllBe(document => !document.Json.Contains(SettingsProtection.Prefix) && !document.Json.Contains(payload));

        var outcome = await database.AsAsync(Admin, async (IServiceProvider services) =>
            await services.GetRequiredService<SettingsService>().ImportAsync("media", SiteA, """{"provider":"gcs"}""", dryRun: false, services.GetRequiredService<IOperationContextAccessor>().Current!, Token));

        outcome.ShouldBe(TransferOutcome.Updated);
        StoredApiKey(database, SiteA.Value).ShouldBe(payload);
        var read = await database.GetAsync(SiteA);
        (read.Provider, read.ApiKey).ShouldBe(("gcs", null));
    }

    [Fact]
    public async Task AWriteOverASiteRowTheClassCannotReadDropsItsUnreadableSecretWithAWarningAndInheritsAgain()
    {
        using var database = new SettingsDatabase();
        await database.SetDefaultAsync(new MediaSettings { ApiKey = "instance-key" });
        database.AddRow(SiteA.Value, "media", Foreign($$"""{"maxUploadMb":"not a number","apiKey":"{{Secret}}"}""", SiteA.Value));
        (await database.GetAsync(SiteA)).ApiKey.ShouldBe("instance-key");

        await WriteAsync(database, SiteA, """{"maxUploadMb":30}""");

        JsonNode.Parse(database.Rows().Single(row => row.SiteId == SiteA.Value).Json)!.AsObject().ContainsKey("apiKey").ShouldBeFalse();
        database.Logs.All.ShouldContain($"The stored value of the secret key apiKey of area 'media' for site {SiteA.Value} can no longer be read by the class and was not kept");
        var read = await database.GetAsync(SiteA);
        (read.MaxUploadMb, read.ApiKey).ShouldBe((30, "instance-key"));
    }

    [Fact]
    public async Task AnInstanceRowTheClassCannotReadKeepsItsUnreadableSecret()
    {
        using var database = new SettingsDatabase();
        database.AddRow(null, "media", Foreign($$"""{"maxUploadMb":"not a number","apiKey":"{{Secret}}"}""", null));
        var payload = StoredApiKey(database, null);

        await WriteAsync(database, null, """{"maxUploadMb":30}""");

        StoredApiKey(database, null).ShouldBe(payload);
        database.Logs.All.ShouldNotContain("can no longer be read by the class and was not kept");
        var read = await database.GetDefaultAsync();
        (read.MaxUploadMb, read.ApiKey).ShouldBe((30, null));
    }

    [Fact]
    public async Task AWriteOverASiteRowTheClassCannotReadThatNamesTheSecretStoresIt()
    {
        using var database = new SettingsDatabase();
        await database.SetDefaultAsync(new MediaSettings { ApiKey = "instance-key" });
        database.AddRow(SiteA.Value, "media", Foreign($$"""{"maxUploadMb":"not a number","apiKey":"{{Secret}}"}""", SiteA.Value));

        await WriteAsync(database, SiteA, """{"maxUploadMb":30,"apiKey":"set-again"}""");

        StoredApiKey(database, SiteA.Value).ShouldStartWith(SettingsProtection.Prefix);
        (await database.GetAsync(SiteA)).ApiKey.ShouldBe("set-again");
        (await ReportAsync(database)).ShouldBeEmpty();
    }

    [Fact]
    public async Task ARequiredSecretTheKeyRingCannotReadKeepsItsRowReadableAndAWriteCannotLeaveItOut()
    {
        using var database = new SettingsDatabase(cms => cms.Settings.Add<RequiredSecret>());
        database.AddRow(SiteA.Value, "required-secret", Foreign($$"""{"token":"{{Secret}}","name":"a"}""", SiteA.Value, "required-secret", typeof(RequiredSecret)));
        var payload = Stored(database, "required-secret", SiteA.Value, "token");

        var read = await database.AsAsync(Admin, async (IServiceProvider services) => await services.GetRequiredService<ISettings<RequiredSecret>>().GetAsync(SiteA, Token));
        (read.Name, read.Token).ShouldBe(("a", "code-token"));
        database.Logs.All.ShouldNotContain("cannot be read by the class as it is now");

        // A document without a required key is refused before the row is read, so no write can drop the stored value.
        (await Should.ThrowAsync<ValidationException>(() => WriteAsync(database, SiteA, """{"name":"b"}""", "required-secret"))).Errors.ShouldHaveSingleItem().Code.ShouldBe("FCMS2805");
        Stored(database, "required-secret", SiteA.Value, "token").ShouldBe(payload);

        await WriteAsync(database, SiteA, """{"name":"b","token":"set-again"}""", "required-secret");
        var written = await database.AsAsync(Admin, async (IServiceProvider services) => await services.GetRequiredService<ISettings<RequiredSecret>>().GetAsync(SiteA, Token));
        (written.Name, written.Token).ShouldBe(("b", "set-again"));
    }

    [Fact]
    public async Task ARequiredSecretTheKeyRingCannotReadIsNotListedAsSet()
    {
        using var database = new SettingsDatabase(cms => cms.Settings.Add<RequiredSecret>());
        database.AddRow(SiteA.Value, "required-secret", Foreign($$"""{"token":"{{Secret}}","name":"a"}""", SiteA.Value, "required-secret", typeof(RequiredSecret)));
        database.AddRow(null, "required-secret", Foreign($$"""{"token":"{{Secret}}","name":"i"}""", null, "required-secret", typeof(RequiredSecret)));

        var site = await RedactedAsync(database, "required-secret", SiteA);
        (site.Source, site.SecretsSet.Count, site.SecretsInherited.Count).ShouldBe((SettingsSource.Site, 0, 0));
        site.Json.ShouldContain("\"name\":\"a\"");
        var instance = await RedactedAsync(database, "required-secret", null);
        (instance.Source, instance.SecretsSet.Count).ShouldBe((SettingsSource.Default, 0));
        (await RedactedAsync(database, "required-secret", SiteB)).SecretsSet.ShouldBeEmpty();

        await WriteAsync(database, SiteA, """{"name":"b","token":"set-again"}""", "required-secret");
        (await RedactedAsync(database, "required-secret", SiteA)).SecretsSet.ShouldBe(["token"]);
    }

    // The instance row cannot be read without its required secret, so both of its unreadable secrets hold their code defaults,
    // and the site takes the default of the one it inherits.
    [Fact]
    public async Task ASecretASiteInheritsFromAnInstanceRowTheKeyRingCannotReadIsNotListedAsSet()
    {
        using var database = new SettingsDatabase(cms => cms.Settings.Add<RequiredSecretAndAccount>());
        await WriteAsync(database, SiteA, """{"name":"a","token":"site-token"}""", "required-and-account");
        database.AddRow(null, "required-and-account", Foreign($$"""{"token":"{{Secret}}","account":"{{Secret}}-account","name":"i"}""", null, "required-and-account", typeof(RequiredSecretAndAccount)));

        var site = await RedactedAsync(database, "required-and-account", SiteA);

        site.Source.ShouldBe(SettingsSource.Site);
        site.SecretsSet.ShouldBe(["token"]);
        site.SecretsInherited.ShouldBe(["account"]);
        (await RedactedAsync(database, "required-and-account", null)).SecretsSet.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnImportOfAnAreaThatRequiresASecretKeepsTheSitesOwnValue()
    {
        using var database = new SettingsDatabase(cms => cms.Settings.Add<RequiredSecret>());
        await WriteAsync(database, SiteA, """{"name":"a","token":"site-token"}""", "required-secret");

        (await ImportAsync(database, "required-secret", SiteA, """{"name":"imported"}""")).ShouldBe(TransferOutcome.Updated);

        var read = await database.AsAsync(Admin, async (IServiceProvider services) => await services.GetRequiredService<ISettings<RequiredSecret>>().GetAsync(SiteA, Token));
        (read.Name, read.Token).ShouldBe(("imported", "site-token"));
        (await ImportAsync(database, "required-secret", SiteA, """{"name":"imported"}""")).ShouldBe(TransferOutcome.Skipped);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnImportOfAnAreaThatRequiresASecretTheSiteDoesNotHoldIsRefusedAndMakesNoValueUp(bool dryRun)
    {
        using var database = new SettingsDatabase(cms => cms.Settings.Add<RequiredSecret>());
        await WriteAsync(database, null, """{"name":"i","token":"instance-token"}""", "required-secret");

        var refused = await Should.ThrowAsync<ValidationException>(() => ImportAsync(database, "required-secret", SiteA, $$"""{"name":"imported","token":"{{Secret}}"}""", dryRun));

        var error = refused.Errors.ShouldHaveSingleItem();
        (error.Path, error.Code).ShouldBe(("token", "FCMS2805"));
        error.Message.ShouldBe("The settings area 'required-secret' requires the secret 'token', which a package never carries and the site does not hold; set it in the site, then import again.");
        database.Rows().ShouldHaveSingleItem().SiteId.ShouldBeNull();
    }

    [Fact]
    public async Task AnImportOfAnAreaWithASecretRequiredByAnAttributeUsesTheSitesOwnValueOrIsRefused()
    {
        using var database = new SettingsDatabase(cms => cms.Settings.Add<RequiredAttributeSecret>());
        await WriteAsync(database, null, """{"name":"i","apiKey":"instance-key"}""", "required-attribute-secret");
        await WriteAsync(database, SiteA, """{"name":"a","apiKey":"site-key"}""", "required-attribute-secret");

        (await ImportAsync(database, "required-attribute-secret", SiteA, """{"name":"imported"}""")).ShouldBe(TransferOutcome.Updated);
        var read = await database.AsAsync(Admin, async (IServiceProvider services) => await services.GetRequiredService<ISettings<RequiredAttributeSecret>>().GetAsync(SiteA, Token));
        (read.Name, read.ApiKey).ShouldBe(("imported", "site-key"));

        var refused = await Should.ThrowAsync<ValidationException>(() => ImportAsync(database, "required-attribute-secret", SiteB, $$"""{"name":"imported","apiKey":"{{Secret}}"}"""));
        var error = refused.Errors.ShouldHaveSingleItem();
        (error.Path, error.Code).ShouldBe(("apiKey", "FCMS2805"));
        error.Message.ShouldNotContain(Secret);
        database.Rows().ShouldNotContain(row => row.SiteId == SiteB.Value);
    }

    // A reset of the site's area lands after the import has checked the site's row and before it writes.
    [Fact]
    public async Task AnImportOfAnAreaThatRequiresASecretChecksTheRowItWritesOver()
    {
        var check = new InterruptingCheck("required-secret");
        using var database = new SettingsDatabase(cms =>
        {
            cms.Settings.Add<RequiredSecret>();
            cms.Services.AddSingleton<ISettingsCheck>(check);
        });
        await WriteAsync(database, null, """{"name":"i","token":"instance-token"}""", "required-secret");
        await WriteAsync(database, SiteA, """{"name":"a","token":"site-token"}""", "required-secret");
        check.Arm(() =>
        {
            using var scope = database.Inner.Database.NewScope();
            scope.ServiceProvider.GetRequiredService<CmsDbContext>().Set<SettingRow>()
                .Where(row => row.Key == "required-secret" && row.SiteId == SiteA.Value)
                .ExecuteDelete();
        });

        var refused = await Should.ThrowAsync<ValidationException>(() => ImportAsync(database, "required-secret", SiteA, """{"name":"imported"}"""));

        (refused.Errors.ShouldHaveSingleItem().Path, refused.Errors.Single().Code).ShouldBe(("token", "FCMS2805"));
        database.Rows().ShouldHaveSingleItem().SiteId.ShouldBeNull();
        var read = await database.AsAsync(Admin, async (IServiceProvider services) => await services.GetRequiredService<ISettings<RequiredSecret>>().GetAsync(SiteA, Token));
        (read.Name, read.Token).ShouldBe(("i", "instance-token"));
    }

    [Fact]
    public async Task ASecretThatAPackageNamesIsNotWrittenByAnImport()
    {
        using var database = new SettingsDatabase();
        await database.SetDefaultAsync(new MediaSettings { ApiKey = "instance-key" });
        await database.SetAsync(SiteA, new MediaSettings { ApiKey = "site-key" });

        (await ImportAsync(database, "media", SiteA, $$"""{"provider":"gcs","apiKey":"{{Secret}}"}""")).ShouldBe(TransferOutcome.Updated);
        (await ImportAsync(database, "media", SiteB, $$"""{"provider":"gcs","apiKey":"{{Secret}}","passphrase":null}""")).ShouldBe(TransferOutcome.Created);

        var a = await database.GetAsync(SiteA);
        (a.Provider, a.ApiKey).ShouldBe(("gcs", "site-key"));
        var b = await database.GetAsync(SiteB);
        (b.Provider, b.ApiKey).ShouldBe(("gcs", "instance-key"));
        var row = JsonNode.Parse(database.Rows().Single(row => row.SiteId == SiteB.Value).Json)!.AsObject();
        row.ContainsKey("apiKey").ShouldBeFalse();
        row.ContainsKey("passphrase").ShouldBeFalse();
    }

    private static Task<RedactedSetting> RedactedAsync(SettingsDatabase database, string area, SiteId? site) =>
        database.AsAsync(Admin, async (IServiceProvider services) =>
        {
            var service = services.GetRequiredService<SettingsService>();
            return await service.ResolveRedactedAsync(service.Require(area), site, Token);
        });

    private static Task<TransferOutcome> ImportAsync(SettingsDatabase database, string area, SiteId site, string json, bool dryRun = false) =>
        database.AsAsync(Admin, async (IServiceProvider services) =>
            await services.GetRequiredService<SettingsService>().ImportAsync(area, site, json, dryRun, services.GetRequiredService<IOperationContextAccessor>().Current!, Token));

    private static string Foreign(string json, Guid? site, string area = "media", Type? type = null) =>
        new SettingsProtection(new EphemeralDataProtectionProvider(), NullLogger<SettingsProtection>.Instance).Protect(new SettingsDescriptor(area, type ?? typeof(MediaSettings)), json, site);

    private static string StoredApiKey(SettingsDatabase database, Guid? site) => Stored(database, "media", site, "apiKey");

    private static string Stored(SettingsDatabase database, string area, Guid? site, string key) =>
        JsonNode.Parse(database.Rows().Single(row => row.Key == area && row.SiteId == site).Json)![key]!.GetValue<string>();

    private static Task<bool> WriteAsync(SettingsDatabase database, SiteId? site, string json, string area = "media") =>
        database.AsAsync(Admin, async (IServiceProvider services) =>
        {
            var service = services.GetRequiredService<SettingsService>();
            await service.WriteAsync(service.Require(area), site, json, null, Token);
            return true;
        });

    private sealed class FailingScopes : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new InvalidOperationException("No scope.");
    }

    private static Task<List<InsecureDefault>> ReportAsync(SettingsDatabase database) =>
        database.AsAsync(Admin, (IServiceProvider services) => Task.FromResult(services.GetServices<IInsecureDefaultSource>().OfType<SecretSettingsReport>().Single().GetInsecureDefaults().ToList()));
}
