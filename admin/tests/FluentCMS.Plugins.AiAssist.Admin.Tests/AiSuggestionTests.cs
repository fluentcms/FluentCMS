using System.Text.Json.Nodes;
using Bunit;
using FluentCMS.Admin.Actions;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Components;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.AiAssist.Admin.Tests;

// The actions of the half on a field (spec 5.5): the panel the suggestion streams into, the draft changed only by an
// acceptance, and every other end, a decline, a stop, a refusal before the stream and an error within it, leaving it.
public sealed class AiSuggestionTests : AiTestContext
{
    [Fact]
    public async Task TheSuggestionArrivesInPartsAndGoesIntoTheDraftOnlyWhenAccepted()
    {
        var (result, panel) = Run(new RewriteAction(Overlays, Texts), Context("summary", new JsonObject { ["summary"] = "Ships are big boats." }));
        panel.WaitForAssertion(() => panel.Markup.ShouldContain("Writing the suggestion…"));
        Buttons(panel).ShouldBe(["Stop"]);

        Server.Delta("Ships are ");
        panel.WaitForAssertion(() => panel.Find(".ai-suggestion").TextContent.Trim().ShouldBe("Ships are"));
        panel.Find(".ai-suggestion").GetAttribute("aria-busy").ShouldBe("true");
        Server.Delta("large vessels.");
        panel.WaitForAssertion(() => panel.Find(".ai-suggestion").TextContent.Trim().ShouldBe("Ships are large vessels."));
        Buttons(panel).ShouldNotContain("Accept");
        result.IsCompleted.ShouldBeFalse();

        Server.Done("Ships are large vessels.");
        panel.WaitForAssertion(() => Buttons(panel).ShouldBe(["Accept", "Decline"]));
        Button(panel, "Accept").Click();
        await ClosedAsync(panel);

        var done = await result;
        done.Replaces.ShouldBeTrue();
        done.Value!.GetValue<string>().ShouldBe("Ships are large vessels.");
        Overlays.Shown.ShouldBeEmpty();

        // Announced once as it starts and once as it ends, never piece by piece.
        Announcer.Messages.ShouldBe(["Writing a suggestion.", "The suggestion is ready. Accept it to put it into the field, or decline it."]);
        var sent = Server.Requests.ShouldHaveSingleItem();
        sent.Path.ShouldBe("ai/actions/rewrite");
        JsonNode.DeepEquals(sent.Body, new JsonObject { ["type"] = "article", ["field"] = "summary", ["locale"] = "en", ["text"] = "Ships are big boats." }).ShouldBeTrue(sent.Body!.ToJsonString());
    }

    [Fact]
    public async Task ADeclinedSuggestionLeavesTheDraft()
    {
        var (result, panel) = Run(new ShortenAction(Overlays, Texts), Context("title", new JsonObject { ["title"] = "A long title" }));
        Server.Done("A title");
        panel.WaitForAssertion(() => Buttons(panel).ShouldContain("Decline"));

        Button(panel, "Decline").Click();
        await ClosedAsync(panel);

        (await result).ShouldBeSameAs(FieldActionResult.Unchanged);
    }

    [Fact]
    public async Task AStopEndsTheStreamAndLeavesTheDraft()
    {
        var (result, panel) = Run(new ExpandAction(Overlays, Texts), Context("title", new JsonObject { ["title"] = "Ships" }));
        Server.Delta("Ships are");
        panel.WaitForAssertion(() => panel.Find(".ai-suggestion").TextContent.Trim().ShouldBe("Ships are"));

        Button(panel, "Stop").Click();
        await ClosedAsync(panel);

        (await result).ShouldBeSameAs(FieldActionResult.Unchanged);
        Server.StreamCancelled.ShouldBeTrue();
    }

    // What came before the error is not a suggestion: it goes, and nothing can be accepted.
    [Fact]
    public async Task AnErrorWithinTheStreamShowsItsReasonAndLeavesTheDraft()
    {
        var (result, panel) = Run(new RewriteAction(Overlays, Texts), Context("title", new JsonObject { ["title"] = "Ships" }));
        Server.Delta("Ships are ");
        Server.Error("FCMS1689", "The AI provider failed", "The AI provider failed the call.");

        panel.WaitForAssertion(() => panel.Find(".ai-panel-problem").TextContent.ShouldContain("The AI provider failed the call."));
        panel.Find(".ai-panel-problem strong").TextContent.ShouldBe("The AI provider failed");
        panel.Find(".ai-suggestion").TextContent.Trim().ShouldBeEmpty();
        Buttons(panel).ShouldBe(["Close"]);
        Announcer.Messages.ShouldContain("The AI provider failed");

        Button(panel, "Close").Click();
        await ClosedAsync(panel);
        (await result).ShouldBeSameAs(FieldActionResult.Unchanged);
    }

    // The daily budget is a 429 the client does not send again: its own text is shown, and the action is asked once.
    [Theory]
    [InlineData(429, "FCMS1693", "AI token budget used", "The site has used its daily budget of 1000 AI tokens. It resets at 2026-11-21T00:00:00Z.")]
    [InlineData(403, "FCMS1683", "Field not opted in", "The AI settings of the site do not opt this field in.")]
    public async Task ARefusalBeforeTheStreamShowsItsReasonAndLeavesTheDraft(int status, string code, string title, string detail)
    {
        Server.Refusal = new ApiProblem { Status = status, Code = code, Title = title, Detail = detail };

        var (result, panel) = Run(new RewriteAction(Overlays, Texts), Context("title", new JsonObject { ["title"] = "Ships" }));

        panel.WaitForAssertion(() => panel.Find(".ai-panel-problem strong").TextContent.ShouldBe(title));
        panel.Find(".ai-panel-problem").TextContent.ShouldContain(detail);
        Buttons(panel).ShouldBe(["Close"]);
        Server.Requests.Count.ShouldBe(1);

        Button(panel, "Close").Click();
        await ClosedAsync(panel);
        (await result).ShouldBeSameAs(FieldActionResult.Unchanged);
        Server.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ATextActionIsNotOfferedOnAnEmptyField()
    {
        (await new RewriteAction(Overlays, Texts).AppliesToAsync(Context("title", new JsonObject { ["title"] = " " }), Token)).ShouldBeFalse();
        (await new RewriteAction(Overlays, Texts).AppliesToAsync(Context("title", []), Token)).ShouldBeFalse();
    }

    [Fact]
    public async Task SeoOffersItsMetaTitleAndItsMetaDescriptionForTheField()
    {
        var (result, panel) = Run(new SeoAction(Overlays, Texts), Context("summary", new JsonObject { ["summary"] = "Ships are big boats." }));
        Server.Done(new JsonObject { ["metaTitle"] = "Ships at sea", ["metaDescription"] = "Where ships sail." });

        panel.WaitForAssertion(() => Buttons(panel).ShouldBe(["Use the meta title", "Use the meta description", "Decline"]));
        panel.FindAll(".ai-suggestion dd").Select(item => item.TextContent).ShouldBe(["Ships at sea", "Where ships sail."]);
        Button(panel, "Use the meta description").Click();
        await ClosedAsync(panel);

        (await result).Value!.GetValue<string>().ShouldBe("Where ships sail.");
    }

    // The images are those of the saved draft in the form's locale, which the server describes, and not one the form holds
    // unsaved; the call names the entry.
    [Fact]
    public async Task AltTextIsSuggestedForAnImageOfTheEntryChosenAmongItsMediaFields()
    {
        var hero = Server.AddImage("hero.png");
        var photo = Server.AddImage("photo.jpg", mimeType: "image/jpeg");
        var paper = Server.AddImage("paper.pdf", mimeType: "application/pdf");
        var unsaved = Server.AddImage("unsaved.png");
        var entry = Guid.CreateVersion7();
        Server.Drafts[(entry, "en")] = new JsonObject { ["id"] = entry, ["hero"] = Read(hero), ["gallery"] = new JsonArray(Read(photo), Read(paper)) };
        var document = new JsonObject { ["title"] = "", ["hero"] = unsaved.ToString("D"), ["gallery"] = new JsonArray(photo.ToString("D"), paper.ToString("D")) };
        var action = new AltTextAction(Overlays, Texts, Server);
        (await action.AppliesToAsync(Context("title", document, entryId: entry), Token)).ShouldBeTrue();
        var draftRead = Server.Requests.ShouldHaveSingleItem();
        (draftRead.Path, draftRead.Locale).ShouldBe(($"content/article/{entry:D}?fallback=none&fields=hero,gallery", "en"));

        var (result, panel) = Run(action, Context("title", document, entryId: entry));
        panel.WaitForAssertion(() => panel.FindAll("select option").Select(option => option.TextContent).ShouldBe(["", "hero.png", "photo.jpg"]));
        panel.Find("select").Change(photo.ToString("D"));
        Button(panel, "Suggest").Click();
        Server.Done("A ship at sea.");
        panel.WaitForAssertion(() => Buttons(panel).ShouldContain("Accept"));
        Button(panel, "Accept").Click();
        await ClosedAsync(panel);

        (await result).Value!.GetValue<string>().ShouldBe("A ship at sea.");
        var sent = Server.Requests.Single(request => request.Path == "ai/actions/altText");
        JsonNode.DeepEquals(sent.Body, new JsonObject { ["type"] = "article", ["field"] = "title", ["locale"] = "en", ["documentId"] = entry, ["options"] = new JsonObject { ["mediaId"] = photo } }).ShouldBeTrue(sent.Body!.ToJsonString());
    }

    // Where the call cannot pass, the action is not offered: an entry not created yet, one not saved in the form's locale, and
    // one whose saved draft holds no image, whatever the form holds unsaved.
    [Fact]
    public async Task AltTextIsNotOfferedWhereTheSavedDraftHasNoImage()
    {
        var hero = Server.AddImage("hero.png");
        var entry = Guid.CreateVersion7();
        var bare = Guid.CreateVersion7();
        Server.Drafts[(entry, "en")] = new JsonObject { ["id"] = entry, ["hero"] = Read(hero), ["gallery"] = new JsonArray() };
        Server.Drafts[(bare, "en")] = new JsonObject { ["id"] = bare, ["hero"] = null, ["gallery"] = new JsonArray() };
        var document = new JsonObject { ["title"] = "Ships", ["hero"] = hero.ToString("D") };
        var action = new AltTextAction(Overlays, Texts, Server);

        (await action.AppliesToAsync(Context("title", document, created: false), Token)).ShouldBeFalse();
        Server.Requests.ShouldBeEmpty();
        (await action.AppliesToAsync(Context("title", document, locale: "fr", entryId: entry), Token)).ShouldBeFalse();
        (await action.AppliesToAsync(Context("title", document, entryId: bare), Token)).ShouldBeFalse();
        (await action.AppliesToAsync(Context("title", document, entryId: entry), Token)).ShouldBeTrue();
    }

    // A media field as a read gives it.
    private static JsonObject Read(Guid asset) => new() { ["id"] = asset, ["type"] = "media" };

    // The asset's own alt text is the asset's, saved on it as the media field saves it; the entry's draft stays as it was.
    [Fact]
    public async Task TheAltTextOfAnAssetIsSavedOnTheAssetWhenAcceptedAndNotInTheDraft()
    {
        var hero = Server.AddImage("hero.png", alt: "Old");
        var action = new AssetAltTextAction(Overlays, Texts);
        var context = Context("hero", new JsonObject { ["hero"] = hero.ToString("D") }, locale: "fr");
        action.Label.ShouldBe("Suggest the image's alt text");
        (await action.AppliesToAsync(context, Token)).ShouldBeTrue();
        (await action.AppliesToAsync(Context("hero", []), Token)).ShouldBeFalse();

        var (result, panel) = Run(action, context);
        Server.Done("A ship at sea.");
        panel.WaitForAssertion(() => Buttons(panel).ShouldContain("Accept"));
        Button(panel, "Accept").Click();
        await ClosedAsync(panel);

        (await result).ShouldBeSameAs(FieldActionResult.Unchanged);
        Server.Assets[hero]["alt"]!.GetValue<string>().ShouldBe("A ship at sea.");
        var asked = Server.Requests.Single(request => request.Path == "ai/actions/altText");
        JsonNode.DeepEquals(asked.Body, new JsonObject { ["locale"] = "fr", ["options"] = new JsonObject { ["mediaId"] = hero } }).ShouldBeTrue(asked.Body!.ToJsonString());
        var saved = Server.Requests.Single(request => request.Method == HttpMethod.Patch);
        (saved.Path, saved.Locale, saved.IfMatch).ShouldBe(($"media/{hero:D}", "fr", "\"3\""));
        Announcer.Messages.ShouldContain("The alt text of hero.png is saved.");
    }

    // An asset changed since the panel read it is read again, its alt text shown, and the next accept is over that version.
    [Fact]
    public async Task AnAssetChangedSinceItWasReadIsReadAgainAndTheNextAcceptIsOverItsNewVersion()
    {
        var hero = Server.AddImage("hero.png", alt: "Old");
        var (result, panel) = Run(new AssetAltTextAction(Overlays, Texts), Context("hero", new JsonObject { ["hero"] = hero.ToString("D") }));
        Server.Done("A ship at sea.");
        panel.WaitForAssertion(() => Buttons(panel).ShouldContain("Accept"));
        Server.Assets[hero]["alt"] = "Changed elsewhere";
        Server.Assets[hero]["version"] = 4;

        Button(panel, "Accept").Click();

        panel.WaitForAssertion(() => panel.Find(".ai-panel-problem").TextContent.ShouldContain("Its alt text is now: Changed elsewhere"));
        panel.Find(".ai-panel-problem strong").TextContent.ShouldBe("The alt text of hero.png was changed since it was read. Accept again to replace it.");
        Server.Assets[hero]["alt"]!.GetValue<string>().ShouldBe("Changed elsewhere");
        result.IsCompleted.ShouldBeFalse();

        Button(panel, "Accept").Click();
        await ClosedAsync(panel);

        (await result).ShouldBeSameAs(FieldActionResult.Unchanged);
        Server.Assets[hero]["alt"]!.GetValue<string>().ShouldBe("A ship at sea.");
        Server.Requests.Where(request => request.Method == HttpMethod.Patch).Select(request => request.IfMatch).ShouldBe(["\"3\"", "\"4\""]);
    }

    // The asset is read again in the form's locale, as it was read first, whatever locale the first answer named.
    [Fact]
    public void AnAssetChangedSinceItWasReadIsReadAgainInTheLocaleOfTheFirstRead()
    {
        var hero = Server.AddImage("hero.png", alt: "Old");
        Server.AnsweredLocale = _ => null;
        var (_, panel) = Run(new AssetAltTextAction(Overlays, Texts), Context("hero", new JsonObject { ["hero"] = hero.ToString("D") }, locale: "fr"));
        Server.Done("A ship at sea.");
        panel.WaitForAssertion(() => Buttons(panel).ShouldContain("Accept"));
        Server.Assets[hero]["version"] = 4;

        Button(panel, "Accept").Click();

        panel.WaitForAssertion(() => panel.Find(".ai-panel-problem strong").TextContent.ShouldBe("The alt text of hero.png was changed since it was read. Accept again to replace it."));
        Server.Requests.Where(request => request.Method == HttpMethod.Get && request.Path == $"media/{hero:D}").Select(request => request.Locale).ShouldBe(["fr", "fr"]);
    }
}