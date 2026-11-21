using System.Reflection;
using Bunit;
using FluentCMS.Admin.Plugins;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.MultiSite.Admin.Tests;

public sealed class MultiSiteSitesPageTests : MultiSiteTestContext
{
    [Theory]
    [InlineData(typeof(MultiSiteSitesPage))]
    [InlineData(typeof(MultiSiteHostsPage))]
    public void EveryPageNeedsSitesManageForTheInstance(Type page)
    {
        var declared = page.GetCustomAttribute<AdminPageAttribute>()!;

        declared.Permission.ShouldBe("sites.manage");
        declared.InstanceOnly.ShouldBeTrue();
    }

    [Fact]
    public void TheSitesAreListedWithTheirSlugAndDefaultLocaleAndTheDefaultSiteHasNoDelete()
    {
        var cut = Render<MultiSiteSitesPage>();

        cut.WaitForAssertion(() => cut.FindAll(".multi-site-table tbody tr").Count.ShouldBe(2));
        var rows = cut.FindAll(".multi-site-table tbody tr");
        rows[0].TextContent.ShouldContain("(default site)");
        rows[0].TextContent.ShouldContain("(the site you are in)");
        rows[0].QuerySelectorAll("fluent-button").ShouldBeEmpty();
        rows[1].QuerySelector("td")!.TextContent.ShouldBe("shop");
        rows[1].QuerySelector("a")!.GetAttribute("href").ShouldBe($"multi-site/sites/{Shop:D}/hosts");
        rows[1].QuerySelector("fluent-button")!.TextContent.Trim().ShouldBe("Delete the site Shop");
    }

    [Fact]
    public void AnAdministratorCreatesASiteAndTheShellReadsItsSitesAgain()
    {
        var cut = Render<MultiSiteSitesPage>();
        cut.WaitForAssertion(() => cut.FindAll(".multi-site-table tbody tr").Count.ShouldBe(2));

        Field(cut, "Name").Input("Blog");
        Field(cut, "Slug").Input("blog");
        Field(cut, "Default locale").Input("fr");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => cut.FindAll(".multi-site-table tbody tr").Count.ShouldBe(3));
        Server.Requests.Single(request => request.Method == HttpMethod.Post).Body!.ToJsonString().ShouldBe("""{"name":"Blog","slug":"blog","defaultLocale":"fr"}""");
        Server.Requests.ShouldAllBe(request => request.NoLocale);
        Admin.SiteReads.ShouldBe(1);
        Announcer.Messages.ShouldContain("The site Blog is created.");
        Field(cut, "Name").GetAttribute("value").ShouldBeNullOrEmpty();
    }

    [Fact]
    public void ACreateTheServerRefusesShowsTheReasonByItsField()
    {
        Server.Refuse = request => request.Method == HttpMethod.Post ? Problem(422, "FCMS2304", "slug") : null;
        var cut = Render<MultiSiteSitesPage>();
        cut.WaitForAssertion(() => cut.FindAll(".multi-site-table tbody tr").Count.ShouldBe(2));

        Field(cut, "Name").Input("Second shop");
        Field(cut, "Slug").Input("shop");
        Field(cut, "Default locale").Input("en");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("A slug is lower case letters and digits joined by hyphens, up to 64 characters, that no other site has."));
        Field(cut, "Slug").GetAttribute("aria-invalid").ShouldBe("true");
        cut.Find(".cms-card .multi-site-message").TextContent.ShouldBe("The site was not created.");
        Admin.SiteReads.ShouldBe(0);
    }

    [Fact]
    public void DeletingASiteAsksFirstAndNamesItAndACancelKeepsIt()
    {
        var cut = Render<MultiSiteSitesPage>();
        cut.WaitForAssertion(() => cut.FindAll(".multi-site-table tbody tr").Count.ShouldBe(2));

        Button(cut, "Delete the site Shop").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Delete the site Shop (shop)?"));
        cut.Markup.ShouldNotContain("You are working in this site.");
        Button(cut, "Cancel").Click();
        cut.WaitForAssertion(() => cut.Find(".multi-site-message").TextContent.ShouldBe("Nothing was deleted."));
        Server.Requests.ShouldNotContain(request => request.Method == HttpMethod.Delete);

        Button(cut, "Delete the site Shop").Click();
        cut.WaitForAssertion(() => HasButton(cut, "Delete the site").ShouldBeTrue());
        Button(cut, "Delete the site").Click();

        cut.WaitForAssertion(() => cut.FindAll(".multi-site-table tbody tr").Count.ShouldBe(1));
        Server.Requests.ShouldContain(request => request.Method == HttpMethod.Delete && request.Path == $"sites/{Shop:D}");
        Admin.Gone.ShouldBe([Shop]);
        Announcer.Messages.ShouldBe(["The site Shop is deleted."]);
    }

    // The server's not-found of the site the path names is told from its refusal of the site the shell sends.
    [Fact]
    public void ADeleteOfASiteThatIsGoneAlreadySaysSoAndTheShellReadsItsSites()
    {
        Server.Refuse = request => request.Method == HttpMethod.Delete ? Problem(422, "FCMS2301", "site") : null;
        var cut = Render<MultiSiteSitesPage>();
        cut.WaitForAssertion(() => cut.FindAll(".multi-site-table tbody tr").Count.ShouldBe(2));

        Button(cut, "Delete the site Shop").Click();
        cut.WaitForAssertion(() => HasButton(cut, "Delete the site").ShouldBeTrue());
        Button(cut, "Delete the site").Click();

        cut.WaitForAssertion(() => Admin.Gone.ShouldBe([Shop]));
        Announcer.Messages.ShouldBe(["The site Shop no longer exists."]);
    }

    [Fact]
    public void ARefusalOfTheSiteTheShellShowsIsNotTakenForTheSiteDeletedAndTheShellLeavesIt()
    {
        Server.Refuse = request => request.Method == HttpMethod.Delete ? Problem(422, "FCMS2301", "X-Cms-Site") : null;
        var cut = Render<MultiSiteSitesPage>();
        cut.WaitForAssertion(() => cut.FindAll(".multi-site-table tbody tr").Count.ShouldBe(2));

        Button(cut, "Delete the site Shop").Click();
        cut.WaitForAssertion(() => HasButton(cut, "Delete the site").ShouldBeTrue());
        Button(cut, "Delete the site").Click();

        cut.WaitForAssertion(() => Admin.Gone.ShouldBe([Main]));
        Announcer.Messages.ShouldBe(["The site the admin shows no longer exists, so nothing was changed. The admin leaves it."]);
        Announcer.Messages.ShouldNotContain("The site Shop no longer exists.");
    }

    // The question says that the shell moves, and the shell reads its sites before the list is read again.
    [Fact]
    public void DeletingTheSiteTheShellShowsSaysSoFirstAndTheShellMovesToTheDefaultSite()
    {
        Admin.Site = FakeAdminContext.SiteOf(Shop);
        Admin.Moves = () => FakeAdminContext.SiteOf(Main);
        var cut = Render<MultiSiteSitesPage>();
        cut.WaitForAssertion(() => cut.FindAll(".multi-site-table tbody tr").Count.ShouldBe(2));

        Button(cut, "Delete the site Shop").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("You are working in this site. Once it is deleted, the admin moves to the default site."));
        Button(cut, "Delete the site").Click();

        cut.WaitForAssertion(() => Announcer.Messages.ShouldBe(["The site Shop is deleted.", "The admin now shows the site Main."]));
        Admin.Site!.Id.ShouldBe(Main);
        var delete = Server.Requests.FindIndex(request => request.Method == HttpMethod.Delete);
        Server.Requests.FindLastIndex(request => request.Method == HttpMethod.Get && request.Path == "sites").ShouldBeGreaterThan(delete);
    }

    [Fact]
    public void ADeleteTheServerRefusesForContentKeepsTheQuestionOpenWithTheReason()
    {
        Server.Refuse = request => request.Method == HttpMethod.Delete ? Problem(422, "FCMS2303", "site") : null;
        var cut = Render<MultiSiteSitesPage>();
        cut.WaitForAssertion(() => cut.FindAll(".multi-site-table tbody tr").Count.ShouldBe(2));

        Button(cut, "Delete the site Shop").Click();
        cut.WaitForAssertion(() => HasButton(cut, "Delete the site").ShouldBeTrue());
        Button(cut, "Delete the site").Click();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.ShouldBe("The site Shop still has content, so it is kept. Delete its entries first."));
        cut.FindAll(".multi-site-table tbody tr").Count.ShouldBe(2);
        Admin.SiteReads.ShouldBe(0);
    }
}
