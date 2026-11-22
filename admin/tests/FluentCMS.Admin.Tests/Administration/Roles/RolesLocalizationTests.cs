using System.Net;
using System.Text.Json.Nodes;
using Bunit;
using FluentCMS.Admin.Administration.Roles;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Administration.Roles;

// The check of spec 3.1 on the pages of the area: under a pseudo-culture no text is left untranslated. The area's issue
// adds each of its routes here, with the answers of the fake Management API that its page reads, so that the page is
// checked with its data and not only in its error state.
public sealed class RolesLocalizationTests : RolesTestContext
{
    [Theory]
    [InlineData(PseudoCulture.LeftToRight)]
    [InlineData(PseudoCulture.RightToLeft)]
    public void EveryTextOfTheRolesPageComesFromTheResources(string culture)
    {
        MarkData(culture);

        var app = LoadLocalized(culture, "roles", shown => shown.FindAll("main table tbody tr").Count.ShouldBe(Roles.Count));

        // The heading of an area's first page is {Area}.Page.Heading of its resources, which the end-to-end tests find it by.
        Heading(app).ShouldBe(Localized<RolesResources>("Roles.Page.Heading"));
    }

    [Theory]
    [InlineData(PseudoCulture.LeftToRight)]
    [InlineData(PseudoCulture.RightToLeft)]
    public void EveryTextOfARoleWithConstrainedGrantsComesFromTheResources(string culture)
    {
        MarkData(culture);

        LoadLocalized(culture, $"roles/{Reviewers:D}", shown => shown.FindAll("main [data-grant] dl").Count.ShouldBe(2));
    }

    [Theory]
    [InlineData(PseudoCulture.LeftToRight)]
    [InlineData(PseudoCulture.RightToLeft)]
    public void EveryTextOfThePublicRoleAndOfANewRoleComesFromTheResources(string culture)
    {
        MarkData(culture);

        var app = LoadLocalized(culture, $"roles/{SeededPublic:D}", shown => shown.Find("main [role=note]"));

        Navigation.NavigateTo("roles/new");

        app.WaitForAssertion(() => app.FindAll("main select").Count.ShouldBe(3));
        PseudoCulture.Unlocalized(app.Nodes).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(PseudoCulture.LeftToRight)]
    [InlineData(PseudoCulture.RightToLeft)]
    public void EveryTextOfARefusalComesFromTheResources(string culture)
    {
        MarkData(culture);
        Refuse = (method, path) => method == HttpMethod.Put ? Problem(HttpStatusCode.Forbidden, "FCMS3103") : null;
        var app = LoadLocalized(culture, $"roles/{Reviewers:D}", shown => shown.Find("main form"));

        Save(app);

        app.WaitForAssertion(() => Alert(app).ShouldNotBeEmpty());
        PseudoCulture.Unlocalized(app.Nodes).ShouldBeEmpty();
    }

    // Data the fake serves is marked as a resource is, so that only a text of the page itself can fail the check.
    private void MarkData(string culture)
    {
        var rightToLeft = culture == PseudoCulture.RightToLeft;
        string Mark(string value) => PseudoCulture.Transform(value, rightToLeft);

        void MarkAll(JsonNode? items)
        {
            if (items is JsonArray array)
            {
                for (var index = 0; index < array.Count; index++)
                {
                    array[index] = Mark(array[index]!.GetValue<string>());
                }
            }
        }

        foreach (var role in Roles)
        {
            role!["name"] = Mark((string)role["name"]!);
            role["description"] = Mark("A role");
            foreach (var grant in role["grants"]!.AsArray())
            {
                grant!["permission"] = Mark((string)grant["permission"]!);
                if (grant["constraints"] is JsonObject constraints)
                {
                    MarkAll(constraints["locales"]);
                    MarkAll(constraints["fields"]);
                    MarkAll(constraints["resources"]);
                    if (constraints["rowRule"] is { } rule)
                    {
                        constraints["rowRule"] = Mark((string)rule!);
                    }
                }
            }
        }

        foreach (var permission in Permissions)
        {
            permission!["name"] = Mark((string)permission["name"]!);
            permission["description"] = Mark((string)permission["description"]!);
        }

        MarkAll(Management.Plugins[MainSite][0]!["contributions"]!["permissions"]);
        Management.Plugins[MainSite][0]!["name"] = Mark("media");
        Management.Schemas[MainSite]["types"]![0]!["displayName"] = Mark("Article");
        foreach (var site in Management.Sites)
        {
            site!["name"] = Mark((string)site["name"]!);
        }
    }
}
