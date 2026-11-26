using System.Text.Json.Nodes;
using Bunit;
using FluentCMS.Admin.Actions;
using FluentCMS.Admin.Api;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.AiAssist.Admin.Tests;

// Translate (spec 5.5, #3513): the field's text in the locale the form shows, translated into another enabled locale of the
// site that the user chooses among those where it may change the field; an accepted translation is a value for that locale,
// which the form puts into its draft, and never one for the locale shown.
public sealed class TranslateTests : AiTestContext
{
    public TranslateTests() => Admin.Site = Admin.Site! with
    {
        Locales =
        [
            new MySiteLocale { Code = "en", Position = 0, Enabled = true },
            new MySiteLocale { Code = "es", Position = 2, Enabled = true },
            new MySiteLocale { Code = "fr", Position = 1, Enabled = true },
            new MySiteLocale { Code = "de", Position = 3, Enabled = false },
        ],
    };

    private TranslateAction Action => new(Overlays, Texts, Admin, Server);

    [Fact]
    public async Task TheUserChoosesTheLocaleAndAnAcceptedTranslationIsAValueForThatLocale()
    {
        var context = Context("title", new JsonObject { ["title"] = "Ships are big boats." });
        Action.Label.ShouldBe("Translate");
        (await Action.AppliesToAsync(context, Token)).ShouldBeTrue();

        var (result, panel) = Run(Action, context);
        panel.WaitForAssertion(() => panel.FindAll("select option").Select(option => option.TextContent).ShouldBe(["", "français (fr)", "español (es)"]));
        panel.Find("label").TextContent.ShouldContain("Translate into");
        Button(panel, "Suggest").HasAttribute("disabled").ShouldBeTrue();
        Server.Requests.ShouldNotContain(request => request.Path.StartsWith("ai/actions/", StringComparison.Ordinal));

        panel.Find("select").Change("es");
        Button(panel, "Suggest").Click();
        Server.Done("Los barcos son grandes.");
        panel.WaitForAssertion(() => Buttons(panel).ShouldBe(["Accept", "Decline"]));
        panel.Find(".ai-suggestion").GetAttribute("lang").ShouldBe("es");
        panel.Find(".ai-panel-note").TextContent.ShouldBe("Accepting moves the form to español (es) with the translation in the field, not saved yet. The field in this locale keeps its text.");
        Button(panel, "Accept").Click();
        await ClosedAsync(panel);

        var done = await result;
        (done.Replaces, done.Locale, done.Value!.GetValue<string>()).ShouldBe((true, "es", "Los barcos son grandes."));
        Announcer.Messages.ShouldBe(["Writing a suggestion.", "The translation is ready. Accept it to put it into the field in español (es), or decline it."]);
        var sent = Server.Requests.Single(request => request.Path == "ai/actions/translate");
        JsonNode.DeepEquals(sent.Body, new JsonObject { ["type"] = "article", ["field"] = "title", ["locale"] = "en", ["text"] = "Ships are big boats.", ["options"] = new JsonObject { ["targetLocale"] = "es" } }).ShouldBeTrue(sent.Body!.ToJsonString());
    }

    // The locales are read for the caller's access in each: one where it may not change the field is not offered, and with
    // one left it is chosen already. A decline gives nothing for any locale.
    [Fact]
    public async Task ALocaleWhereTheUserMayNotChangeTheFieldIsLeftOutAndADeclineGivesNothing()
    {
        Server.ReadOnlyIn.Add("es");

        var (result, panel) = Run(Action, Context("title", new JsonObject { ["title"] = "Ships" }));

        panel.WaitForAssertion(() => panel.FindAll("select option").Select(option => option.TextContent).ShouldBe(["français (fr)"]));
        Server.Requests.Where(request => request.Path == "me/access").Select(request => request.Locale).ShouldBe(["fr", "es"]);
        Button(panel, "Suggest").HasAttribute("disabled").ShouldBeFalse();
        Button(panel, "Suggest").Click();
        Server.Done("Navires");
        panel.WaitForAssertion(() => Buttons(panel).ShouldContain("Decline"));
        Button(panel, "Decline").Click();
        await ClosedAsync(panel);

        (await result).ShouldBeSameAs(FieldActionResult.Unchanged);
    }

    // From another locale, the site's default is a target as any other, and the locale shown never is.
    [Fact]
    public void TheLocaleTheFormShowsIsNeverATarget()
    {
        var (_, panel) = Run(Action, Context("summary", new JsonObject { ["summary"] = "Des navires." }, locale: "fr"));

        panel.WaitForAssertion(() => panel.FindAll("select option").Select(option => option.TextContent).ShouldBe(["", "English (en)", "español (es)"]));
    }

    // A type that is not localized, an entry not created yet, an empty field and a site where the caller may change the field
    // in no other locale offer no translation.
    [Fact]
    public async Task TranslateIsNotOfferedWhereThereIsNoOtherLocaleToWriteIn()
    {
        (await Action.AppliesToAsync(Context("title", new JsonObject { ["title"] = "Ships" }, locale: null), Token)).ShouldBeFalse();
        (await Action.AppliesToAsync(Context("title", new JsonObject { ["title"] = "Ships" }, created: false), Token)).ShouldBeFalse();
        (await Action.AppliesToAsync(Context("title", new JsonObject { ["title"] = " " }), Token)).ShouldBeFalse();
        Server.Requests.ShouldBeEmpty();

        Server.ReadOnlyIn.UnionWith(["fr", "es"]);
        (await Action.AppliesToAsync(Context("title", new JsonObject { ["title"] = "Ships" }), Token)).ShouldBeFalse();
    }
}
