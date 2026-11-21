using System.Text.Json.Nodes;
using Bunit;
using FluentCMS.Admin.Administration.ApiKeys;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Administration.ApiKeys;

// The check of spec 3.1 on the pages of the area: under a pseudo-culture no text is left untranslated. The area's issue
// adds each of its routes here, with the answers of the fake Management API that its page reads, so that the page is
// checked with its data and not only in its error state.
public sealed class ApiKeysLocalizationTests : AdministrationTestContext
{
    private readonly ApiKeysFake keys = new();

    [Theory]
    [InlineData(PseudoCulture.LeftToRight)]
    [InlineData(PseudoCulture.RightToLeft)]
    public void EveryTextOfTheApiKeysPageComesFromTheResources(string culture)
    {
        Serve(culture);
        var app = LoadLocalized(culture, "api-keys", ready: app => app.FindAll("main a[href^='api-keys/0']").Count.ShouldBe(2));

        // The heading of an area's first page is {Area}.Page.Heading of its resources, which the end-to-end tests find it by.
        Heading(app).ShouldBe(Localized<ApiKeysResources>("ApiKeys.Page.Heading"));
    }

    [Theory]
    [InlineData(PseudoCulture.LeftToRight)]
    [InlineData(PseudoCulture.RightToLeft)]
    public void EveryTextOfTheNewKeyFormComesFromTheResources(string culture)
    {
        Serve(culture);
        var app = LoadLocalized(culture, "api-keys/new", ready: app => app.FindAll("form.api-key-form select").Count.ShouldBe(2));

        Heading(app).ShouldBe(Localized<ApiKeysResources>("ApiKeys.New.Heading"));
    }

    [Theory]
    [InlineData(PseudoCulture.LeftToRight)]
    [InlineData(PseudoCulture.RightToLeft)]
    public void EveryTextOfAKeyComesFromTheResources(string culture)
    {
        Serve(culture);
        var app = LoadLocalized(culture, ApiKeyRoutes.Key(ApiKeysFake.KeyId), ready: app => app.Find(".api-key-facts"));

        app.Find("main").TextContent.ShouldContain(Localized<ApiKeysResources>("ApiKeys.Key.RotateHeading"));
    }

    // The data the pages show is marked as a resource is, so that only a text of the page itself can fail the check.
    private void Serve(string culture)
    {
        var rightToLeft = culture == PseudoCulture.RightToLeft;
        string Data(string text) => PseudoCulture.Transform(text, rightToLeft);

        Management.Sites[0]!["name"] = Data("Main");
        Management.SetTypes(MainSite, Type("article", displayName: Data("Article")));
        keys.Roles[0]!["name"] = Data("Reader");
        keys.Key = ApiKeysFake.KeyOf(ApiKeysFake.KeyId, Data("Shop reader"), Data("fcms_dk_ab12"));
        keys.Key["typeNames"] = new JsonArray(Data("article"));
        keys.Key["allowedAddresses"] = new JsonArray("203.0.113.0/24");
        keys.Key["expiresAt"] = "2999-01-01T00:00:00Z";
        keys.Key["rateLimit"] = 60;
        var revoked = ApiKeysFake.KeyOf(Guid.CreateVersion7(), Data("Old feed"), Data("fcms_dk_cd34"));
        revoked["revokedAt"] = "2026-11-02T10:00:00Z";
        keys.Keys = [keys.Key.DeepClone(), revoked];
        Management.Respond = keys.Respond;
    }
}
