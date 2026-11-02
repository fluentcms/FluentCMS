using FluentCMS.Content;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Data.Tests.Sqlite;
using FluentCMS.Localization;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Sites;

public sealed class ResolverTests : IDisposable
{
    private readonly SqliteFileDatabase _database = new();
    private readonly HostTable _hosts = new();
    private readonly ServiceProvider _services;
    private readonly SiteId _docs;
    private readonly SiteId _blog;
    private readonly SiteId _shop;

    public ResolverTests()
    {
        _services = _database.CreateServices(SampleSchema.FromClasses().Build(), cms => cms.Services.AddSingleton<ISiteHostResolver>(_hosts));
        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<SystemDbContext>().Database.Migrate();

        var manager = scope.ServiceProvider.GetRequiredService<SiteManager>();
        _docs = new SiteId(manager.CreateAsync("Docs", "docs", "fr", Token).GetAwaiter().GetResult().Id);
        _blog = new SiteId(manager.CreateAsync("Blog", "blog", "en", Token).GetAwaiter().GetResult().Id);
        _shop = new SiteId(manager.CreateAsync("Shop", "shop", "en", Token).GetAwaiter().GetResult().Id);
        _hosts.Sites["shop.example.com"] = _shop;

        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        context.AddRange(
            new SiteLocale { SiteId = _docs.Value, Code = "en", Position = 1 },
            new SiteLocale { SiteId = _docs.Value, Code = "fr-CA", Position = 2 },
            new SiteLocale { SiteId = _docs.Value, Code = "de", Position = 3, Enabled = false });
        context.SaveChanges();
    }

    public void Dispose()
    {
        _services.Dispose();
        _database.Dispose();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<SiteId> SiteAsync(SiteRequest request)
    {
        using var scope = _services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISiteResolver>().ResolveAsync(request, Token);
    }

    private async Task<string> LocaleAsync(SiteId site, string? parameter = null, string? header = null)
    {
        using var scope = _services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ILocaleResolver>().ResolveAsync(new LocaleRequest(site) { Parameter = parameter, Header = header }, Token);
    }

    [Fact]
    public async Task WithNothingSaidTheDefaultSiteIsResolved() =>
        (await SiteAsync(new SiteRequest())).ShouldBe(new SiteId(DefaultSite.Id));

    [Fact]
    public async Task TheApiKeyBindingWinsOverEverythingElse() =>
        (await SiteAsync(new SiteRequest { ApiKeySite = _docs, Header = "blog", Parameter = "shop", Host = "shop.example.com" })).ShouldBe(_docs);

    [Fact]
    public async Task TheHeaderWinsOverTheParameterAndTheHost() =>
        (await SiteAsync(new SiteRequest { Header = "blog", Parameter = "docs", Host = "shop.example.com" })).ShouldBe(_blog);

    [Fact]
    public async Task TheParameterWinsOverTheHost() =>
        (await SiteAsync(new SiteRequest { Parameter = "docs", Host = "shop.example.com" })).ShouldBe(_docs);

    [Fact]
    public async Task TheHostResolverOfAPluginIsAskedBeforeTheDefault()
    {
        (await SiteAsync(new SiteRequest { Host = "shop.example.com" })).ShouldBe(_shop);
        (await SiteAsync(new SiteRequest { Host = "unknown.example.com" })).ShouldBe(new SiteId(DefaultSite.Id));
    }

    [Fact]
    public async Task ASiteIsNamedByIdOrBySlugInAnyCase()
    {
        (await SiteAsync(new SiteRequest { Header = _blog.ToString() })).ShouldBe(_blog);
        (await SiteAsync(new SiteRequest { Parameter = "BLOG" })).ShouldBe(_blog);
        (await SiteAsync(new SiteRequest { Header = " " })).ShouldBe(new SiteId(DefaultSite.Id));
    }

    [Fact]
    public async Task ASiteThatDoesNotExistIsRejectedWithAProblemCode()
    {
        var byHeader = (await Should.ThrowAsync<ValidationException>(() => SiteAsync(new SiteRequest { Header = "nothing" }))).Errors.Single();
        (byHeader.Code, byHeader.Path).ShouldBe(("FCMS2301", "X-Cms-Site"));

        var byParameter = (await Should.ThrowAsync<ValidationException>(() => SiteAsync(new SiteRequest { Parameter = Guid.CreateVersion7().ToString() }))).Errors.Single();
        (byParameter.Code, byParameter.Path).ShouldBe(("FCMS2301", "site"));

        (await Should.ThrowAsync<ValidationException>(() => SiteAsync(new SiteRequest { ApiKeySite = new SiteId(Guid.CreateVersion7()) }))).Errors.Single().Code.ShouldBe("FCMS2301");
    }

    [Fact]
    public async Task TheLocaleParameterWinsOverTheHeaderAndTheHeaderOverTheSiteDefault()
    {
        (await LocaleAsync(_docs, parameter: "en", header: "fr-CA")).ShouldBe("en");
        (await LocaleAsync(_docs, header: "fr-ca")).ShouldBe("fr-CA");
        (await LocaleAsync(_docs)).ShouldBe("fr");
        (await LocaleAsync(_blog, parameter: " ")).ShouldBe("en");
    }

    [Fact]
    public async Task AllIsPassedThroughForTheReadOfEveryLocale() =>
        (await LocaleAsync(_docs, parameter: "ALL")).ShouldBe("all");

    [Theory]
    [InlineData("en_US", "FCMS2311")]
    [InlineData("fr-", "FCMS2311")]
    [InlineData("ja", "FCMS2312")]
    [InlineData("de", "FCMS2312")]
    public async Task AMalformedUnlistedOrDisabledLocaleIsRejectedWithAProblemCode(string locale, string code)
    {
        var error = (await Should.ThrowAsync<ValidationException>(() => LocaleAsync(_docs, parameter: locale))).Errors.Single();

        (error.Code, error.Path).ShouldBe((code, "locale"));
    }

    [Fact]
    public async Task AHeaderLocaleIsValidatedLikeAParameter()
    {
        (await Should.ThrowAsync<ValidationException>(() => LocaleAsync(_docs, header: "ja"))).Errors.Single().Code.ShouldBe("FCMS2312");
    }

    [Fact]
    public async Task ALocaleIsCheckedAgainstTheListOfTheSiteItIsReadFrom()
    {
        (await LocaleAsync(_docs, parameter: "fr-CA")).ShouldBe("fr-CA");
        (await Should.ThrowAsync<ValidationException>(() => LocaleAsync(_blog, parameter: "fr-CA"))).Errors.Single().Code.ShouldBe("FCMS2312");
    }

    private sealed class HostTable : ISiteHostResolver
    {
        public Dictionary<string, SiteId> Sites { get; } = [];

        public ValueTask<SiteId?> ResolveAsync(string host, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Sites.TryGetValue(host, out var site) ? (SiteId?)site : null);
    }
}
