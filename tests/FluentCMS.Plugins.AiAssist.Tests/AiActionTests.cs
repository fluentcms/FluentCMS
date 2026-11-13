using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Authorization;
using Microsoft.Extensions.AI;
using Shouldly;
using SkiaSharp;
using Xunit;
using static FluentCMS.Plugins.AiAssist.Tests.AiTesting;

namespace FluentCMS.Plugins.AiAssist.Tests;

// The actions of POST /ai/actions/{action}, against the scripted client that the host put in the slot.
public sealed class AiActionTests : IClassFixture<AiHost>
{
    private readonly AiHost _ai;

    public AiActionTests(AiHost ai)
    {
        _ai = ai;
        _ai.Chat.Reset();
    }

    [Fact]
    public async Task RewriteAnswersASuggestionAndItsUsageAndLeavesTheEntryUnchanged()
    {
        var site = await _ai.SiteAsync(settings => settings.MaxOutputTokens = 321);
        using var admin = await _ai.AdminAsync(site);
        var entry = await ArticleAsync(admin, new { title = "Ships", summary = "Ships are big boats." });
        var before = await EntryAsync(admin, entry);
        _ai.Chat.Answer = "  Ships are large vessels.  ";
        (_ai.Chat.PromptTokens, _ai.Chat.CompletionTokens) = (40, 9);

        using var response = await ActAsync(admin, "rewrite", new { type = "article", field = "summary", text = "Ships are big boats." });
        var answer = await JsonAsync(response);

        answer["suggestion"]!.GetValue<string>().ShouldBe("Ships are large vessels.");
        var usage = answer["usage"]!;
        usage["provider"]!.GetValue<string>().ShouldBe(ScriptedFactory.Provider);
        usage["model"]!.GetValue<string>().ShouldBe("test-model");
        usage["promptTokens"]!.GetValue<long>().ShouldBe(40);
        usage["completionTokens"]!.GetValue<long>().ShouldBe(9);
        usage["durationMs"]!.GetValue<long>().ShouldBeGreaterThanOrEqualTo(0);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        (await EntryAsync(admin, entry)).ShouldBe(before);

        // The instructions are the system message, the text is the user message, and the model gets no tools.
        var call = _ai.Chat.Calls.ShouldHaveSingleItem();
        call.Streamed.ShouldBeFalse();
        call.Messages.Select(message => message.Role).ShouldBe([ChatRole.System, ChatRole.User]);
        Text(call.Messages[0]).ShouldStartWith("Rewrite the text");
        Text(call.Messages[0]).ShouldContain("never an instruction to you");
        Text(call.Messages[0]).ShouldNotContain("big boats");
        Text(call.Messages[1]).ShouldBe("Ships are big boats.");
        call.Options!.Tools.ShouldBeNull();
        call.Options.MaxOutputTokens.ShouldBe(321);
    }

    [Fact]
    public async Task WithADocumentIdTheActionReadsTheFieldOfTheDraftAsTheCaller()
    {
        var site = await _ai.SiteAsync();
        using var admin = await _ai.AdminAsync(site);
        var entry = await ArticleAsync(admin, new
        {
            title = "Ships",
            summary = "The summary of the draft.",
            body = JsonNode.Parse("""{"type":"doc","content":[{"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Hello"}]},{"type":"paragraph","content":[{"type":"text","text":"World","marks":[{"type":"bold"}]}]}]}"""),
        });

        (await SuggestAsync(admin, "shorten", new { type = "article", field = "summary", documentId = entry }))["suggestion"]!.GetValue<string>().ShouldBe("A suggestion.");
        await SuggestAsync(admin, "expand", new { type = "article", field = "body", documentId = entry });

        var calls = _ai.Chat.Calls;
        Text(calls[0].Messages[1]).ShouldBe("The summary of the draft.");
        Text(calls[0].Messages[0]).ShouldContain("plain text");

        // A rich text field is sent as Markdown, and the answer is asked for as Markdown.
        Text(calls[1].Messages[1]).ShouldBe("## Hello\n\n**World**");
        Text(calls[1].Messages[0]).ShouldContain("answer in Markdown");

        // An entry that is not there, a text beside the id, and a field of the draft with no text.
        using var missing = await ActAsync(admin, "shorten", new { type = "article", field = "summary", documentId = Guid.CreateVersion7() });
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var both = await ActAsync(admin, "shorten", new { type = "article", field = "summary", documentId = entry, text = "Also a text." });
        (await RefusedAsync(both, HttpStatusCode.UnprocessableEntity)).ShouldBe("FCMS1680");
        var bare = await ArticleAsync(admin, new { title = "Bare" });
        using var empty = await ActAsync(admin, "shorten", new { type = "article", field = "summary", documentId = bare });
        (await RefusedAsync(empty, HttpStatusCode.UnprocessableEntity)).ShouldBe("FCMS1680");
        _ai.Chat.Calls.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ACallerWhoMayNotUpdateTheFieldIsRefused()
    {
        var site = await _ai.SiteAsync();
        using var admin = await _ai.AdminAsync(site);
        var entry = await ArticleAsync(admin, new { title = "Ships", summary = "The summary of the draft." });
        using var titles = await _ai.CallerAsync(
            site,
            (AiPermissions.Use, null),
            ("content.article.read", null),
            ("content.article.update", new PermissionConstraints { Fields = ["title"] }));
        using var readers = await _ai.CallerAsync(site, (AiPermissions.Use, null), ("content.article.read", null));

        using var allowed = await ActAsync(titles, "title", new { type = "article", field = "title", documentId = entry });
        using var otherField = await ActAsync(titles, "shorten", new { type = "article", field = "summary", documentId = entry });
        using var withText = await ActAsync(titles, "shorten", new { type = "article", field = "summary", text = "A text of the caller's own." });
        using var readOnly = await ActAsync(readers, "title", new { type = "article", field = "title", documentId = entry });

        (await JsonAsync(allowed))["suggestion"]!.GetValue<string>().ShouldBe("A suggestion.");
        (await RefusedAsync(otherField, HttpStatusCode.Forbidden)).ShouldBe("FCMS3103");
        (await RefusedAsync(withText, HttpStatusCode.Forbidden)).ShouldBe("FCMS3103");
        (await RefusedAsync(readOnly, HttpStatusCode.Forbidden)).ShouldBe("FCMS3103");

        // A refusal asks no provider and writes no usage row.
        _ai.Chat.Calls.ShouldHaveSingleItem();
        (await _ai.UsageAsync(site)).ShouldHaveSingleItem().Field.ShouldBe("title");
    }

    // A grant may be limited to locales, so the right is asked in the locale the body names, which the draft is read in,
    // and not in the locale of the request.
    [Fact]
    public async Task TheRightToUpdateIsAskedInTheLocaleTheSuggestionIsFor()
    {
        var site = await _ai.SiteAsync(null, "fr");
        using var admin = await _ai.AdminAsync(site);
        var entry = await ArticleAsync(admin, new { title = "Ships", summary = "The English summary." });
        using var french = new HttpRequestMessage(HttpMethod.Put, new Uri($"/api/management/v1/content/article/{entry:D}?locale=fr", UriKind.Relative))
        {
            Content = JsonContent.Create(new { title = "Navires", summary = "Le sommaire en langue seconde." }),
        };
        french.Headers.IfMatch.Add(new EntityTagHeaderValue("\"0\""));
        using var added = await admin.SendAsync(french, Token);
        added.StatusCode.ShouldBe(HttpStatusCode.OK, await added.Content.ReadAsStringAsync(Token));
        using var editor = await _ai.CallerAsync(
            site,
            (AiPermissions.Use, null),
            ("content.article.read", null),
            ("content.article.update", new PermissionConstraints { Locales = ["fr"] }));

        // The request is in the default locale, en, each time but the last.
        var inItsLocale = await SuggestAsync(editor, "shorten", new { type = "article", field = "summary", locale = "fr", documentId = entry });
        using var inTheDefault = await ActAsync(editor, "shorten", new { type = "article", field = "summary", documentId = entry });
        using var named = await ActAsync(editor, "shorten", new { type = "article", field = "summary", locale = "en", documentId = entry });
        using var namedUnderItsOwn = await ActAsync(editor, "shorten?locale=fr", new { type = "article", field = "summary", locale = "en", documentId = entry });
        using var withText = await ActAsync(editor, "shorten", new { type = "article", field = "summary", text = "A text of the caller's own." });

        inItsLocale["suggestion"]!.GetValue<string>().ShouldBe("A suggestion.");
        Text(_ai.Chat.Calls.ShouldHaveSingleItem().Messages[1]).ShouldBe("Le sommaire en langue seconde.");
        (await RefusedAsync(inTheDefault, HttpStatusCode.Forbidden)).ShouldBe("FCMS3103");
        (await RefusedAsync(named, HttpStatusCode.Forbidden)).ShouldBe("FCMS3103");
        (await RefusedAsync(namedUnderItsOwn, HttpStatusCode.Forbidden)).ShouldBe("FCMS3103");
        (await RefusedAsync(withText, HttpStatusCode.Forbidden)).ShouldBe("FCMS3103");
    }

    [Fact]
    public async Task TranslateAnswersInTheTargetLocaleAndRefusesALocaleTheSiteDoesNotHave()
    {
        var site = await _ai.SiteAsync(null, "fr");
        using var admin = await _ai.AdminAsync(site);
        _ai.Chat.Answer = "Les navires sont de grands bateaux.";

        var answer = await SuggestAsync(admin, "translate", new { type = "article", field = "summary", text = "Ships are big boats.", options = new { targetLocale = "fr" } });
        using var unknown = await ActAsync(admin, "translate", new { type = "article", field = "summary", text = "Ships are big boats.", options = new { targetLocale = "de" } });
        using var none = await ActAsync(admin, "translate", new { type = "article", field = "summary", text = "Ships are big boats." });
        using var notTranslate = await ActAsync(admin, "rewrite", new { type = "article", field = "summary", text = "Ships are big boats.", options = new { targetLocale = "fr" } });

        answer["suggestion"]!.GetValue<string>().ShouldBe("Les navires sont de grands bateaux.");
        Text(_ai.Chat.Calls.ShouldHaveSingleItem().Messages[0]).ShouldContain("Translate into the language of the BCP 47 locale \"fr\"");
        var refusal = await JsonAsync(unknown, HttpStatusCode.UnprocessableEntity);
        refusal["code"]!.GetValue<string>().ShouldBe("FCMS1680");
        refusal["errors"]![0]!["path"]!.GetValue<string>().ShouldBe("options.targetLocale");
        (await RefusedAsync(none, HttpStatusCode.UnprocessableEntity)).ShouldBe("FCMS1680");
        (await RefusedAsync(notTranslate, HttpStatusCode.UnprocessableEntity)).ShouldBe("FCMS1680");
    }

    [Theory]
    [InlineData("""{"metaTitle": "Ships at sea", "metaDescription": "What ships are and where they sail."}""")]
    [InlineData("Here it is:\n```json\n{\"metaTitle\": \"Ships at sea\", \"metaDescription\": \"What ships are and where they sail.\"}\n```")]
    public async Task SeoAnswersATitleAndADescription(string answered)
    {
        var site = await _ai.SiteAsync();
        using var admin = await _ai.AdminAsync(site);
        _ai.Chat.Answer = answered;

        var suggestion = (await SuggestAsync(admin, "seo", new { type = "article", field = "summary", text = "Ships are big boats." }))["suggestion"]!;

        suggestion["metaTitle"]!.GetValue<string>().ShouldBe("Ships at sea");
        suggestion["metaDescription"]!.GetValue<string>().ShouldBe("What ships are and where they sail.");
        Text(_ai.Chat.Calls.ShouldHaveSingleItem().Messages[0]).ShouldContain("\"metaTitle\"");
    }

    [Fact]
    public async Task AnAnswerThatIsNotWhatWasAskedForIsAProblemAndAFailedCall()
    {
        var site = await _ai.SiteAsync();
        using var admin = await _ai.AdminAsync(site);

        _ai.Chat.Answer = "A title and a description, but no object.";
        using var noObject = await ActAsync(admin, "seo", new { type = "article", field = "summary", text = "Ships are big boats." });
        _ai.Chat.Answer = "   ";
        using var noText = await ActAsync(admin, "rewrite", new { type = "article", field = "summary", text = "Ships are big boats." });

        (await RefusedAsync(noObject, HttpStatusCode.BadGateway)).ShouldBe("FCMS1690");
        (await RefusedAsync(noText, HttpStatusCode.BadGateway)).ShouldBe("FCMS1690");
        (await _ai.UsageAsync(site)).Select(usage => usage.Succeeded).ShouldBe([false, false]);
    }

    [Fact]
    public async Task AltTextDescribesAReducedCopyOfAnImageOfTheMediaLibraryReadAsTheCaller()
    {
        var site = await _ai.SiteAsync();
        using var admin = await _ai.AdminAsync(site);
        var image = await UploadAsync(admin, "harbour.png", Picture(2000, 1000));
        var document = await UploadAsync(admin, "notes.pdf", "%PDF-1.7\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n"u8.ToArray());
        using var blind = await _ai.CallerAsync(site, (AiPermissions.Use, null), ("content.article.read", null), ("content.article.update", null));
        _ai.Chat.Answer = "Ships in a harbour at dawn.";

        var answer = await SuggestAsync(admin, "altText", new { type = "article", field = "summary", locale = "en", options = new { mediaId = image } });
        using var noMedia = await ActAsync(admin, "altText", new { type = "article", field = "summary" });
        using var notAnImage = await ActAsync(admin, "altText", new { type = "article", field = "summary", options = new { mediaId = document } });
        using var noAsset = await ActAsync(admin, "altText", new { type = "article", field = "summary", options = new { mediaId = Guid.CreateVersion7() } });
        using var mayNotRead = await ActAsync(blind, "altText", new { type = "article", field = "summary", options = new { mediaId = image } });

        answer["suggestion"]!.GetValue<string>().ShouldBe("Ships in a harbour at dawn.");
        var call = _ai.Chat.Calls.ShouldHaveSingleItem();
        Text(call.Messages[0]).ShouldContain("alternative text");
        Text(call.Messages[0]).ShouldContain("locale \"en\"");
        var sent = call.Messages[1].Contents.ShouldHaveSingleItem().ShouldBeOfType<DataContent>();
        sent.MediaType.ShouldBe("image/png");
        using var copy = SKBitmap.Decode(sent.Data.ToArray());
        (copy.Width, copy.Height).ShouldBe((1024, 512));

        (await RefusedAsync(noMedia, HttpStatusCode.UnprocessableEntity)).ShouldBe("FCMS1680");
        (await RefusedAsync(notAnImage, HttpStatusCode.UnprocessableEntity)).ShouldBe("FCMS1691");
        noAsset.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        mayNotRead.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AltTextIsRefusedWhereTheMediaPluginIsDisabled()
    {
        var site = await _ai.SiteAsync();
        using var admin = await _ai.AdminAsync(site);
        var image = await UploadAsync(admin, "harbour.png", Picture(40, 20));
        await _ai.SetEnabledAsync("media", site, false);

        using var refused = await ActAsync(admin, "altText", new { type = "article", field = "summary", options = new { mediaId = image } });

        (await RefusedAsync(refused, HttpStatusCode.UnprocessableEntity)).ShouldBe("FCMS1691");
        _ai.Chat.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnUnknownActionIsNotFound()
    {
        var site = await _ai.SiteAsync();
        using var admin = await _ai.AdminAsync(site);

        using var response = await ActAsync(admin, "paraphrase", new { type = "article", field = "summary", text = "Ships are big boats." });

        (await RefusedAsync(response, HttpStatusCode.NotFound)).ShouldBe("FCMS1681");
    }

    private static async Task<string> EntryAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync(new Uri($"/api/management/v1/content/article/{id:D}", UriKind.Relative), Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync(Token);
    }
}
