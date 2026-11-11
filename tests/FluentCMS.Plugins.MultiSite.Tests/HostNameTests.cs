using System.Net;
using FluentCMS.Content;
using FluentCMS.Core.Sites;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.MultiSite.Tests.MultiSiteTesting;

namespace FluentCMS.Plugins.MultiSite.Tests;

// What a host name is, and the hosts of a site: one namespace for the instance, and one primary host per site.
public sealed class HostNameTests(MultiSiteHost fixture) : IClassFixture<MultiSiteHost>
{
    [Theory]
    [InlineData("www.example.com", "www.example.com")]
    [InlineData("WWW.Example.COM", "www.example.com")]
    [InlineData("localhost", "localhost")]
    [InlineData("a-b.example.co.uk", "a-b.example.co.uk")]
    [InlineData("bücher.example", "xn--bcher-kva.example")]
    [InlineData("xn--bcher-kva.example", "xn--bcher-kva.example")]
    public void AHostNameIsStoredInLowerCaseAndAnInternationalizedNameAsItsALabel(string sent, string stored) =>
        HostNames.Parse(sent, "hostname").ShouldBe(stored);

    [Theory]
    [InlineData(null, "FCMS1660")]
    [InlineData("", "FCMS1660")]
    [InlineData("*.example.com", "FCMS1661")]
    [InlineData("*", "FCMS1661")]
    [InlineData("www.example.com:8080", "FCMS1662")]
    [InlineData("localhost:443", "FCMS1662")]
    [InlineData("https://www.example.com", "FCMS1660")]
    [InlineData("www.example.com/blog", "FCMS1660")]
    [InlineData("www.example.com?x=1", "FCMS1660")]
    [InlineData("user@www.example.com", "FCMS1660")]
    [InlineData(" www.example.com", "FCMS1660")]
    [InlineData("www.example.com.", "FCMS1660")]
    [InlineData(".example.com", "FCMS1660")]
    [InlineData("www..example.com", "FCMS1660")]
    [InlineData("-www.example.com", "FCMS1660")]
    [InlineData("www-.example.com", "FCMS1660")]
    [InlineData("www_1.example.com", "FCMS1660")]
    [InlineData("192.0.2.10", "FCMS1660")]
    [InlineData("[2001:db8::1]", "FCMS1660")]
    [InlineData("2001:db8::1", "FCMS1660")]
    [InlineData("[2001:db8::1]:8080", "FCMS1660")]
    public void WhatIsNotAHostNameIsRefusedWithItsCode(string? sent, string code)
    {
        var error = Should.Throw<ValidationException>(() => HostNames.Parse(sent, "hostname")).Errors.ShouldHaveSingleItem();

        (error.Path, error.Code).ShouldBe(("hostname", code));
    }

    [Fact]
    public void AHostNameHasAtMost253CharactersAndLabelsOfAtMost63()
    {
        var label = new string('a', 63);
        var longest = string.Join('.', label, label, label, new string('a', 61));

        HostNames.Parse(longest, "hostname").Length.ShouldBe(253);
        Should.Throw<ValidationException>(() => HostNames.Parse(longest + "a", "hostname"));
        Should.Throw<ValidationException>(() => HostNames.Parse(new string('a', 64) + ".example.com", "hostname"));
    }

    // The host of a request is looked up in the stored form, and what could not be stored finds nothing.
    [Theory]
    [InlineData("WWW.Example.com", "www.example.com")]
    [InlineData("www.example.com.", "www.example.com")]
    [InlineData("bücher.example", "xn--bcher-kva.example")]
    [InlineData("192.0.2.10", null)]
    [InlineData("[::1]", null)]
    [InlineData("", null)]
    [InlineData(".", null)]
    public void ARequestsHostIsLookedUpByTheStoredForm(string host, string? name) =>
        HostNames.OfRequest(host).ShouldBe(name);

    [Fact]
    public async Task AWildcardAPortAndANameThatIsNoHostNameAreRefusedThroughTheApi()
    {
        using var admin = await AdminAsync(fixture.Host);
        var site = await CreateSiteAsync(admin);

        Code(await AddHostAsync(admin, site, "*.example.com", expected: HttpStatusCode.UnprocessableEntity)).ShouldBe("FCMS1661");
        Code(await AddHostAsync(admin, site, "www.example.com:8443", expected: HttpStatusCode.UnprocessableEntity)).ShouldBe("FCMS1662");
        Code(await AddHostAsync(admin, site, "https://www.example.com/", expected: HttpStatusCode.UnprocessableEntity)).ShouldBe("FCMS1660");
        Code(await SendAsync(admin, HttpMethod.Post, $"{SitesRoute}/{site}/hosts", new { isPrimary = true }, HttpStatusCode.UnprocessableEntity)).ShouldBe("FCMS1660");
        Code(await AddHostAsync(admin, Guid.CreateVersion7(), HostName(), expected: HttpStatusCode.NotFound)).ShouldBe("FCMS2301");

        (await HostsAsync(admin, site)).ShouldBeEmpty();
    }

    [Fact]
    public async Task AHostNameThatASiteHasIsRefusedForAnotherSiteAndForTheSameSite()
    {
        using var admin = await AdminAsync(fixture.Host);
        var first = await CreateSiteAsync(admin);
        var second = await CreateSiteAsync(admin);
        var name = HostName();
        var added = await AddHostAsync(admin, first, name.ToUpperInvariant());

        added!["hostname"]!.GetValue<string>().ShouldBe(name);
        added["siteId"]!.GetValue<string>().ShouldBe(first.ToString());

        var taken = await AddHostAsync(admin, second, name, expected: HttpStatusCode.Conflict);
        Code(taken).ShouldBe("FCMS1663");
        taken!["errors"]!.AsArray().Single()!["path"]!.GetValue<string>().ShouldBe("hostname");
        Code(await AddHostAsync(admin, first, name, expected: HttpStatusCode.Conflict)).ShouldBe("FCMS1663");
        Code(await AddHostAsync(admin, DefaultSite.Id, name, expected: HttpStatusCode.Conflict)).ShouldBe("FCMS1663");

        (await HostsAsync(admin, second)).ShouldBeEmpty();
    }

    // The first host is the primary one whatever the request says; a later one when the request asks, or when it is made
    // primary, in place of the one that was. The primary host stays until another is, and is removed last.
    [Fact]
    public async Task ASiteKeepsExactlyOnePrimaryHostWhileItHasAny()
    {
        using var admin = await AdminAsync(fixture.Host);
        var site = await CreateSiteAsync(admin);
        var (first, second, third) = (HostName("a"), HostName("b"), HostName("c"));

        (await AddHostAsync(admin, site, first, isPrimary: false))!["isPrimary"]!.GetValue<bool>().ShouldBeTrue();
        (await AddHostAsync(admin, site, second))!["isPrimary"]!.GetValue<bool>().ShouldBeFalse();
        (await HostsAsync(admin, site)).ShouldBe([(first, true), (second, false)]);

        (await AddHostAsync(admin, site, third, isPrimary: true))!["isPrimary"]!.GetValue<bool>().ShouldBeTrue();
        (await HostsAsync(admin, site)).ShouldBe([(third, true), (first, false), (second, false)]);

        var made = await SendAsync(admin, HttpMethod.Patch, $"{SitesRoute}/{site}/hosts/{second.ToUpperInvariant()}", new { isPrimary = true });
        made!["isPrimary"]!.GetValue<bool>().ShouldBeTrue();
        (await HostsAsync(admin, site)).ShouldBe([(second, true), (first, false), (third, false)]);

        Code(await SendAsync(admin, HttpMethod.Patch, $"{SitesRoute}/{site}/hosts/{second}", new { isPrimary = false }, HttpStatusCode.Conflict)).ShouldBe("FCMS1665");
        Code(await SendAsync(admin, HttpMethod.Delete, $"{SitesRoute}/{site}/hosts/{second}", expected: HttpStatusCode.Conflict)).ShouldBe("FCMS1665");
        Code(await SendAsync(admin, HttpMethod.Patch, $"{SitesRoute}/{site}/hosts/{HostName()}", new { isPrimary = true }, HttpStatusCode.NotFound)).ShouldBe("FCMS1664");
        Code(await SendAsync(admin, HttpMethod.Delete, $"{SitesRoute}/{site}/hosts/{HostName()}", expected: HttpStatusCode.NotFound)).ShouldBe("FCMS1664");
        (await HostsAsync(admin, site)).ShouldBe([(second, true), (first, false), (third, false)]);

        await SendAsync(admin, HttpMethod.Delete, $"{SitesRoute}/{site}/hosts/{first}", expected: HttpStatusCode.NoContent);
        await SendAsync(admin, HttpMethod.Delete, $"{SitesRoute}/{site}/hosts/{third}", expected: HttpStatusCode.NoContent);
        await SendAsync(admin, HttpMethod.Delete, $"{SitesRoute}/{site}/hosts/{second}", expected: HttpStatusCode.NoContent);
        (await HostsAsync(admin, site)).ShouldBeEmpty();
    }

    // The host names of another site are not this site's to change.
    [Fact]
    public async Task AHostNameIsChangedAndRemovedOnlyThroughItsOwnSite()
    {
        using var admin = await AdminAsync(fixture.Host);
        var first = await CreateSiteAsync(admin);
        var second = await CreateSiteAsync(admin);
        var name = HostName();
        await AddHostAsync(admin, first, name);

        Code(await SendAsync(admin, HttpMethod.Delete, $"{SitesRoute}/{second}/hosts/{name}", expected: HttpStatusCode.NotFound)).ShouldBe("FCMS1664");
        Code(await SendAsync(admin, HttpMethod.Patch, $"{SitesRoute}/{second}/hosts/{name}", new { isPrimary = true }, HttpStatusCode.NotFound)).ShouldBe("FCMS1664");

        (await HostsAsync(admin, first)).ShouldBe([(name, true)]);
    }

    // Each change is a change to its site: site.updated from the event, beside the row that names the host. A change
    // that changes nothing writes no row.
    [Fact]
    public async Task AHostChangeWritesItsActivityRowBesideTheSitesUpdate()
    {
        using var admin = await AdminAsync(fixture.Host);
        var site = await CreateSiteAsync(admin);
        var (first, second) = (HostName("a"), HostName("b"));

        await AddHostAsync(admin, site, first);
        await AddHostAsync(admin, site, second);
        await SendAsync(admin, HttpMethod.Patch, $"{SitesRoute}/{site}/hosts/{second}", new { isPrimary = true });
        await SendAsync(admin, HttpMethod.Patch, $"{SitesRoute}/{site}/hosts/{second}", new { isPrimary = true });
        await SendAsync(admin, HttpMethod.Patch, $"{SitesRoute}/{site}/hosts/{first}", new { });
        await SendAsync(admin, HttpMethod.Delete, $"{SitesRoute}/{site}/hosts/{first}", expected: HttpStatusCode.NoContent);

        var rows = await ActivityAsync(fixture.Host, site);
        rows.Where(row => row.Action.StartsWith("site.host-", StringComparison.Ordinal)).ShouldBe(
            [("site.host-added", first, site), ("site.host-added", second, site), ("site.host-updated", second, site), ("site.host-removed", first, site)],
            ignoreOrder: true);
        rows.Count(row => row.Action == "site.updated").ShouldBe(4);
    }

    private static async Task<List<(string Hostname, bool IsPrimary)>> HostsAsync(HttpClient admin, Guid site)
    {
        var hosts = await SendAsync(admin, HttpMethod.Get, $"{SitesRoute}/{site}/hosts");
        return [.. hosts!["items"]!.AsArray().Select(host => (host!["hostname"]!.GetValue<string>(), host["isPrimary"]!.GetValue<bool>()))];
    }
}
