using Bunit;
using FluentCMS.Admin.Localization;
using FluentCMS.Admin.Shell;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Administration;

// The pages of the administration assembly are not among the shell's own, so the check of spec 3.1 runs on them here:
// under a pseudo-culture no text of a page, of its sidebar entry or of its breadcrumbs is left untranslated.
public sealed class AdministrationLocalizationTests : AdministrationTestContext
{
    public static TheoryData<string, string> Routes()
    {
        var data = new TheoryData<string, string>();
        foreach (var culture in new[] { PseudoCulture.LeftToRight, PseudoCulture.RightToLeft })
        {
            foreach (var area in AdministrationAreas.All)
            {
                data.Add(culture, area.Route);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public void EveryTextOfEveryAdministrationPageComesFromTheResources(string culture, string route)
    {
        UseInterfaceCulture(culture);
        string[] everything = [.. AdministrationAreas.All.Select(area => area.Permission.Replace("*", "media", StringComparison.Ordinal))];
        Grant([], everything);

        var app = Load(route);

        app.WaitForAssertion(() => Loader.Loads.ShouldBe(1));
        app.WaitForAssertion(() => app.FindAll("main h1").ShouldHaveSingleItem().TextContent.ShouldNotBe(Text("Shell.NotFound.Heading")));
        app.FindAll(".shell-sidebar a.cms-nav-link").Count.ShouldBe(AdministrationAreas.All.Count + 1);
        PseudoCulture.Unlocalized(app.Nodes).ShouldBeEmpty();
    }
}
