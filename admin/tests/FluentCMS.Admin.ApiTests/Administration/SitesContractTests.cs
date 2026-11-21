using System.Text.Json.Nodes;
using FluentCMS.Admin.Administration.Sites;
using FluentCMS.Admin.Api;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Sites;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.ApiTests.Administration;

// The records of the sites area held to the reference host's OpenAPI document (spec 7), and its calls on the test host: the
// locales of a site added, changed, moved and removed, the default locale changed, and what the server refuses by its code.
public sealed class SitesContractTests
{
    private const string Prefix = "/api/management/v1/";

    private static CancellationToken Token => AdminHost.Token;

    [Fact]
    public async Task TheRecordsOfTheSitesAreaMatchTheOpenApiDocument()
    {
        var document = await OpenApiDocument.ReadAsync();
        var paths = document["paths"]!;
        var sites = SitesJsonContext.Default;
        List<string> gaps = [];

        void Check(System.Text.Json.Serialization.Metadata.JsonTypeInfo record, string path, string method, string status = "200") =>
            OpenApiContractTests.Check(record, OpenApiContractTests.Answer(paths, Prefix + path, method, status), $"{path} {method}", document, gaps);

        Check(sites.SiteRecord, "sites/{id}", "get");
        Check(sites.SiteRecord, "sites/{id}", "patch");
        Check(sites.ItemListSiteLocaleRecord, "sites/{id}/locales", "get");
        Check(sites.SiteLocaleRecord, "sites/{id}/locales", "post", "201");
        Check(sites.SiteLocaleRecord, "sites/{id}/locales/{code}", "patch");

        gaps.ShouldBeEmpty();
        paths[Prefix + "sites/{id}/locales/{code}"]?["delete"]?["responses"]?["204"].ShouldNotBeNull();
        Body(document, "sites/{id}", "patch").Select(member => member.Key).ShouldBe(["name", "defaultLocale"], ignoreOrder: true);
        Body(document, "sites/{id}/locales", "post").Select(member => member.Key).ShouldBe(["code", "position", "enabled", "fallbacks"], ignoreOrder: true);
        Body(document, "sites/{id}/locales/{code}", "patch").Select(member => member.Key).ShouldBe(["position", "enabled", "fallbacks"], ignoreOrder: true);
    }

    [Fact]
    public async Task ALocaleIsAddedWithAFallbackMovedDisabledAndRemovedAndTheSitesOfTheUserFollow()
    {
        await using var admin = new AdminHost();
        using var client = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        var api = client.Api;
        var site = DefaultSite.Id;

        var added = await api.AddLocaleAsync(site, "it", enabled: true, ["fr", "en"], Token);
        (added.Code, added.Enabled).ShouldBe(("it", true));
        added.Fallbacks.ShouldBe(["fr", "en"]);
        (await client.Client.GetMySitesAsync(Token)).Single(item => item.Id == site).Locales.Select(locale => locale.Code).ShouldContain("it");

        var dutch = await api.AddLocaleAsync(site, "nl", enabled: true, null, Token);
        dutch.Fallbacks.ShouldBeNull();
        await api.MoveLocaleAsync(site, "nl", added.Position, Token);
        await api.MoveLocaleAsync(site, "it", dutch.Position, Token);
        var order = (await api.GetLocalesAsync(site, Token)).Select(locale => locale.Code).ToList();
        order.IndexOf("nl").ShouldBeLessThan(order.IndexOf("it"));

        (await api.SetFallbacksAsync(site, "nl", ["it"], Token)).Fallbacks.ShouldBe(["it"]);
        (await api.SetFallbacksAsync(site, "nl", null, Token)).Fallbacks.ShouldBeNull();
        (await api.EnableLocaleAsync(site, "it", enabled: false, Token)).Enabled.ShouldBeFalse();
        (await client.Client.GetMySitesAsync(Token)).Single(item => item.Id == site).Locales.Single(locale => locale.Code == "it").Enabled.ShouldBeFalse();

        // A default locale the site has not enabled, a fallback that is not a locale of the site, a locale the site has, the
        // default disabled, and a locale that entries are in removed.
        var current = await api.GetSiteAsync(site, Token);
        Code(await Should.ThrowAsync<ApiProblemException>(() => api.ChangeSiteAsync(site, null, "it", Token)), "defaultLocale").ShouldBe(SiteProblems.LocaleNotAllowed);
        Code(await Should.ThrowAsync<ApiProblemException>(() => api.AddLocaleAsync(site, "pt", true, ["xx"], Token)), "fallbacks").ShouldBe(SiteProblems.LocaleNotAllowed);
        Code(await Should.ThrowAsync<ApiProblemException>(() => api.AddLocaleAsync(site, "it", true, null, Token)), "code").ShouldBe(SiteProblems.LocaleExists);
        Code(await Should.ThrowAsync<ApiProblemException>(() => api.EnableLocaleAsync(site, current.DefaultLocale, false, Token)), "enabled").ShouldBe(SiteProblems.LocaleInUse);
        Code(await Should.ThrowAsync<ApiProblemException>(() => api.RemoveLocaleAsync(site, "fr", Token)), "code").ShouldBe(SiteProblems.LocaleInUse);

        await api.RemoveLocaleAsync(site, "it", Token);
        await api.RemoveLocaleAsync(site, "nl", Token);
        (await api.GetLocalesAsync(site, Token)).Select(locale => locale.Code).ShouldNotContain("it");
        (await api.ChangeSiteAsync(site, "Main street", null, Token)).Name.ShouldBe("Main street");
    }
    // The area is for a grant of the site as well: a role of the site with sites.manage changes the site's locales.
    [Fact]
    public async Task ACallerWhoHoldsSitesManageInTheSiteChangesItsLocales()
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

        (await client.Api.GetSiteAsync(DefaultSite.Id, Token)).Id.ShouldBe(DefaultSite.Id);
        (await client.Api.AddLocaleAsync(DefaultSite.Id, "nl", enabled: true, null, Token)).Code.ShouldBe("nl");
    }

    private static string? Code(ApiProblemException refused, string path) => refused.Problem.Errors.SingleOrDefault(error => error.Path == path)?.Code;

    private static JsonObject Body(JsonObject document, string path, string method)
    {
        var schema = document["paths"]![Prefix + path]![method]!["requestBody"]!["content"]!["application/json"]!["schema"]!;
        var reference = schema["$ref"]?.GetValue<string>();
        var resolved = reference is null ? schema : document["components"]!["schemas"]![reference.Split('/')[^1]]!;
        return resolved["properties"]!.AsObject();
    }
}
