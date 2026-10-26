using System.Text.Json;
using System.Text.RegularExpressions;
using FluentCMS.Core.Content;
using FluentCMS.Core.Schema;
using Microsoft.Playwright;

namespace FluentCMS.IntegrationTests.Browser;

public sealed class EntryFormBrowserTests
{
    // What a real browser posts back for fields the person leaves alone, line breaks included, is saved as it was stored.
    [BrowserFact]
    public async Task A_title_only_edit_in_the_browser_keeps_every_line_break()
    {
        await using var session = await BrowserSession.StartAsync();
        await session.Host.AsUserAsync<IContentTypeDirectory, ContentType>(session.OwnerId, async types => (await types.CreateAsync(
            new NewContentType("Note", "note", new ContentTypeDefinition(
            [
                new FieldDefinition("title", "text", required: true),
                new FieldDefinition("subtitle", "text"),
                new FieldDefinition("body", "richText"),
            ])),
            TestChangeSetTargets.ContentModel)).Value);
        var entry = await session.Host.AsUserAsync<IEntryDirectory, Entry>(session.OwnerId, async entries => (await entries.CreateAsync(new NewEntry(
            "note",
            JsonSerializer.Deserialize<JsonElement>("""{"title": "Drill", "subtitle": "Line one\nLine two", "body": "\nStarts with a line break.\nAnd goes on."}""")))).Value);
        var page = session.Page;

        await session.SignInWithKeyboardAsync();
        await page.GotoAsync($"{session.BaseUrl}/content/note/{entry.Id}");
        Assert.Equal("Line one\nLine two", await page.Locator("textarea[name='Form.Values[subtitle]']").InputValueAsync());
        Assert.Equal("\nStarts with a line break.\nAnd goes on.", await page.Locator("textarea[name='Form.Values[body]']").InputValueAsync());

        await page.Locator("input[name='Form.Values[title]']").FillAsync("Cordless drill");
        await page.Locator("textarea[name='Form.Rationale']").FillAsync("Retitle.");
        await page.Locator("[data-action=save]").ClickAsync();
        await page.WaitForURLAsync(new Regex("/sets/[0-9a-f-]{36}\\?done=saved$"));

        var fields = (await session.Host.AsUserAsync<IEntryDirectory, Entry>(session.OwnerId, async entries => (await entries.GetAsync(entry.Id)).Value)).Latest.Fields;
        Assert.Equal(
            ("Cordless drill", "Line one\nLine two", "\nStarts with a line break.\nAnd goes on."),
            (fields.GetProperty("title").GetString(), fields.GetProperty("subtitle").GetString(), fields.GetProperty("body").GetString()));
    }
}
