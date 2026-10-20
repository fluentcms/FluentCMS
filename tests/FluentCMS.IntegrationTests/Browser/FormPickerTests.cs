using System.Text.Json;
using System.Text.RegularExpressions;
using FluentCMS.Admin.Components.Forms;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Content;
using FluentCMS.Core.Schema;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.Playwright;

namespace FluentCMS.IntegrationTests.Browser;

public sealed class FormPickerTests
{
    // FRM-01 by keyboard alone, from the top of the page: a person finds the referenced product by typing at full speed, chooses it
    // with the keyboard and saves. The target type holds 500 entries, more than a circuit's first message could carry as options.
    [BrowserFact]
    public async Task A_person_picks_a_referenced_entry_by_search_with_the_keyboard_alone_and_saves()
    {
        await using var database = await TemporaryDatabase.CreateAsync(Provider);
        await using var session = await BrowserSession.StartAsync(database.Configuration);
        var drill = await ModelAsync(session, products: 500);
        var page = session.Page;

        await session.SignInWithKeyboardAsync();
        await page.GotoAsync($"{session.BaseUrl}/content/product-page/new");
        var search = page.Locator("[data-picker=product] input[type=search]");
        await Assertions.Expect(search).ToBeVisibleAsync();

        await TabToAsync(page, "input[name='Form.Values[title]']");
        await page.Keyboard.TypeAsync("Drills");
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(search).ToBeFocusedAsync();
        await page.Keyboard.TypeAsync("cordless drill");
        await Assertions.Expect(search).ToHaveValueAsync("cordless drill");
        await Assertions.Expect(page.Locator("[data-picker=product] [role=option]")).ToHaveCountAsync(1);

        // Enter chooses the only match and keeps focus in the box; it never submits the form.
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator("[data-picker=product] input[type=hidden]")).ToHaveValueAsync(drill.ToString());
        await Assertions.Expect(page.Locator("[data-testid=picker-choice]")).ToContainTextAsync("Cordless drill");
        await Assertions.Expect(search).ToBeFocusedAsync();
        await Assertions.Expect(search).ToHaveValueAsync("");
        Assert.EndsWith("/content/product-page/new", page.Url);

        await TabToAsync(page, "textarea[name='Form.Rationale']");
        await page.Keyboard.TypeAsync("Add the drills page.");
        await TabToAsync(page, "[data-action=save]");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForURLAsync(new Regex("/sets/[0-9a-f-]{36}\\?done=saved$"));

        var setId = Guid.Parse(new Uri(page.Url).AbsolutePath["/sets/".Length..]);
        var set = await session.Host.AsUserAsync<IChangeSetDirectory, ChangeSet>(session.OwnerId, async sets => (await sets.GetAsync(setId)).Value);
        var entry = await session.Host.AsUserAsync<IEntryDirectory, Entry>(session.OwnerId, async entries => (await entries.GetAsync(set.Operations[0].ItemId)).Value);
        Assert.Equal(drill.ToString(), entry.Latest.Fields.GetProperty("product").GetString());
    }

    // With no pause, every key runs a search, and the searches overlap; each reads through its own scope, so none ends the circuit.
    [BrowserFact]
    public async Task Overlapping_searches_never_end_the_circuit_and_the_newest_one_shows()
    {
        await using var database = await TemporaryDatabase.CreateAsync(Provider);
        await using var session = await BrowserSession.StartAsync(database.Configuration);
        await ModelAsync(session, products: 20);
        var page = session.Page;
        var pause = PickerField.SearchPause;
        PickerField.SearchPause = TimeSpan.Zero;
        try
        {
            await session.SignInWithKeyboardAsync();
            await page.GotoAsync($"{session.BaseUrl}/content/product-page/new");
            var search = page.Locator("[data-picker=product] input[type=search]");
            await Assertions.Expect(search).ToBeVisibleAsync();

            await search.PressSequentiallyAsync("product number 1");
            await Assertions.Expect(page.Locator("[data-picker=product] [role=option]").First).ToContainTextAsync("Product number 1");
            await search.FillAsync("");
            await search.PressSequentiallyAsync("cordless");
            await Assertions.Expect(page.Locator("[data-picker=product] [role=option]")).ToHaveCountAsync(1);
            await Assertions.Expect(page.Locator("#components-reconnect-modal")).ToBeHiddenAsync();
        }
        finally
        {
            PickerField.SearchPause = pause;
        }
    }

    // Enter answers what the box holds now: matches still shown for an earlier word are not chosen while the new search runs.
    [BrowserFact]
    public async Task Enter_never_chooses_a_match_from_an_earlier_search()
    {
        await using var session = await BrowserSession.StartAsync();
        await ModelAsync(session, products: 3);
        var page = session.Page;
        var pause = PickerField.SearchPause;
        PickerField.SearchPause = TimeSpan.FromSeconds(2);
        try
        {
            await session.SignInWithKeyboardAsync();
            await page.GotoAsync($"{session.BaseUrl}/content/product-page/new");
            var search = page.Locator("[data-picker=product] input[type=search]");
            await search.PressSequentiallyAsync("cordless");
            await Assertions.Expect(page.Locator("[data-picker=product] [role=option]")).ToHaveCountAsync(1);

            await search.PressAsync("Control+a");
            await search.PressSequentiallyAsync("product number");
            await search.PressAsync("Enter");

            await Assertions.Expect(page.Locator("[data-picker=product] input[type=hidden]")).ToHaveValueAsync("");
            await Assertions.Expect(search).ToHaveValueAsync("product number");
            Assert.EndsWith("/content/product-page/new", page.Url);
        }
        finally
        {
            PickerField.SearchPause = pause;
        }
    }

    // A circuit paused and resumed, as after a laptop sleeps, keeps the choice; keys typed while it was paused never reach the server,
    // and Enter still chooses afterwards because it compares the text the matches answer, not a count of keys.
    [BrowserFact]
    public async Task A_paused_and_resumed_circuit_keeps_the_choice_and_enter_still_chooses()
    {
        await using var session = await BrowserSession.StartAsync();
        var drill = await ModelAsync(session, products: 3);
        var page = session.Page;

        await session.SignInWithKeyboardAsync();
        await page.GotoAsync($"{session.BaseUrl}/content/product-page/new");
        var search = page.Locator("[data-picker=product] input[type=search]");
        var choice = page.Locator("[data-picker=product] input[type=hidden]");
        await search.PressSequentiallyAsync("cordless");
        await Assertions.Expect(page.Locator("[data-picker=product] [role=option]")).ToHaveCountAsync(1);
        await search.PressAsync("Enter");
        await Assertions.Expect(choice).ToHaveValueAsync(drill.ToString());

        await page.EvaluateAsync("Blazor.pauseCircuit()");
        await search.PressSequentiallyAsync("product");
        await page.EvaluateAsync("Blazor.resumeCircuit()");
        await Assertions.Expect(search).ToBeEnabledAsync();
        await Assertions.Expect(choice).ToHaveValueAsync(drill.ToString());
        await Assertions.Expect(page.Locator("[data-testid=picker-choice]")).ToContainTextAsync("Cordless drill");

        await page.Locator("[data-picker=product] [data-action=clear]").ClickAsync();
        await Assertions.Expect(choice).ToHaveValueAsync("");
        await search.FillAsync("");
        await search.PressSequentiallyAsync("cordless");
        await Assertions.Expect(page.Locator("[data-picker=product] [role=option]")).ToHaveCountAsync(1);
        await search.PressAsync("Enter");
        await Assertions.Expect(choice).ToHaveValueAsync(drill.ToString());
    }

    // Enhanced navigation to another version of the entry gives the picker that version's value: starting from the published version
    // saves its reference, not the rejected draft's the picker showed before.
    [BrowserFact]
    public async Task Starting_from_the_published_version_gives_the_picker_its_value()
    {
        await using var session = await BrowserSession.StartAsync();
        var drill = await ModelAsync(session, products: 1);
        var hammer = await session.Host.AsUserAsync<IEntryDirectory, Entry>(session.OwnerId, async entries =>
            (await entries.CreateAsync(new NewEntry("product", JsonDocument.Parse("""{"name": "Hammer"}""").RootElement))).Value);
        var page = await session.Host.AsUserAsync<IEntryDirectory, Entry>(session.OwnerId, async entries => (await entries.CreateAsync(new NewEntry(
            "product-page", JsonDocument.Parse($$"""{"title": "Published", "product": "{{drill}}"}""").RootElement))).Value);
        await session.Host.AsUserAsync<IChangeSetDirectory, ChangeSet>(session.OwnerId, async sets =>
        {
            var set = (await sets.OpenAsync(new NewChangeSet("Publish the page.", []))).Value;
            await sets.AddOperationAsync(set.Id, ChangeOperation.Create(OperationKind.Create, ItemKind.Entry, page.Id, null, 1).Value);
            await sets.TransitionAsync(set.Id, ChangeSetState.ChecksRunning);
            await sets.TransitionAsync(set.Id, ChangeSetState.Approved);
            return (await sets.PublishAsync(set.Id)).Value;
        });
        await session.Host.AsUserAsync<IEntryDirectory, Entry>(session.OwnerId, async entries => (await entries.UpdateAsync(
            page.Id, 1, JsonDocument.Parse($$"""{"title": "Rejected draft", "product": "{{hammer.Id}}"}""").RootElement)).Value);
        var browser = session.Page;

        await session.SignInWithKeyboardAsync();
        await browser.GotoAsync($"{session.BaseUrl}/content/product-page/{page.Id}");
        var choice = browser.Locator("[data-picker=product] input[type=hidden]");
        await Assertions.Expect(choice).ToHaveValueAsync(hammer.Id.ToString());
        await browser.GetByTestId("from-published").ClickAsync();
        await Assertions.Expect(browser.Locator("input[name='Form.Values[title]']")).ToHaveValueAsync("Published");
        await Assertions.Expect(choice).ToHaveValueAsync(drill.ToString());

        await browser.Locator("textarea[name='Form.Rationale']").FillAsync("Back to the published page.");
        await browser.Locator("[data-action=save]").ClickAsync();
        await browser.WaitForURLAsync(new Regex("/sets/[0-9a-f-]{36}\\?done=saved$"));
        var saved = await session.Host.AsUserAsync<IEntryDirectory, Entry>(session.OwnerId, async entries => (await entries.GetAsync(page.Id)).Value);
        Assert.Equal(drill.ToString(), saved.Latest.Fields.GetProperty("product").GetString());
    }

    // A circuit the server no longer knows ends the search, not the form: nothing reloads, what was typed stays, and the choice made
    // before the loss is saved with the form.
    [BrowserFact]
    public async Task A_lost_circuit_keeps_the_form_and_what_was_typed()
    {
        await using var session = await BrowserSession.StartAsync();
        var drill = await ModelAsync(session, products: 1);
        var page = session.Page;

        await session.SignInWithKeyboardAsync();
        await page.GotoAsync($"{session.BaseUrl}/content/product-page/new");
        var search = page.Locator("[data-picker=product] input[type=search]");
        await search.PressSequentiallyAsync("cordless");
        await page.Locator("[data-picker=product] [role=option]").ClickAsync();
        await Assertions.Expect(page.Locator("[data-picker=product] input[type=hidden]")).ToHaveValueAsync(drill.ToString());
        await page.Locator("input[name='Form.Values[title]']").FillAsync("Typed before the loss");
        await page.EvaluateAsync("document.dispatchEvent(new CustomEvent('components-reconnect-state-changed', { detail: { state: 'rejected' } }))");

        await Assertions.Expect(page.Locator("[data-testid=reconnect-lost]")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("input[name='Form.Values[title]']")).ToHaveValueAsync("Typed before the loss");
        await Assertions.Expect(search).ToBeDisabledAsync();

        await page.Locator("textarea[name='Form.Rationale']").FillAsync("Saved after the loss.");
        await page.Locator("[data-action=save]").ClickAsync();
        await page.WaitForURLAsync(new Regex("/sets/[0-9a-f-]{36}\\?done=saved$"));
        var setId = Guid.Parse(new Uri(page.Url).AbsolutePath["/sets/".Length..]);
        var set = await session.Host.AsUserAsync<IChangeSetDirectory, ChangeSet>(session.OwnerId, async sets => (await sets.GetAsync(setId)).Value);
        var entry = await session.Host.AsUserAsync<IEntryDirectory, Entry>(session.OwnerId, async entries => (await entries.GetAsync(set.Operations[0].ItemId)).Value);
        Assert.Equal(("Typed before the loss", drill.ToString()), (entry.Latest.Fields.GetProperty("title").GetString(), entry.Latest.Fields.GetProperty("product").GetString()));
    }

    // Any page with typed input is kept, such as a rejection reason on the set view, whose diff is an island too.
    [BrowserFact]
    public async Task A_lost_circuit_keeps_a_page_with_typed_input_even_without_a_picker()
    {
        await using var session = await BrowserSession.StartAsync();
        var page = session.Page;
        var type = await session.Host.AsUserAsync<IContentTypeDirectory, ContentType>(session.OwnerId, async types => (await types.CreateAsync(new NewContentType(
            "Brand", "brand", new ContentTypeDefinition([new FieldDefinition("name", "text")])), TestChangeSetTargets.ContentModel)).Value);
        var set = await session.Host.AsUserAsync<IChangeSetDirectory, ChangeSet>(session.OwnerId, async sets =>
        {
            var opened = (await sets.OpenAsync(new NewChangeSet("Add the brand type.", []))).Value;
            await sets.AddOperationAsync(opened.Id, ChangeOperation.Create(OperationKind.Create, ItemKind.Schema, type.Id, null, 1).Value);
            await sets.TransitionAsync(opened.Id, ChangeSetState.ChecksRunning);
            return (await sets.TransitionAsync(opened.Id, ChangeSetState.AwaitingReview)).Value;
        });

        await session.SignInWithKeyboardAsync();
        await page.GotoAsync($"{session.BaseUrl}/sets/{set.Id}");
        await page.Locator("[data-action=reject]").ClickAsync();
        await page.Locator("textarea[name='Reject.Reason']").FillAsync("Typed before the loss");
        await page.EvaluateAsync("document.dispatchEvent(new CustomEvent('components-reconnect-state-changed', { detail: { state: 'rejected' } }))");

        await Assertions.Expect(page.Locator("[data-testid=reconnect-lost]")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("textarea[name='Reject.Reason']")).ToHaveValueAsync("Typed before the loss");
    }

    private static string Provider => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresFactAttribute.VariableName))
        ? DatabaseProviders.Sqlite
        : DatabaseProviders.PostgreSql;

    // Presses Tab until the element has focus, as a keyboard user reaches a field.
    private static async Task TabToAsync(IPage page, string selector)
    {
        for (var press = 0; press < 40; press++)
        {
            if (await page.Locator(selector).EvaluateAsync<bool>("element => element === document.activeElement"))
            {
                return;
            }

            await page.Keyboard.PressAsync("Tab");
        }

        Assert.Fail($"Tab never reached {selector}.");
    }

    private static async Task<Guid> ModelAsync(BrowserSession session, int products)
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
        await session.Host.AsUserAsync<IEntryDirectory, bool>(session.OwnerId, async entries =>
        {
            for (var number = 1; number < products; number++)
            {
                await entries.CreateAsync(new NewEntry("product", JsonDocument.Parse($$"""{"name": "Product number {{number}}"}""").RootElement));
            }

            return true;
        });
        var drill = await session.Host.AsUserAsync<IEntryDirectory, Entry>(session.OwnerId, async entries =>
            (await entries.CreateAsync(new NewEntry("product", JsonDocument.Parse("""{"name": "Cordless drill"}""").RootElement))).Value);
        return drill.Id;
    }
}
