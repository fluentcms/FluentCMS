using Bunit;
using FluentCMS.Admin.Administration.Sites;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Administration.Sites;

// The check of spec 3.1 on the pages of the area: under a pseudo-culture no text is left untranslated. The area's issue
// adds each of its routes here, with the answers of the fake Management API that its page reads, so that the page is
// checked with its data and not only in its error state.
public sealed class SitesLocalizationTests : SitesTestContext
{
    [Theory]
    [InlineData(PseudoCulture.LeftToRight)]
    [InlineData(PseudoCulture.RightToLeft)]
    public void EveryTextOfTheSitesPageComesFromTheResources(string culture)
    {
        var (english, french, german, canadian) = MarkData(culture);

        var app = LoadLocalized(culture, "sites", shown => Rows(shown).Length.ShouldBe(4));

        // The heading of an area's first page is {Area}.Page.Heading of its resources, which the end-to-end tests find it by.
        Heading(app).ShouldBe(Localized<SitesResources>("Sites.Page.Heading"));
        Rows(app).Select(row => row[0]).ShouldBe([english, canadian, french, german]);

        // The form of a locale's fallbacks, and a refusal named by its code.
        var locales = Section(app, Localized<SitesResources>("Sites.Locales.Heading"));
        Button(locales, Localized<SitesResources>("Sites.Locales.EditFallbacks") + Localized<SitesResources>("Sites.Locales.OfWhich", french)).Click();
        app.WaitForAssertion(() => Section(app, Localized<SitesResources>("Sites.Locales.Heading")).QuerySelector("form").ShouldNotBeNull());
        PseudoCulture.Unlocalized(app.Nodes).ShouldBeEmpty();

        WithEntries.Add(german);
        Button(Section(app, Localized<SitesResources>("Sites.Locales.Heading")), Localized<SitesResources>("Sites.Locales.Remove") + Localized<SitesResources>("Sites.Locales.Which", german)).Click();
        app.WaitForAssertion(() => Message(Section(app, Localized<SitesResources>("Sites.Locales.Heading"))).ShouldBe(Localized<SitesResources>("Sites.Locales.HasEntries", german)));
        PseudoCulture.Unlocalized(app.Nodes).ShouldBeEmpty();
    }

    // Data the fake serves is marked as a resource is, so that only a text of the page itself can fail the check: the site's
    // name and slug, and the codes of its locales, which the page shows as they are.
    private (string English, string French, string German, string Canadian) MarkData(string culture)
    {
        var rightToLeft = culture == PseudoCulture.RightToLeft;
        string Mark(string value) => PseudoCulture.Transform(value, rightToLeft);

        var (english, french, german, canadian) = (Mark("en"), Mark("fr"), Mark("de"), Mark("fr-CA"));
        Locales.Clear();
        Locales.Add(Locale(english, 0));
        AddLocale(canadian, 1, [french, english]);
        AddLocale(french, 2, []);
        AddLocale(german, 3, enabled: false);
        Site["name"] = Mark("Main");
        Site["slug"] = Mark("main");
        Site["defaultLocale"] = english;
        return (english, french, german, canadian);
    }
}
