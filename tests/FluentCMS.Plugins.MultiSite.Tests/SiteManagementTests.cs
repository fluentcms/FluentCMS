using System.Net;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.MultiSite.Tests.MultiSiteTesting;

namespace FluentCMS.Plugins.MultiSite.Tests;

// POST /sites and DELETE /sites/{id}, beside the routes of core that read and change a site.
public sealed class SiteManagementTests(MultiSiteHost fixture) : IClassFixture<MultiSiteHost>
{
    [Fact]
    public async Task ASiteCreatedThroughTheApiHasItsDefaultLocaleAndItsPublicRole()
    {
        using var admin = await AdminAsync(fixture.Host);
        var slug = Slug();

        var created = await SendAsync(admin, HttpMethod.Post, SitesRoute, new { name = " Docs ", slug, defaultLocale = "FR-ca" }, HttpStatusCode.Created);

        var id = Guid.Parse(created!["id"]!.GetValue<string>());
        created["name"]!.GetValue<string>().ShouldBe("Docs");
        created["slug"]!.GetValue<string>().ShouldBe(slug);
        created["defaultLocale"]!.GetValue<string>().ShouldBe("fr-CA");
        created["isDefault"]!.GetValue<bool>().ShouldBeFalse();

        var read = await SendAsync(admin, HttpMethod.Get, $"{SitesRoute}/{id}");
        read!.ToJsonString().ShouldBe(created.ToJsonString());
        var locales = await SendAsync(admin, HttpMethod.Get, $"{SitesRoute}/{id}/locales");
        locales!["items"]!.AsArray().Select(locale => locale!["code"]!.GetValue<string>()).ShouldBe(["fr-CA"]);

        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var roles = await scope.ServiceProvider.GetRequiredService<CmsDbContext>().Set<Role>().AsNoTracking().Where(role => role.SiteId == id).Select(role => role.Name).ToListAsync(Token);
        roles.ShouldBe([BuiltInRoles.PublicName]);

        (await ActivityAsync(fixture.Host, id)).ShouldContain(("site.created", null, id));
    }

    [Theory]
    [InlineData("", "docs", "en", "name", "FCMS2304")]
    [InlineData("Docs", "Not A Slug", "en", "slug", "FCMS2304")]
    [InlineData("Docs", null, "en", "slug", "FCMS2304")]
    [InlineData("Docs", "docs", "en_US", "defaultLocale", "FCMS2311")]
    public async Task ASiteThatIsNotWellFormedIsRefusedWithTheCodeOfCore(string name, string? slug, string locale, string path, string code)
    {
        using var admin = await AdminAsync(fixture.Host);

        var problem = await SendAsync(admin, HttpMethod.Post, SitesRoute, new { name, slug, defaultLocale = locale }, HttpStatusCode.UnprocessableEntity);

        var error = problem!["errors"]!.AsArray().Single()!;
        (error["path"]!.GetValue<string>(), error["code"]!.GetValue<string>()).ShouldBe((path, code));
    }

    [Fact]
    public async Task ASlugThatASiteHasIsRefused()
    {
        using var admin = await AdminAsync(fixture.Host);
        var slug = Slug();
        await CreateSiteAsync(admin, slug);

        var problem = await SendAsync(admin, HttpMethod.Post, SitesRoute, new { name = "Again", slug, defaultLocale = "en" }, HttpStatusCode.UnprocessableEntity);

        Code(problem).ShouldBe("FCMS2304");
    }

    [Fact]
    public async Task DeletingRefusesTheDefaultSiteAndASiteWithContentAndAnUnknownSite()
    {
        using var admin = await AdminAsync(fixture.Host);
        var site = await CreateSiteAsync(admin);
        await PublishArticleAsync(fixture.Host, site, "Kept");

        Code(await SendAsync(admin, HttpMethod.Delete, $"{SitesRoute}/{DefaultSite.Id}", expected: HttpStatusCode.Conflict)).ShouldBe("FCMS2302");
        Code(await SendAsync(admin, HttpMethod.Delete, $"{SitesRoute}/{site}", expected: HttpStatusCode.Conflict)).ShouldBe("FCMS2303");
        Code(await SendAsync(admin, HttpMethod.Delete, $"{SitesRoute}/{Guid.CreateVersion7()}", expected: HttpStatusCode.NotFound)).ShouldBe("FCMS2301");

        (await SendAsync(admin, HttpMethod.Get, $"{SitesRoute}/{site}"))!["id"]!.GetValue<string>().ShouldBe(site.ToString());
    }

    // The hosts go with the site, so the name is free for another site, and the row of the delete is the instance's.
    [Fact]
    public async Task DeletingASiteRemovesItsHostNamesAndWritesTheActivityRowOfTheInstance()
    {
        using var admin = await AdminAsync(fixture.Host);
        var site = await CreateSiteAsync(admin);
        var name = HostName();
        await AddHostAsync(admin, site, name);

        await SendAsync(admin, HttpMethod.Delete, $"{SitesRoute}/{site}", expected: HttpStatusCode.NoContent);

        Code(await SendAsync(admin, HttpMethod.Get, $"{SitesRoute}/{site}", expected: HttpStatusCode.NotFound)).ShouldBe("FCMS2301");
        Code(await SendAsync(admin, HttpMethod.Get, $"{SitesRoute}/{site}/hosts", expected: HttpStatusCode.NotFound)).ShouldBe("FCMS2301");
        (await ActivityAsync(fixture.Host, site)).ShouldContain(("site.deleted", null, null));

        var other = await CreateSiteAsync(admin);
        await AddHostAsync(admin, other, name);
    }

    // sites.manage for the instance: a role of one site, however much it grants there, creates and deletes no site and
    // manages no host name, not even its own site's.
    [Fact]
    public async Task ARoleOfOneSiteManagesNoSiteAndNoHostName()
    {
        using var admin = await AdminAsync(fixture.Host);
        var site = await CreateSiteAsync(admin);
        using var owner = await RoleKeyAsync(fixture.Host, admin, "management", site, null, "sites.manage");
        owner.DefaultRequestHeaders.Add("X-Cms-Site", site.ToString());

        (await SendAsync(owner, HttpMethod.Get, $"{SitesRoute}/{site}"))!["id"]!.GetValue<string>().ShouldBe(site.ToString());
        Code(await SendAsync(owner, HttpMethod.Post, SitesRoute, new { name = "More", slug = Slug(), defaultLocale = "en" }, HttpStatusCode.Forbidden)).ShouldBe("FCMS3103");
        Code(await SendAsync(owner, HttpMethod.Delete, $"{SitesRoute}/{site}", expected: HttpStatusCode.Forbidden)).ShouldBe("FCMS3103");
        Code(await SendAsync(owner, HttpMethod.Get, $"{SitesRoute}/{site}/hosts", expected: HttpStatusCode.Forbidden)).ShouldBe("FCMS3103");
        Code(await AddHostAsync(owner, site, HostName(), expected: HttpStatusCode.Forbidden)).ShouldBe("FCMS3103");
    }

    [Fact]
    public async Task ACallerThatIsNotSignedInIsRefused()
    {
        using var anonymous = fixture.Host.CreateClient();

        await SendAsync(anonymous, HttpMethod.Post, SitesRoute, new { name = "Docs", slug = Slug(), defaultLocale = "en" }, HttpStatusCode.Unauthorized);
        await SendAsync(anonymous, HttpMethod.Get, $"{SitesRoute}/{DefaultSite.Id}/hosts", expected: HttpStatusCode.Unauthorized);
    }

    // Where the plugin is disabled for the site of a request, its routes answer 404, and the routes of core still answer.
    [Fact]
    public async Task ThePluginsRoutesAnswer404ForARequestOfASiteWhereItIsDisabled()
    {
        using var admin = await AdminAsync(fixture.Host);
        var site = await CreateSiteAsync(admin);
        await SendAsync(admin, HttpMethod.Put, $"/api/management/v1/plugins/multi-site/sites/{site}", new { enabled = false });

        Code(await SendAsync(admin, HttpMethod.Get, $"{SitesRoute}/{site}/hosts?site={site}", expected: HttpStatusCode.NotFound)).ShouldBe("FCMS1550");
        (await SendAsync(admin, HttpMethod.Get, $"{SitesRoute}/{site}?site={site}"))!["id"]!.GetValue<string>().ShouldBe(site.ToString());
        (await SendAsync(admin, HttpMethod.Get, $"{SitesRoute}/{site}/hosts"))!["items"]!.AsArray().ShouldBeEmpty();
    }
}
