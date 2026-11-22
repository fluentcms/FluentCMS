using System.Text.Json.Nodes;
using Bunit;
using FluentCMS.Admin.Administration.Plugins;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Administration.Plugins;

// The check of spec 3.1 on the pages of the area: under a pseudo-culture no text is left untranslated. The area's issue
// adds each of its routes here, with the answers of the fake Management API that its page reads, so that the page is
// checked with its data and not only in its error state.
public sealed class PluginsLocalizationTests : PluginsTestContext
{
    private string shop = "";

    [Theory]
    [InlineData(PseudoCulture.LeftToRight)]
    [InlineData(PseudoCulture.RightToLeft)]
    public void EveryTextOfThePluginsPageComesFromTheResources(string culture)
    {
        shop = MarkData(culture);

        var app = LoadLocalized(culture, "plugins", shown => shown.FindAll("main section.cms-card").Count.ShouldBe(2));

        // The heading of an area's first page is {Area}.Page.Heading of its resources, which the end-to-end tests find it by.
        Heading(app).ShouldBe(Localized<PluginsResources>("Plugins.Page.Heading"));

        // A switch's answer, and one the server refuses.
        FirstSwitch(app).Find("fluent-switch").Change(false);
        app.WaitForAssertion(() => app.Find("main .plugins-message").TextContent.ShouldNotBeEmpty());
        PseudoCulture.Unlocalized(app.Nodes).ShouldBeEmpty();

        Management.Answer = (method, path) => method == HttpMethod.Put ? Problem(System.Net.HttpStatusCode.Forbidden, "FCMS2001") : null;
        FirstSwitch(app).Find("fluent-switch").Change(true);
        app.WaitForAssertion(() => app.Find("main .plugins-message").TextContent.ShouldBe(Localized<PluginsResources>("Plugins.Sites.Forbidden", shop)));
        PseudoCulture.Unlocalized(app.Nodes).ShouldBeEmpty();
    }

    private IRenderedComponent<FluentCMS.Admin.Components.CmsSwitch> FirstSwitch(IRenderedComponent<AdminApp> app) =>
        app.FindComponents<FluentCMS.Admin.Components.CmsSwitch>().First(toggle => toggle.Instance.Label == Localized<PluginsResources>("Plugins.Sites.Switch", shop));

    // Data the fake serves is marked as a resource is, so that only a text of the page itself can fail the check: the names,
    // versions and contributions of the plugins, and the names of the sites, which the page shows as they are.
    private string MarkData(string culture)
    {
        var rightToLeft = culture == PseudoCulture.RightToLeft;
        string Mark(string value) => PseudoCulture.Transform(value, rightToLeft);

        foreach (var site in Management.Sites)
        {
            site!["name"] = Mark((string)site["name"]!);
        }

        foreach (var plugin in Installed)
        {
            foreach (var member in (string[])["name", "version", "sdkRange"])
            {
                plugin[member] = Mark((string)plugin[member]!);
            }

            var contributions = plugin["contributions"]!.AsObject();
            foreach (var member in (string[])["fieldTypes", "permissions", "jobs"])
            {
                contributions[member] = new JsonArray([.. contributions[member]!.AsArray().Select(item => (JsonNode)Mark((string)item!))]);
            }

            foreach (var endpoint in contributions["endpoints"]!.AsArray())
            {
                endpoint!["method"] = Mark((string)endpoint["method"]!);
                endpoint["route"] = Mark((string)endpoint["route"]!);
            }
        }

        return Mark("Shop");
    }
}
