using System.Text.Json.Nodes;
using Bunit;
using FluentCMS.Admin.Administration.Settings;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Administration.Settings;

// The check of spec 3.1 on the pages of the area: under a pseudo-culture no text is left untranslated. The area's issue
// adds each of its routes here, with the answers of the fake Management API that its page reads, so that the page is
// checked with its data and not only in its error state.
public sealed class SettingsLocalizationTests : AdministrationTestContext
{
    private readonly FakeSettings settings = new();

    public SettingsLocalizationTests() => Management.Respond = settings.Respond;

    [Theory]
    [InlineData(PseudoCulture.LeftToRight)]
    [InlineData(PseudoCulture.RightToLeft)]
    public void EveryTextOfTheSettingsPageComesFromTheResources(string culture)
    {
        settings.Areas.Add(FakeSettings.Area("branding", site: true, defaults: true));
        settings.Areas.Add(FakeSettings.Area("ai", "ai", site: true, defaults: true));
        settings.Areas.Add(FakeSettings.Area("media", "media", site: false, defaults: true));

        var app = LoadLocalized(culture, "settings", ready: app => app.FindAll("main .settings-areas a").Count.ShouldBe(3));

        // The heading of an area's first page is {Area}.Page.Heading of its resources, which the end-to-end tests find it by.
        Heading(app).ShouldBe(Localized<SettingsResources>("Settings.Page.Heading"));
    }

    [Theory]
    [InlineData(PseudoCulture.LeftToRight, false)]
    [InlineData(PseudoCulture.RightToLeft, false)]
    [InlineData(PseudoCulture.LeftToRight, true)]
    [InlineData(PseudoCulture.RightToLeft, true)]
    public void EveryTextOfTheGeneratedPageComesFromTheResources(string culture, bool defaults)
    {
        var rightToLeft = culture == PseudoCulture.RightToLeft;
        string Data(string text) => PseudoCulture.Transform(text, rightToLeft);

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                [Data("model")] = new JsonObject { ["type"] = "string", ["maxLength"] = 128, ["description"] = Data("The model.") },
                [Data("tokens")] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 32000 },
                [Data("legacy")] = new JsonObject { ["type"] = "boolean" },
                [Data("key")] = new JsonObject { ["type"] = new JsonArray("string", "null"), ["writeOnly"] = true },
                [Data("token")] = new JsonObject { ["type"] = new JsonArray("string", "null"), ["writeOnly"] = true },
            },
        };
        var value = new JsonObject { [Data("model")] = Data("gpt"), [Data("tokens")] = 1024, [Data("legacy")] = false };
        settings.Areas.Add(FakeSettings.Area("ai", "ai", site: true, defaults: true));
        var site = settings.Add("ai", schema, value, source: "site", version: 2);
        (site.SecretsSet, site.SecretsInherited) = ([Data("key")], [Data("key"), Data("token")]);
        settings.Add("ai/defaults", schema.DeepClone().AsObject(), value.DeepClone().AsObject(), source: "default", version: 1);

        var app = LoadLocalized(culture, defaults ? "settings/ai/defaults" : "settings/ai", ready: app => app.Find(".settings-source"));

        Heading(app).ShouldBe(Localized<SettingsResources>("Settings.Area.Name.ai"));
        app.FindAll("main .settings-fields input").Count.ShouldBe(4);
    }
}
