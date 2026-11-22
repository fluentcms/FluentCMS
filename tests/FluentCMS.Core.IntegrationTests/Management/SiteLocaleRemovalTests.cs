using System.Net;
using FluentCMS.Core.Sites;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Management;

// A locale that another locale of the site names as a fallback is not removed, and a removal changes no chain (#3517).
// Each case has locales of its own, so the cases do not depend on their order.
public sealed class SiteLocaleRemovalTests(AdminFixture fixture) : IClassFixture<AdminFixture>
{
    private static readonly string Site = DefaultSite.Id.ToString("D");

    [Fact]
    public async Task ALocaleNamedAsAFallbackIsNotRemovedAndTheRefusalNamesWhoNamesIt()
    {
        await AddAsync("""{ "code": "fr" }""");
        await AddAsync("""{ "code": "fr-CA", "fallbacks": ["fr", "en"] }""");
        await AddAsync("""{ "code": "fr-CH", "enabled": false, "fallbacks": ["FR"] }""");

        var refused = await fixture.DeleteAsync($"sites/{Site}/locales/fr");

        refused.Status.ShouldBe(HttpStatusCode.Conflict, refused.Body?.ToJsonString());
        refused.Json["code"]!.GetValue<string>().ShouldBe("FCMS3143");
        var errors = refused.Json["errors"]!.AsArray();
        errors.Select(error => error!["path"]!.GetValue<string>()).ShouldBe(["locales.fr-CA.fallbacks", "locales.fr-CH.fallbacks"]);
        errors.Select(error => error!["code"]!.GetValue<string>()).ShouldAllBe(code => code == "FCMS3143");
        errors[0]!["message"]!.GetValue<string>().ShouldBe("'fr-CA' names 'fr' as a fallback; take 'fr' out of the fallbacks of 'fr-CA' first.");
        Codes(await fixture.GetAsync($"sites/{Site}/locales")).ShouldContain("fr");
        (await FallbacksAsync("fr-CA")).ShouldBe(["fr", "en"]);
    }

    [Fact]
    public async Task ALocaleIsRemovedOnceNoChainNamesItAndTheOtherChainsStayAsTheyWere()
    {
        await AddAsync("""{ "code": "it" }""");
        await AddAsync("""{ "code": "it-CH", "fallbacks": ["it", "en"] }""");
        (await fixture.DeleteAsync($"sites/{Site}/locales/it")).Json["code"]!.GetValue<string>().ShouldBe("FCMS3143");

        (await fixture.PatchAsync($"sites/{Site}/locales/it-CH", """{ "fallbacks": ["en"] }""")).Status.ShouldBe(HttpStatusCode.OK);
        var removed = await fixture.DeleteAsync($"sites/{Site}/locales/it");

        removed.Status.ShouldBe(HttpStatusCode.NoContent, removed.Body?.ToJsonString());
        Codes(await fixture.GetAsync($"sites/{Site}/locales")).ShouldNotContain("it");
        (await FallbacksAsync("it-CH")).ShouldBe(["en"]);
    }

    // The implicit chain is worked out on each read from the locales the site has, so it names no locale.
    [Fact]
    public async Task ALocaleNoChainNamesIsRemoved()
    {
        await AddAsync("""{ "code": "nl" }""");
        await AddAsync("""{ "code": "nl-BE" }""");
        await AddAsync("""{ "code": "pt", "fallbacks": ["en"] }""");

        var removed = await fixture.DeleteAsync($"sites/{Site}/locales/nl");

        removed.Status.ShouldBe(HttpStatusCode.NoContent, removed.Body?.ToJsonString());
        Codes(await fixture.GetAsync($"sites/{Site}/locales")).ShouldNotContain("nl");
        (await FallbacksAsync("nl-BE")).ShouldBeNull();
        (await FallbacksAsync("pt")).ShouldBe(["en"]);
    }

    private async Task AddAsync(string json)
    {
        var added = await fixture.PostAsync($"sites/{Site}/locales", json);
        added.Status.ShouldBe(HttpStatusCode.Created, added.Body?.ToJsonString());
    }

    private async Task<List<string>?> FallbacksAsync(string code)
    {
        var locale = (await fixture.GetAsync($"sites/{Site}/locales")).Json["items"]!.AsArray().Single(item => item!["code"]!.GetValue<string>() == code)!;
        return locale["fallbacks"]?.AsArray().Select(item => item!.GetValue<string>()).ToList();
    }

    private static List<string> Codes(ApiResponse response) =>
        [.. response.Json["items"]!.AsArray().Select(item => item!["code"]!.GetValue<string>())];
}
