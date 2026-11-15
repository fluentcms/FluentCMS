using System.Net;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Events;
using FluentCMS.Core.Settings;
using FluentCMS.Core.Sites;
using FluentCMS.Seeding;
using FluentCMS.Settings;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Management;

// A seeder that writes nothing, so that the host registers the area "seeding".
public sealed class NothingToSeed : ISeeder
{
    public Task SeedAsync(SeedContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

// The reset of a settings area and the list of the areas over HTTP, on SQLite (#3098). The area "branding" never gets a
// row here, so each test may reset it.
public sealed class SettingsResetAndListTests(AdminFixture fixture) : IClassFixture<AdminFixture>
{
    private static readonly string Site = DefaultSite.Id.ToString("D");

    [Fact]
    public async Task ASiteThatResetsItsAreaInheritsTheSecretOfTheDefaultsAgain()
    {
        var defaults = await fixture.PutAsync("settings/mail/defaults", """{ "host": "smtp.instance.example", "port": 587, "password": "password-of-the-instance" }""", ifMatch: "\"0\"");
        defaults.Status.ShouldBe(HttpStatusCode.OK, defaults.Body?.ToJsonString());
        var own = await fixture.PutAsync("settings/mail", """{ "host": "smtp.site.example", "port": 25, "password": "password-of-the-site" }""", ifMatch: "\"0\"");
        own.Status.ShouldBe(HttpStatusCode.OK, own.Body?.ToJsonString());
        own.Json["secretsInherited"]!.AsArray().ShouldBeEmpty();
        (await PasswordAsync()).ShouldBe("password-of-the-site");

        var stale = await fixture.DeleteAsync("settings/mail", ifMatch: "\"0\"");
        stale.Status.ShouldBe(HttpStatusCode.PreconditionFailed);
        stale.Json["code"]!.GetValue<string>().ShouldBe("FCMS2808");
        var unconditional = await fixture.DeleteAsync("settings/mail");
        unconditional.Status.ShouldBe(HttpStatusCode.PreconditionRequired);
        unconditional.Json["code"]!.GetValue<string>().ShouldBe("FCMS3130");
        (await PasswordAsync()).ShouldBe("password-of-the-site");

        var changes = await ChangesAsync("mail");
        var reset = await fixture.DeleteAsync("settings/mail", ifMatch: own.ETag);
        reset.Status.ShouldBe(HttpStatusCode.NoContent, reset.Body?.ToJsonString());
        reset.Body.ShouldBeNull();
        (await ChangesAsync("mail")).ShouldBe(changes + 1);

        var read = await fixture.GetAsync("settings/mail");
        read.ETag.ShouldBe("\"0\"");
        read.Json["source"]!.GetValue<string>().ShouldBe("default");
        read.Json["value"]!["host"]!.GetValue<string>().ShouldBe("smtp.instance.example");
        read.Json["secretsInherited"]!.AsArray().Select(key => key!.GetValue<string>()).ShouldBe(["password"]);
        (await PasswordAsync()).ShouldBe("password-of-the-instance");

        // The defaults replace the secret, and the site follows; a write of the site that leaves it out keeps it inherited.
        var replaced = await fixture.PutAsync("settings/mail/defaults", """{ "host": "smtp.instance.example", "port": 587, "password": "password-that-replaced-it" }""", ifMatch: defaults.ETag);
        replaced.Status.ShouldBe(HttpStatusCode.OK, replaced.Body?.ToJsonString());
        (await PasswordAsync()).ShouldBe("password-that-replaced-it");
        var saved = await fixture.PutAsync("settings/mail", """{ "host": "smtp.site.example", "port": 25 }""", ifMatch: "\"0\"");
        saved.Json["secretsInherited"]!.AsArray().Select(key => key!.GetValue<string>()).ShouldBe(["password"]);
        (await PasswordAsync()).ShouldBe("password-that-replaced-it");

        // A reset of the defaults leaves the secret the site inherits to its code default.
        (await fixture.DeleteAsync("settings/mail/defaults", ifMatch: replaced.ETag)).Status.ShouldBe(HttpStatusCode.NoContent);
        var instance = await fixture.GetAsync("settings/mail/defaults");
        (instance.ETag, instance.Json["source"]!.GetValue<string>()).ShouldBe(("\"0\"", "code"));
        (await PasswordAsync()).ShouldBeNull();
        (await fixture.GetAsync("settings/mail")).Json["value"]!["host"]!.GetValue<string>().ShouldBe("smtp.site.example");
    }

    [Fact]
    public async Task AResetOfAnAreaWithoutADocumentChangesNothing()
    {
        var changes = await ChangesAsync("branding");

        (await fixture.DeleteAsync("settings/branding", ifMatch: "\"0\"")).Status.ShouldBe(HttpStatusCode.NoContent);
        (await fixture.DeleteAsync("settings/branding/defaults", ifMatch: "\"0\"")).Status.ShouldBe(HttpStatusCode.NoContent);
        var moved = await fixture.DeleteAsync("settings/branding", ifMatch: "\"3\"");

        moved.Status.ShouldBe(HttpStatusCode.PreconditionFailed);
        moved.Json["code"]!.GetValue<string>().ShouldBe("FCMS2808");
        (await ChangesAsync("branding")).ShouldBe(changes);
        (await fixture.GetAsync("settings/branding")).Json["source"]!.GetValue<string>().ShouldBe("code");
    }

    [Fact]
    public async Task AResetNeedsThePermissionOfAWrite()
    {
        var role = await fixture.CreateRoleAsync($$"""
            { "name": "Branding admin {{Guid.NewGuid():N}}", "siteId": "{{Site}}", "grants": [ { "permission": "settings.branding.manage" } ] }
            """);
        using var siteAdmin = await fixture.ClientForAsync((await fixture.GetAsync($"roles/{role}")).Json["name"]!.GetValue<string>());
        using var other = await fixture.ClientWithAsync("jobs.manage");

        (await fixture.DeleteAsync("settings/branding", siteAdmin, "\"0\"")).Status.ShouldBe(HttpStatusCode.NoContent);
        var defaults = await fixture.DeleteAsync("settings/branding/defaults", siteAdmin, "\"0\"");
        defaults.Status.ShouldBe(HttpStatusCode.Forbidden);
        defaults.Json["code"]!.GetValue<string>().ShouldBe("FCMS3103");
        (await fixture.DeleteAsync("settings/branding", other, "\"0\"")).Status.ShouldBe(HttpStatusCode.Forbidden);

        // A caller without the permission cannot tell an unknown area from one it may not manage.
        (await fixture.DeleteAsync("settings/unknown", other, "\"0\"")).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.DeleteAsync("settings/unknown", ifMatch: "\"0\"")).Json["code"]!.GetValue<string>().ShouldBe("FCMS2802");
    }

    [Fact]
    public async Task TheListHoldsTheAreasTheCallerMayManage()
    {
        var listed = await fixture.GetAsync("settings");
        listed.Status.ShouldBe(HttpStatusCode.OK, listed.Body?.ToJsonString());
        var items = listed.Json["items"]!.AsArray();
        items.Select(item => item!["area"]!.GetValue<string>()).ShouldBeInOrder(SortDirection.Ascending, StringComparer.Ordinal);
        var mail = items.Single(item => item!["area"]!.GetValue<string>() == "mail")!;
        mail.AsObject().Select(property => property.Key).ShouldBe(["area", "plugin", "site", "defaults"]);
        mail["plugin"].ShouldBeNull();
        (mail["site"]!.GetValue<bool>(), mail["defaults"]!.GetValue<bool>()).ShouldBe((true, true));
        items.ShouldContain(item => item!["area"]!.GetValue<string>() == "branding");

        var role = await fixture.CreateRoleAsync($$"""
            { "name": "Mail admin {{Guid.NewGuid():N}}", "siteId": "{{Site}}", "grants": [ { "permission": "settings.mail.manage" } ] }
            """);
        using var siteAdmin = await fixture.ClientForAsync((await fixture.GetAsync($"roles/{role}")).Json["name"]!.GetValue<string>());
        using var instanceAdmin = await fixture.ClientWithAsync("settings.branding.manage");
        using var other = await fixture.ClientWithAsync("jobs.manage");

        var ofSite = (await fixture.GetAsync("settings", siteAdmin)).Json["items"]!.AsArray().ShouldHaveSingleItem()!;
        (ofSite["area"]!.GetValue<string>(), ofSite["site"]!.GetValue<bool>(), ofSite["defaults"]!.GetValue<bool>()).ShouldBe(("mail", true, false));
        var ofInstance = (await fixture.GetAsync("settings", instanceAdmin)).Json["items"]!.AsArray().ShouldHaveSingleItem()!;
        (ofInstance["area"]!.GetValue<string>(), ofInstance["site"]!.GetValue<bool>(), ofInstance["defaults"]!.GetValue<bool>()).ShouldBe(("branding", true, true));
        (await fixture.GetAsync("settings", other)).Json["items"]!.AsArray().ShouldBeEmpty();
        (await fixture.GetAsync("settings?area=mail")).Json["code"]!.GetValue<string>().ShouldBe("FCMS2201");
    }

    [Fact]
    public async Task TheListLeavesOutTheAreaOfTheSeeders()
    {
        var token = TestContext.Current.CancellationToken;
        await using var host = new FluentCmsTestHost().Configure(cms => cms.Seeding.Add<NothingToSeed>());
        using var admin = host.AsKey(await host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: token));

        var listed = await AdminFixture.SendAsync(admin, HttpMethod.Get, "settings");

        listed.Status.ShouldBe(HttpStatusCode.OK, listed.Body?.ToJsonString());
        var areas = listed.Json["items"]!.AsArray().Select(item => item!["area"]!.GetValue<string>()).ToList();
        areas.ShouldContain("branding");
        areas.ShouldNotContain("seeding");
        host.Services.GetRequiredService<SettingsRegistry>().Find("seeding").ShouldNotBeNull();
    }

    private async Task<string?> PasswordAsync()
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<ISettings<MailSettings>>().GetAsync(new SiteId(DefaultSite.Id), TestContext.Current.CancellationToken)).Password;
    }

    // The activity rows of the area, once the outbox has delivered what was raised.
    private async Task<long> ChangesAsync(string area)
    {
        await fixture.Host.Services.GetRequiredService<OutboxDrainer>().DrainAsync(TestContext.Current.CancellationToken);
        return (await fixture.GetAsync($"activity?action=settings.changed&targetId={area}")).Json["total"]!.GetValue<long>();
    }
}
