using System.Text.Json;
using System.Text.RegularExpressions;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Content;
using FluentCMS.Core.Schema;
using Microsoft.Playwright;

namespace FluentCMS.IntegrationTests.Browser;

public sealed class FormPickerTests
{
    // FRM-01 by keyboard alone: a person finds the referenced product by typing, chooses it with the arrow keys and saves the page.
    [BrowserFact]
    public async Task A_person_picks_a_referenced_entry_by_search_with_the_keyboard_and_saves()
    {
        await using var session = await BrowserSession.StartAsync();
        var drill = await ModelAsync(session);
        var page = session.Page;

        await session.SignInWithKeyboardAsync();
        await page.GotoAsync($"{session.BaseUrl}/content/product-page/new");
        var search = page.Locator("[data-picker=product] input[type=search]");
        await Assertions.Expect(search).ToBeVisibleAsync();

        await page.Locator("input[name='Form.Values[title]']").FocusAsync();
        await page.Keyboard.TypeAsync("Drills");
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(search).ToBeFocusedAsync();
        await page.Keyboard.TypeAsync("cordless");
        await Assertions.Expect(page.Locator("[data-picker=product] [role=option]")).ToHaveCountAsync(1);
        await page.Keyboard.PressAsync("ArrowDown");
        await Assertions.Expect(page.Locator("[data-picker=product] [role=option]")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator("[data-picker=product] input[type=hidden]")).ToHaveValueAsync(drill.ToString());

        await page.Locator("textarea[name='Form.Rationale']").FocusAsync();
        await page.Keyboard.TypeAsync("Add the drills page.");
        await page.Keyboard.PressAsync("Tab");
        await page.Keyboard.PressAsync("Tab");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForURLAsync(new Regex("/sets/[0-9a-f-]{36}\\?done=saved$"));

        var setId = Guid.Parse(new Uri(page.Url).AbsolutePath["/sets/".Length..]);
        var set = await session.Host.AsUserAsync<IChangeSetDirectory, ChangeSet>(session.OwnerId, async sets => (await sets.GetAsync(setId)).Value);
        var entry = await session.Host.AsUserAsync<IEntryDirectory, Entry>(session.OwnerId, async entries => (await entries.GetAsync(set.Operations[0].ItemId)).Value);
        Assert.Equal(drill.ToString(), entry.Latest.Fields.GetProperty("product").GetString());
    }

    private static async Task<Guid> ModelAsync(BrowserSession session)
    {
        await session.Host.AsUserAsync<IContentTypeDirectory, ContentType>(session.OwnerId, async types => (await types.CreateAsync(new NewContentType(
            "Product", "product", new ContentTypeDefinition([new FieldDefinition("name", "text", required: true)])), TestChangeSetTargets.ContentModel)).Value);
        await session.Host.AsUserAsync<IContentTypeDirectory, ContentType>(session.OwnerId, async types => (await types.CreateAsync(new NewContentType(
            "Product page",
            "product-page",
            new ContentTypeDefinition(
            [
                new FieldDefinition("title", "text", required: true),
                new FieldDefinition("product", "reference", settings: JsonDocument.Parse("""{"target": "product"}""").RootElement),
            ])), TestChangeSetTargets.ContentModel)).Value);
        await session.Host.AsUserAsync<IEntryDirectory, Entry>(session.OwnerId, async entries =>
            (await entries.CreateAsync(new NewEntry("product", JsonDocument.Parse("""{"name": "Hammer"}""").RootElement))).Value);
        var drill = await session.Host.AsUserAsync<IEntryDirectory, Entry>(session.OwnerId, async entries =>
            (await entries.CreateAsync(new NewEntry("product", JsonDocument.Parse("""{"name": "Cordless drill"}""").RootElement))).Value);
        return drill.Id;
    }
}
