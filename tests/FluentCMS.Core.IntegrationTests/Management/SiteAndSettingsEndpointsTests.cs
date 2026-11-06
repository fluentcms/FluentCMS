using System.Net;
using FluentCMS.Core.Sites;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Management;

// The default site and its locales, and settings with their secrets and versions, over HTTP on SQLite (#2786).
public sealed class SiteAndSettingsEndpointsTests(AdminFixture fixture) : IClassFixture<AdminFixture>
{
    private static readonly string Site = DefaultSite.Id.ToString("D");

    [Fact]
    public async Task TheDefaultSiteIsReadAndChanged()
    {
        var listed = await fixture.GetAsync("sites");
        var site = listed.Json["items"]!.AsArray()[0]!;
        site["id"]!.GetValue<string>().ShouldBe(Site);
        site["isDefault"]!.GetValue<bool>().ShouldBeTrue();
        site["defaultLocale"]!.GetValue<string>().ShouldBe("en");

        var renamed = await fixture.PatchAsync($"sites/{Site}", """{ "name": "Main" }""");
        renamed.Status.ShouldBe(HttpStatusCode.OK, renamed.Body?.ToJsonString());
        renamed.Json["name"]!.GetValue<string>().ShouldBe("Main");
        (await fixture.GetAsync($"sites/{Site}")).Json["name"]!.GetValue<string>().ShouldBe("Main");

        var refused = await fixture.PatchAsync($"sites/{Site}", """{ "defaultLocale": "nl" }""");
        refused.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        refused.Json["code"]!.GetValue<string>().ShouldBe("FCMS2312");

        var unknown = await fixture.GetAsync($"sites/{Guid.NewGuid():D}");
        unknown.Status.ShouldBe(HttpStatusCode.NotFound);
        unknown.Json["code"]!.GetValue<string>().ShouldBe("FCMS2301");
    }

    [Fact]
    public async Task LocalesAreAddedChangedAndRemoved()
    {
        var added = await fixture.PostAsync($"sites/{Site}/locales", """{ "code": "DE" }""");
        added.Status.ShouldBe(HttpStatusCode.Created, added.Body?.ToJsonString());
        added.Json["code"]!.GetValue<string>().ShouldBe("de");
        added.Json["enabled"]!.GetValue<bool>().ShouldBeTrue();
        added.Json["fallbacks"].ShouldBeNull();
        (await fixture.PostAsync($"sites/{Site}/locales", """{ "code": "de" }""")).Json["code"]!.GetValue<string>().ShouldBe("FCMS3137");

        var regional = await fixture.PostAsync($"sites/{Site}/locales", """{ "code": "de-AT", "fallbacks": ["de", "en"] }""");
        regional.Json["fallbacks"]!.AsArray().Select(code => code!.GetValue<string>()).ShouldBe(["de", "en"]);
        (await fixture.PostAsync($"sites/{Site}/locales", """{ "code": "de-CH", "fallbacks": ["fr"] }""")).Json["code"]!.GetValue<string>().ShouldBe("FCMS2312");

        var disabled = await fixture.PatchAsync($"sites/{Site}/locales/de-AT", """{ "enabled": false, "fallbacks": null }""");
        disabled.Status.ShouldBe(HttpStatusCode.OK, disabled.Body?.ToJsonString());
        disabled.Json["enabled"]!.GetValue<bool>().ShouldBeFalse();
        disabled.Json["fallbacks"].ShouldBeNull();
        (await fixture.PatchAsync($"sites/{Site}/locales/en", """{ "enabled": false }""")).Json["code"]!.GetValue<string>().ShouldBe("FCMS3136");

        var codes = (await fixture.GetAsync($"sites/{Site}/locales")).Json["items"]!.AsArray().Select(item => item!["code"]!.GetValue<string>()).ToList();
        codes.ShouldBe(["en", "de", "de-AT"]);

        var created = await fixture.PostAsync("content/page?locale=de", """{ "title": "Hallo" }""");
        created.Status.ShouldBe(HttpStatusCode.Created, created.Body?.ToJsonString());
        (await fixture.DeleteAsync($"sites/{Site}/locales/de")).Json["code"]!.GetValue<string>().ShouldBe("FCMS3136");
        (await fixture.DeleteAsync($"sites/{Site}/locales/en")).Json["code"]!.GetValue<string>().ShouldBe("FCMS3136");
        (await fixture.DeleteAsync($"sites/{Site}/locales/de-AT")).Status.ShouldBe(HttpStatusCode.NoContent);
        (await fixture.DeleteAsync($"sites/{Site}/locales/de-AT")).Json["code"]!.GetValue<string>().ShouldBe("FCMS3135");

        (await fixture.GetAsync($"sites/{Site}/locales")).Json["items"]!.AsArray().Select(item => item!["code"]!.GetValue<string>()).ShouldBe(["en", "de"]);
        (await fixture.GetAsync($"activity?action=site.locale-removed&targetId={Site}")).Json["items"]!.AsArray().ShouldHaveSingleItem()!["locale"]!.GetValue<string>().ShouldBe("de-AT");
    }

    [Fact]
    public async Task SettingsShowNoSecretAndAStaleVersionIsRefused()
    {
        var read = await fixture.GetAsync("settings/mail");
        read.Status.ShouldBe(HttpStatusCode.OK, read.Body?.ToJsonString());
        read.ETag.ShouldBe("\"0\"");
        read.Json["source"]!.GetValue<string>().ShouldBe("code");
        read.Json["siteId"]!.GetValue<string>().ShouldBe(Site);
        read.Json["value"]!["host"]!.GetValue<string>().ShouldBe("localhost");
        read.Json["value"]!.AsObject().ContainsKey("password").ShouldBeFalse();
        read.Json["schema"]!["properties"]!["password"]!["writeOnly"]!.GetValue<bool>().ShouldBeTrue();

        var written = await fixture.PutAsync("settings/mail", """{ "host": "smtp.example.com", "port": 587, "password": "pa55-word-value" }""", ifMatch: "\"0\"");
        written.Status.ShouldBe(HttpStatusCode.OK, written.Body?.ToJsonString());
        written.ETag.ShouldBe("\"1\"");
        written.Json["source"]!.GetValue<string>().ShouldBe("site");
        written.Json["value"]!["host"]!.GetValue<string>().ShouldBe("smtp.example.com");
        written.Json["secretsSet"]!.AsArray().Select(key => key!.GetValue<string>()).ShouldBe(["password"]);
        written.Body!.ToJsonString().ShouldNotContain("pa55-word-value");
        (await fixture.GetAsync("settings/mail")).Body!.ToJsonString().ShouldNotContain("pa55-word-value");

        var stale = await fixture.PutAsync("settings/mail", """{ "host": "lost.example.com", "port": 25 }""", ifMatch: "\"0\"");
        stale.Status.ShouldBe(HttpStatusCode.PreconditionFailed);
        stale.Json["code"]!.GetValue<string>().ShouldBe("FCMS2808");

        var unconditional = await fixture.PutAsync("settings/mail", """{ "host": "lost.example.com", "port": 25 }""");
        unconditional.Status.ShouldBe(HttpStatusCode.PreconditionRequired);
        unconditional.Json["code"]!.GetValue<string>().ShouldBe("FCMS3130");

        var kept = await fixture.PutAsync("settings/mail", """{ "host": "smtp2.example.com", "port": 587 }""", ifMatch: "\"1\"");
        kept.ETag.ShouldBe("\"2\"");
        kept.Json["secretsSet"]!.AsArray().Select(key => key!.GetValue<string>()).ShouldBe(["password"]);

        var invalid = await fixture.PutAsync("settings/mail", """{ "host": "smtp.example.com", "port": 0 }""", ifMatch: "\"2\"");
        invalid.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        invalid.Json["code"]!.GetValue<string>().ShouldBe("FCMS2805");

        var defaults = await fixture.GetAsync("settings/mail/defaults");
        defaults.Json["siteId"].ShouldBeNull();
        defaults.Json["version"]!.GetValue<int>().ShouldBe(0);
        (await fixture.GetAsync("settings/unknown")).Json["code"]!.GetValue<string>().ShouldBe("FCMS2802");
    }

    [Fact]
    public async Task ASiteLimitedGrantChangesTheSiteButNotTheInstanceDefaults()
    {
        var role = await fixture.CreateRoleAsync($$"""
            { "name": "Mail admin {{Guid.NewGuid():N}}", "siteId": "{{Site}}", "grants": [ { "permission": "settings.mail.manage" } ] }
            """);
        var name = (await fixture.GetAsync($"roles/{role}")).Json["name"]!.GetValue<string>();
        using var client = await fixture.ClientForAsync(name);

        (await fixture.GetAsync("settings/mail", client)).Status.ShouldBe(HttpStatusCode.OK);
        (await fixture.GetAsync("settings/mail/defaults", client)).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.PutAsync("settings/mail/defaults", """{ "host": "x.example.com", "port": 25 }""", client, "\"0\"")).Status.ShouldBe(HttpStatusCode.Forbidden);
    }
}
