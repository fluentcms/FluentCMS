using Bunit;
using FluentCMS.Admin.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Components;

public sealed class CmsNavigationListTests : ComponentTestContext
{
    [Fact]
    public void TheNavigationIsALandmarkNamedByItsLabel()
    {
        var navigation = Render<CmsNavigation>(p => p.Add(x => x.Label, "Main"));

        navigation.Find("nav").GetAttribute("aria-label").ShouldBe("Main");
    }

    [Fact]
    public void ANavigationNeedsALabel() =>
        Should.Throw<InvalidOperationException>(() => Render<CmsNavigation>(p => p.Add(x => x.Label, " ")));

    [Fact]
    public void ASectionIsAListNamedByItsVisibleTitle()
    {
        var section = Render<CmsNavigationSection>(p => p.Add(x => x.Title, "Collections"));

        var list = section.Find("ul");
        section.Find($"#{list.GetAttribute("aria-labelledby")}").TextContent.ShouldBe("Collections");
    }

    [Fact]
    public void ASectionWithoutATitleHasNoName()
    {
        var section = Render<CmsNavigationSection>();

        section.Find("ul").HasAttribute("aria-labelledby").ShouldBeFalse();
        section.FindAll(".cms-nav-section-title").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("content/article", "content/article", true)]
    [InlineData("content/article/0193a1b2-0000-7000-8000-000000000001", "content/article", true)]
    [InlineData("content/article?locale=fr", "content/article", true)]
    [InlineData("content/articles", "content/article", false)]
    [InlineData("content/author", "content/article", false)]
    [InlineData("content/article", "", false)]
    [InlineData("", "", true)]
    public void TheLinkOfTheCurrentPageIsMarkedForAssistiveTechnology(string location, string href, bool current)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(location);

        var link = Render<CmsNavigationLink>(p => p.Add(x => x.Href, href).AddChildContent("Articles"));

        var anchor = link.Find("li > a.cms-nav-link");
        anchor.GetAttribute("href").ShouldBe(href);
        anchor.GetAttribute("aria-current").ShouldBe(current ? "page" : null);
    }

    [Fact]
    public void ALinkThatMatchesAllIsCurrentOnItsOwnAddressOnly()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("system/components/more");
        var link = Render<CmsNavigationLink>(p => p.Add(x => x.Href, "system/components").Add(x => x.Match, CmsNavigationMatch.All).AddChildContent("Gallery"));
        link.Find("a").HasAttribute("aria-current").ShouldBeFalse();

        navigation.NavigateTo("system/components");

        link.WaitForAssertion(() => link.Find("a").GetAttribute("aria-current").ShouldBe("page"));
    }

    [Fact]
    public void TheBadgeIsPartOfTheLinksText()
    {
        var link = Render<CmsNavigationLink>(p => p.Add(x => x.Href, "content/tag").Add(x => x.Badge, "Global").AddChildContent("Tags"));

        link.Find("a").TextContent.Trim().ShouldStartWith("Tags");
        link.Find("a .cms-badge").TextContent.ShouldBe("Global");
    }

    [Fact]
    public void TheBreadcrumbIsANamedTrailWhoseLastItemIsTheCurrentPage()
    {
        var breadcrumb = Render<CmsBreadcrumb>(p => p
            .Add(x => x.Label, "Breadcrumb")
            .Add(x => x.Items, [new CmsBreadcrumbItem("Content"), new CmsBreadcrumbItem("Articles", "content/article"), new CmsBreadcrumbItem("New entry")]));

        breadcrumb.Find("nav").GetAttribute("aria-label").ShouldBe("Breadcrumb");
        var items = breadcrumb.FindAll("ol > li");
        items.Select(item => item.TextContent.Trim()).ShouldBe(["Content", "Articles", "New entry"]);
        items[0].QuerySelector("a").ShouldBeNull();
        items[1].QuerySelector("a")!.GetAttribute("href").ShouldBe("content/article");
        items[2].QuerySelector("[aria-current=page]")!.TextContent.ShouldBe("New entry");
        breadcrumb.FindAll("[aria-current]").Count.ShouldBe(1);
    }

    [Fact]
    public void ABreadcrumbWithoutItemsRendersNothing()
    {
        var breadcrumb = Render<CmsBreadcrumb>(p => p.Add(x => x.Label, "Breadcrumb").Add(x => x.Items, []));

        breadcrumb.Markup.Trim().ShouldBeEmpty();
    }
}
