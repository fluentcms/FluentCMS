using System.Net;
using Bunit;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Administration.Sites;

// The sites page: the site the shell shows, with its name and default locale, and its locales, which are added, changed,
// moved and removed, after which the shell reads the user's sites again for the switchers.
public sealed class SitesPageTests : SitesTestContext
{
    private static readonly Guid Shop = new("0193a1b2-0000-7000-8000-00000000a002");

    public SitesPageTests()
    {
        UseNeutralResources();
        GrantArea("sites.manage", instanceOnly: false);
    }

    [Fact]
    public void ThePageShowsTheSiteOfTheShellAndItsLocalesWithTheirPositionStateAndFallbacks()
    {
        AddLocale("fr-CA", 1, ["fr"]);
        AddLocale("fr", 2, []);
        AddLocale("de", 3, enabled: false);

        var app = Load("sites");

        app.WaitForAssertion(() => Rows(app).Length.ShouldBe(4));
        Rows(app).ShouldBe(
        [
            ["en", "0", "Default", "The implicit chain"],
            ["fr-CA", "1", "Enabled", "fr"],
            ["fr", "2", "Enabled", "None"],
            ["de", "3", "Disabled", "The implicit chain"],
        ]);
        var site = Section(app, "Site");
        Field(site, "Name").GetAttribute("value").ShouldBe("Main");
        site.TextContent.ShouldContain("Slug: main");
        Field(site, "Default locale").QuerySelectorAll("option").Select(option => option.GetAttribute("value")).ShouldBe(["en", "fr-CA", "fr"]);

        // The default locale is neither disabled nor removed.
        var locales = Section(app, "Locales");
        HasButton(locales, "Disable the locale en").ShouldBeFalse();
        HasButton(locales, "Remove the locale en").ShouldBeFalse();
        HasButton(locales, "Remove the locale de").ShouldBeTrue();
        Management.Calls.Where(call => call.Path.StartsWith("sites/", StringComparison.Ordinal)).Select(call => call.Path)
            .ShouldBe([$"sites/{MainSite:D}", $"sites/{MainSite:D}/locales"], ignoreOrder: true);
    }

    [Fact]
    public void AnAdministratorAddsALocaleWithAFallbackDisablesItAndRemovesItAndTheLocalesOfTheShellFollowEachStep()
    {
        var app = Load("sites");
        app.WaitForAssertion(() => Rows(app).Length.ShouldBe(1));
        var add = Section(app, "Add a locale");

        Field(add, "Locale").Input("fr");
        Field(add, "Fallbacks").Input("en");
        add.QuerySelector("form")!.Submit();

        app.WaitForAssertion(() => Context.ContentLocales.ShouldBe(["en", "fr"]));
        BodyOf(HttpMethod.Post, $"sites/{MainSite:D}/locales").ShouldBe("""{"code":"fr","enabled":true,"fallbacks":["en"]}""");
        Rows(app)[1].ShouldBe(["fr", "1", "Enabled", "en"]);
        Message(Section(app, "Add a locale")).ShouldBe("fr is added.");

        Button(Section(app, "Locales"), "Disable the locale fr").Click();
        app.WaitForAssertion(() => Context.ContentLocales.ShouldBe(["en"]));
        BodyOf(HttpMethod.Patch, $"sites/{MainSite:D}/locales/fr").ShouldBe("""{"enabled":false}""");
        Rows(app)[1][2].ShouldBe("Disabled");

        Button(Section(app, "Locales"), "Remove the locale fr").Click();
        app.WaitForAssertion(() => Rows(app).Length.ShouldBe(1));
        Context.Site!.Locales.Select(locale => locale.Code).ShouldBe(["en"]);
        Message(Section(app, "Locales")).ShouldBe("fr is removed.");
        Management.Count("me/sites").ShouldBe(4);
    }

    [Fact]
    public void TheDefaultLocaleAndTheNameAreChangedAndTheSiteSwitcherFollows()
    {
        Management.AddSite(Shop, "Shop", "shop");
        AddLocale("fr", 1);
        var app = Load("sites");
        app.WaitForAssertion(() => Rows(app).Length.ShouldBe(2));
        var site = Section(app, "Site");

        Field(site, "Name").Input("Main street");
        Field(site, "Default locale").Change("fr");
        site.QuerySelector("form")!.Submit();

        app.WaitForAssertion(() => Context.Site!.DefaultLocale.ShouldBe("fr"));
        BodyOf(HttpMethod.Patch, $"sites/{MainSite:D}").ShouldBe("""{"name":"Main street","defaultLocale":"fr"}""");
        Message(Section(app, "Site")).ShouldBe("The site is saved.");
        Context.ContentLocale.ShouldBe("fr");
        app.Find(".site-switcher select").QuerySelectorAll("option").Select(option => option.TextContent).ShouldBe(["Main street", "Shop"]);
        Rows(app).Select(row => row[2]).ShouldBe(["Enabled", "Default"]);
    }

    [Fact]
    public void ADefaultLocaleTheServerRefusesShowsItsReason()
    {
        AddLocale("fr", 1);
        Management.Answer = (method, path) => method == HttpMethod.Patch && path == $"sites/{MainSite:D}" ? Problem(HttpStatusCode.UnprocessableEntity, "FCMS2312", "defaultLocale") : null;
        var app = Load("sites");
        app.WaitForAssertion(() => Rows(app).Length.ShouldBe(2));
        var site = Section(app, "Site");

        Field(site, "Default locale").Change("fr");
        site.QuerySelector("form")!.Submit();

        app.WaitForAssertion(() => Message(Section(app, "Site")).ShouldBe("The site was not saved."));
        site = Section(app, "Site");
        site.TextContent.ShouldContain("The default locale must be one the site has enabled.");
        Field(site, "Default locale").GetAttribute("aria-invalid").ShouldBe("true");
        Context.Site!.DefaultLocale.ShouldBe("en");
    }

    [Fact]
    public void ALocaleThatEntriesAreInIsKeptAndThePageSaysWhy()
    {
        AddLocale("fr", 1);
        WithEntries.Add("fr");
        var app = Load("sites");
        app.WaitForAssertion(() => Rows(app).Length.ShouldBe(2));

        Button(Section(app, "Locales"), "Remove the locale fr").Click();

        app.WaitForAssertion(() => Message(Section(app, "Locales")).ShouldBe("The site has entries in fr, so it stays. Disable it instead, or delete its entries first."));
        Rows(app).Length.ShouldBe(2);
        Management.Count("me/sites").ShouldBe(1);
    }

    [Fact]
    public void MovingALocaleUpGivesEachLocaleItsPlace()
    {
        AddLocale("fr", 1);
        AddLocale("de", 2);
        var app = Load("sites");
        app.WaitForAssertion(() => Rows(app).Length.ShouldBe(3));

        Button(Section(app, "Locales"), "Move up the locale de").Click();

        app.WaitForAssertion(() => Rows(app).Select(row => row[0]).ShouldBe(["en", "de", "fr"]));
        BodyOf(HttpMethod.Patch, $"sites/{MainSite:D}/locales/de").ShouldBe("""{"position":1}""");
        BodyOf(HttpMethod.Patch, $"sites/{MainSite:D}/locales/fr").ShouldBe("""{"position":2}""");
        Context.Site!.Locales.Select(locale => locale.Code).ShouldBe(["en", "de", "fr"]);
        HasButton(Section(app, "Locales"), "Move up the locale en").ShouldBeFalse();
        HasButton(Section(app, "Locales"), "Move down the locale fr").ShouldBeFalse();
    }

    [Fact]
    public void TheFallbacksOfALocaleAreSetInOrderAndAnEmptyTextGoesBackToTheImplicitChain()
    {
        AddLocale("fr", 1);
        AddLocale("fr-CA", 2);
        var app = Load("sites");
        app.WaitForAssertion(() => Rows(app).Length.ShouldBe(3));

        Button(Section(app, "Locales"), "Change the fallbacks of fr-CA").Click();
        var locales = Section(app, "Locales");
        Field(locales, "Fallbacks of fr-CA").Input("fr, en");
        locales.QuerySelector("form")!.Submit();

        app.WaitForAssertion(() => Rows(app)[2][3].ShouldBe("fr, en"));
        BodyOf(HttpMethod.Patch, $"sites/{MainSite:D}/locales/fr-CA").ShouldBe("""{"fallbacks":["fr","en"]}""");
        Message(Section(app, "Locales")).ShouldBe("The fallbacks of fr-CA are saved.");

        Button(Section(app, "Locales"), "Change the fallbacks of fr-CA").Click();
        locales = Section(app, "Locales");
        Field(locales, "Fallbacks of fr-CA").GetAttribute("value").ShouldBe("fr, en");
        Field(locales, "Fallbacks of fr-CA").Input("");
        locales.QuerySelector("form")!.Submit();

        app.WaitForAssertion(() => Rows(app)[2][3].ShouldBe("The implicit chain"));
        BodyOf(HttpMethod.Patch, $"sites/{MainSite:D}/locales/fr-CA").ShouldBe("""{"fallbacks":null}""");
    }

    [Fact]
    public void AFallbackTheServerRefusesShowsItsReasonByTheField()
    {
        AddLocale("fr", 1);
        var app = Load("sites");
        app.WaitForAssertion(() => Rows(app).Length.ShouldBe(2));
        var add = Section(app, "Add a locale");

        Field(add, "Locale").Input("de");
        Field(add, "Fallbacks").Input("it");
        add.QuerySelector("form")!.Submit();

        app.WaitForAssertion(() => Message(Section(app, "Add a locale")).ShouldBe("The locale was not added."));
        add = Section(app, "Add a locale");
        add.TextContent.ShouldContain("Each fallback is another locale of the site, named once.");
        Field(add, "Fallbacks").GetAttribute("aria-invalid").ShouldBe("true");
        Context.ContentLocales.ShouldBe(["en", "fr"]);
    }

    // The shell's content locale is one the site no longer has enabled: it goes back to the default.
    [Fact]
    public async Task DisablingTheContentLocaleOfTheShellTakesItBackToTheDefault()
    {
        AddLocale("fr", 1);
        var app = Load("sites");
        app.WaitForAssertion(() => Rows(app).Length.ShouldBe(2));
        await app.InvokeAsync(() => Context.UseLocaleAsync("fr"));

        Button(Section(app, "Locales"), "Disable the locale fr").Click();

        app.WaitForAssertion(() => Context.ContentLocale.ShouldBe("en"));
        app.WaitForAssertion(() => Context.AccessCurrent.ShouldBeTrue());
        Heading(app).ShouldBe("Sites and locales");
    }
}
