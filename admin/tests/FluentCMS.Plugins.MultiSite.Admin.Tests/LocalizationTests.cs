using Bunit;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.MultiSite.Admin.Tests;

// The sweep of spec 7: every page of the half under both pseudo-cultures, the sites and host names marked as data.
public sealed class LocalizationTests : MultiSiteTestContext
{
    public static TheoryData<string, string> Pages() => PseudoSweep.Pages(typeof(MultiSiteSitesPage).Assembly);

    [Theory]
    [MemberData(nameof(Pages))]
    public void EveryTextOfEveryPageComesFromTheResources(string culture, string page)
    {
        this.UsePseudoCulture(culture);
        string Data(string text) => PseudoCulture.Transform(text, culture);
        foreach (var site in Server.Sites)
        {
            site["name"] = Data(site["name"]!.GetValue<string>());
            site["slug"] = Data(site["slug"]!.GetValue<string>());
            site["defaultLocale"] = Data("en");
        }

        Server.AddHost(Shop, Data("shop.example.com"));
        Server.AddHost(Shop, Data("www.shop.example.com"));

        IRenderedComponent<Microsoft.AspNetCore.Components.IComponent> cut = page switch
        {
            nameof(MultiSiteSitesPage) => Render<MultiSiteSitesPage>(),
            nameof(MultiSiteHostsPage) => Render<MultiSiteHostsPage>(parameters => parameters.Add(page => page.Id, Shop)),
            _ => throw PseudoSweep.NoSweep(page),
        };

        cut.WaitForAssertion(() => cut.FindAll(page == nameof(MultiSiteSitesPage) ? "table tbody tr" : "ul.multi-site-hosts li").Count.ShouldBe(2));
        cut.ShouldBeLocalized();
    }
}
