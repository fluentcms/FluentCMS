using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Sites;
using FluentCMS.Plugins.MultiSite.Admin.Api;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.ApiTests;

// The multi-site half's calls (spec 5.6) against the server half: its records held to the OpenAPI document, a site created,
// given host names and deleted as a Super Admin, and the refusals of a caller who holds sites.manage in a site only, whose
// pages the shell hides for that reason.
public sealed class MultiSiteTests
{
    private const string Prefix = "/api/management/v1/";

    private static CancellationToken Token => AdminHost.Token;

    [Fact]
    public async Task TheRecordsOfTheHalfMatchTheOpenApiDocument()
    {
        var document = await OpenApiDocument.ReadAsync();
        var paths = document["paths"]!;
        var records = MultiSiteJsonContext.Default;
        List<string> gaps = [];

        void Check(System.Text.Json.Serialization.Metadata.JsonTypeInfo record, string path, string method, string status = "200") =>
            OpenApiContractTests.Check(record, OpenApiContractTests.Answer(paths, Prefix + path, method, status), $"{path} {method}", document, gaps);

        Check(records.ItemListSiteSummary, "sites", "get");
        Check(records.SiteSummary, "sites", "post", "201");
        Check(records.SiteSummary, "sites/{id}", "get");
        Check(records.ItemListSiteHost, "sites/{id}/hosts", "get");
        Check(records.SiteHost, "sites/{id}/hosts", "post", "201");
        Check(records.SiteHost, "sites/{id}/hosts/{hostname}", "patch");

        gaps.ShouldBeEmpty();
        paths[Prefix + "sites/{id}"]?["delete"]?["responses"]?["204"].ShouldNotBeNull();
        paths[Prefix + "sites/{id}/hosts/{hostname}"]?["delete"]?["responses"]?["204"].ShouldNotBeNull();
        Body(document, "sites", "post").Select(member => member.Key).ShouldBe(["name", "slug", "defaultLocale"], ignoreOrder: true);
        Body(document, "sites/{id}/hosts", "post").Select(member => member.Key).ShouldBe(["hostname", "isPrimary"], ignoreOrder: true);
        Body(document, "sites/{id}/hosts/{hostname}", "patch").Select(member => member.Key).ShouldBe(["isPrimary"]);
    }

    [Fact]
    public async Task ASiteIsCreatedGetsASecondHostNameThatIsMadePrimaryAndIsDeleted()
    {
        await using var admin = new AdminHost();
        using var client = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        var api = client.Api;

        var site = await api.CreateSiteAsync("Shop", "shop", "en", Token);
        (site.Name, site.Slug, site.DefaultLocale, site.IsDefault).ShouldBe(("Shop", "shop", "en", false));
        (await api.GetSitesAsync(Token)).Select(item => item.Slug).ShouldContain("shop");
        (await client.Client.GetMySitesAsync(Token)).Select(item => item.Id).ShouldContain(site.Id);

        (await api.AddHostAsync(site.Id, "shop.example.com", primary: false, Token)).IsPrimary.ShouldBeTrue();
        (await api.AddHostAsync(site.Id, "www.shop.example.com", primary: false, Token)).IsPrimary.ShouldBeFalse();
        (await api.MakePrimaryAsync(site.Id, "www.shop.example.com", Token)).IsPrimary.ShouldBeTrue();
        (await api.GetHostsAsync(site.Id, Token)).Select(host => (host.Hostname, host.IsPrimary)).ShouldBe([("www.shop.example.com", true), ("shop.example.com", false)]);

        // The primary host is removed last, and a host name has no port.
        Has(await Should.ThrowAsync<ApiProblemException>(() => api.RemoveHostAsync(site.Id, "www.shop.example.com", Token)), MultiSiteProblems.PrimaryHostNeeded).ShouldBeTrue();
        Has(await Should.ThrowAsync<ApiProblemException>(() => api.AddHostAsync(site.Id, "shop.example.com:8080", false, Token)), MultiSiteProblems.HostNameWithPort).ShouldBeTrue();
        await api.RemoveHostAsync(site.Id, "shop.example.com", Token);

        await api.DeleteSiteAsync(site.Id, Token);
        (await api.GetSitesAsync(Token)).Select(item => item.Id).ShouldNotContain(site.Id);
        (await client.Client.GetMySitesAsync(Token)).Select(item => item.Id).ShouldNotContain(site.Id);
        Has(await Should.ThrowAsync<ApiProblemException>(() => api.DeleteSiteAsync(DefaultSite.Id, Token)), MultiSiteProblems.DefaultSiteProtected).ShouldBeTrue();
        Has(await Should.ThrowAsync<ApiProblemException>(() => api.CreateSiteAsync("Again", "Not A Slug", "en", Token)), MultiSiteProblems.InvalidSite).ShouldBeTrue();
    }

    // The half's pages are instance-only: a role of a site with sites.manage is refused what they do.
    [Fact]
    public async Task ACallerWhoHoldsSitesManageInASiteOnlyIsRefusedTheCallsOfTheHalf()
    {
        await using var admin = new AdminHost();
        using var super = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        var role = new JsonObject
        {
            ["name"] = "Site manager",
            ["siteId"] = DefaultSite.Id,
            ["grants"] = new JsonArray(new JsonObject { ["permission"] = CorePermissions.SitesManage }),
        };
        await super.Api.SendAsync(new ApiRequest(HttpMethod.Post, "roles") { Body = role }, Token);
        using var client = await admin.SignInAsync("Site manager");

        (await Should.ThrowAsync<ApiProblemException>(() => client.Api.CreateSiteAsync("Shop", "shop", "en", Token))).Problem.Status.ShouldBe(403);
        (await Should.ThrowAsync<ApiProblemException>(() => client.Api.GetHostsAsync(DefaultSite.Id, Token))).Problem.Status.ShouldBe(403);
    }

    private static bool Has(ApiProblemException refused, string code) =>
        refused.Problem.Code == code || refused.Problem.Errors.Any(error => error.Code == code);

    private static JsonObject Body(JsonObject document, string path, string method)
    {
        var schema = document["paths"]![Prefix + path]![method]!["requestBody"]!["content"]!["application/json"]!["schema"]!;
        var reference = schema["$ref"]?.GetValue<string>();
        var resolved = reference is null ? schema : document["components"]!["schemas"]![reference.Split('/')[^1]]!;
        return resolved["properties"]!.AsObject();
    }
}
