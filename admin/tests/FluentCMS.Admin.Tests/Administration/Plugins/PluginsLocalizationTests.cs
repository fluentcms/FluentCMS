using FluentCMS.Admin.Administration.Plugins;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Administration.Plugins;

// The check of spec 3.1 on the pages of the area: under a pseudo-culture no text is left untranslated. The area's issue
// adds each of its routes here, with the answers of the fake Management API that its page reads, so that the page is
// checked with its data and not only in its error state.
public sealed class PluginsLocalizationTests : AdministrationTestContext
{
    [Theory]
    [InlineData(PseudoCulture.LeftToRight)]
    [InlineData(PseudoCulture.RightToLeft)]
    public void EveryTextOfThePluginsPageComesFromTheResources(string culture)
    {
        var app = LoadLocalized(culture, "plugins");

        // The heading of an area's first page is {Area}.Page.Heading of its resources, which the end-to-end tests find it by.
        Heading(app).ShouldBe(Localized<PluginsResources>("Plugins.Page.Heading"));
    }
}
