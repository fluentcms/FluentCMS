using System.Diagnostics;
using System.Text.RegularExpressions;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Schema;
using Microsoft.Playwright;

namespace FluentCMS.IntegrationTests.Browser;

public sealed class KeyboardReviewTests
{
    // UX-02 and the epic's S1 path: a routine ten-item set is reviewed and approved with the keyboard alone in under two minutes.
    [BrowserFact]
    public async Task A_reviewer_steps_through_a_ten_item_set_and_approves_it_with_the_keyboard_alone()
    {
        await using var session = await BrowserSession.StartAsync();
        var routine = await AwaitingReviewAsync(session, "routine", 10);
        var following = await AwaitingReviewAsync(session, "following", 1);
        var page = session.Page;
        var clock = Stopwatch.StartNew();

        await session.SignInWithKeyboardAsync();
        await page.Keyboard.PressAsync("j");
        Assert.Equal(routine.Rationale, await FocusedTextAsync(page));
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForURLAsync(new Regex($"/sets/{routine.Id}\\?queue=awaiting-me$"));

        // The circuit replaces the prerendered items when it attaches, which would take focus away from a summary.
        await Assertions.Expect(page.Locator("[data-testid=change-set-items][data-interactive=true]")).ToBeVisibleAsync();
        var summaries = page.Locator("[data-slot=diff-view-item-summary]");
        await Assertions.Expect(summaries).ToHaveCountAsync(10);
        for (var item = 0; item < 10; item++)
        {
            await page.Keyboard.PressAsync("j");
            Assert.Equal(item, await page.EvaluateAsync<int>("[...document.querySelectorAll('[data-slot=diff-view-item-summary]')].indexOf(document.activeElement)"));
        }

        // Only the summary with focus is in the tab order, and J at the end stays on the last item.
        Assert.Equal(1, await page.EvaluateAsync<int>("[...document.querySelectorAll('[data-slot=diff-view-item-summary]')].filter(s => s.tabIndex === 0).length"));
        await page.Keyboard.PressAsync("k");
        Assert.Equal(8, await page.EvaluateAsync<int>("[...document.querySelectorAll('[data-slot=diff-view-item-summary]')].indexOf(document.activeElement)"));

        await page.Keyboard.PressAsync("a");
        await page.WaitForURLAsync(new Regex($"/sets/{following.Id}\\?queue=awaiting-me&done=approved$"));
        clock.Stop();

        await Assertions.Expect(page.GetByTestId("review-done")).ToBeVisibleAsync();
        Assert.Equal(ChangeSetState.Approved, (await StateAsync(session, routine.Id)));
        Assert.True(clock.Elapsed < TimeSpan.FromMinutes(2), $"The review took {clock.Elapsed}.");
    }

    [BrowserFact]
    public async Task A_reviewer_rejects_a_set_with_a_reason_by_keyboard_and_returns_to_the_empty_queue()
    {
        await using var session = await BrowserSession.StartAsync();
        var set = await AwaitingReviewAsync(session, "prices", 2);
        var page = session.Page;

        await session.SignInWithKeyboardAsync();
        await page.Keyboard.PressAsync("2");
        await page.WaitForURLAsync(new Regex("\\?queue=awaiting-anyone$"));
        await Assertions.Expect(page.Locator("[data-current-queue=awaiting-anyone]")).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("j");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForURLAsync(new Regex($"/sets/{set.Id}\\?queue=awaiting-anyone$"));

        // Enhanced navigation changes the address before it patches the page, so wait for the set's panel.
        await Assertions.Expect(page.Locator("[data-action=reject]")).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("r");
        await Assertions.Expect(page.Locator("textarea[name='Reject.Reason']")).ToBeFocusedAsync();
        await page.Keyboard.TypeAsync("The prices are from last year.");
        await page.Keyboard.PressAsync("Tab");
        await page.Keyboard.PressAsync("Tab");
        await page.Keyboard.PressAsync("Enter");

        await page.WaitForURLAsync(new Regex("/\\?queue=awaiting-anyone&done=rejected$"));
        await Assertions.Expect(page.GetByTestId("desk-done")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("desk-empty")).ToBeVisibleAsync();
        Assert.Equal(ChangeSetState.Rejected, await StateAsync(session, set.Id));
    }

    [BrowserFact]
    public async Task Shortcuts_wait_while_a_field_has_focus_and_the_help_lists_them()
    {
        await using var session = await BrowserSession.StartAsync();
        var set = await AwaitingReviewAsync(session, "product", 1);
        var page = session.Page;

        await session.SignInWithKeyboardAsync();
        await page.GotoAsync($"{session.BaseUrl}/sets/{set.Id}?queue=awaiting-me");
        await page.Keyboard.PressAsync("r");
        await page.Keyboard.TypeAsync("a j k");
        Assert.Equal("a j k", await page.Locator("textarea[name='Reject.Reason']").InputValueAsync());
        await page.Keyboard.PressAsync("Escape");

        await page.Keyboard.PressAsync("?");
        await Assertions.Expect(page.GetByTestId("shortcut-list")).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("a");
        Assert.Equal(ChangeSetState.AwaitingReview, await StateAsync(session, set.Id));
        await page.Keyboard.PressAsync("Escape");

        await page.Keyboard.PressAsync("b");
        await page.WaitForURLAsync(new Regex("/\\?queue=awaiting-me$"));
    }

    // WCAG 2.1 SC 2.1.4: single-key shortcuts turn off in the help, and stay off in this browser after a reload.
    [BrowserFact]
    public async Task Single_key_shortcuts_turn_off_in_the_help_and_stay_off_after_a_reload()
    {
        await using var session = await BrowserSession.StartAsync();
        var set = await AwaitingReviewAsync(session, "product", 2);
        var page = session.Page;

        await session.SignInWithKeyboardAsync();
        await page.GotoAsync($"{session.BaseUrl}/sets/{set.Id}?queue=awaiting-me");
        await Assertions.Expect(page.Locator("[data-testid=change-set-items][data-interactive=true]")).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("?");
        var toggle = page.Locator("[data-ui=single-key-shortcuts]");
        await Assertions.Expect(toggle).ToBeCheckedAsync();
        await toggle.FocusAsync();
        await page.Keyboard.PressAsync("Space");
        await Assertions.Expect(toggle).Not.ToBeCheckedAsync();
        await page.Keyboard.PressAsync("Escape");

        await page.Keyboard.PressAsync("j");
        await page.Keyboard.PressAsync("a");
        Assert.Equal(-1, await page.EvaluateAsync<int>("[...document.querySelectorAll('[data-slot=diff-view-item-summary]')].indexOf(document.activeElement)"));
        Assert.Equal(ChangeSetState.AwaitingReview, await StateAsync(session, set.Id));

        await page.ReloadAsync();
        await Assertions.Expect(page.Locator("[data-action=approve]")).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("a");
        await page.Keyboard.PressAsync("?");
        Assert.Equal(ChangeSetState.AwaitingReview, await StateAsync(session, set.Id));
        await Assertions.Expect(page.GetByTestId("shortcut-list")).Not.ToBeVisibleAsync();
        await page.GetByTestId("shortcut-help").ClickAsync();
        await Assertions.Expect(page.Locator("[data-ui=single-key-shortcuts]")).Not.ToBeCheckedAsync();
    }

    [BrowserFact]
    public async Task Arrow_keys_home_and_end_move_through_desk_rows_and_set_items()
    {
        await using var session = await BrowserSession.StartAsync();
        var large = await AwaitingReviewAsync(session, "large", 5);
        await AwaitingReviewAsync(session, "second", 1);
        await AwaitingReviewAsync(session, "third", 1);
        var page = session.Page;

        await session.SignInWithKeyboardAsync();
        var rows = "[...document.querySelectorAll('[data-change-set]')].findIndex(row => row.contains(document.activeElement))";
        await page.Keyboard.PressAsync("j");
        Assert.Equal(0, await page.EvaluateAsync<int>(rows));
        await page.Keyboard.PressAsync("ArrowDown");
        Assert.Equal(1, await page.EvaluateAsync<int>(rows));
        await page.Keyboard.PressAsync("End");
        Assert.Equal(2, await page.EvaluateAsync<int>(rows));
        await page.Keyboard.PressAsync("ArrowUp");
        Assert.Equal(1, await page.EvaluateAsync<int>(rows));
        await page.Keyboard.PressAsync("Home");
        Assert.Equal(0, await page.EvaluateAsync<int>(rows));

        await page.GotoAsync($"{session.BaseUrl}/sets/{large.Id}");
        await Assertions.Expect(page.Locator("[data-testid=change-set-items][data-interactive=true]")).ToBeVisibleAsync();
        var items = "[...document.querySelectorAll('[data-slot=diff-view-item-summary]')].indexOf(document.activeElement)";
        await page.Keyboard.PressAsync("j");
        Assert.Equal(0, await page.EvaluateAsync<int>(items));
        await page.Keyboard.PressAsync("ArrowDown");
        Assert.Equal(1, await page.EvaluateAsync<int>(items));
        await page.Keyboard.PressAsync("End");
        Assert.Equal(4, await page.EvaluateAsync<int>(items));
        await page.Keyboard.PressAsync("Home");
        Assert.Equal(0, await page.EvaluateAsync<int>(items));
    }

    // Outside any dialog, a key typed into a field types: B in a generated form's title does not go back.
    [BrowserFact]
    public async Task A_shortcut_key_typed_into_a_form_field_types_and_does_not_act()
    {
        await using var session = await BrowserSession.StartAsync();
        await session.Host.AsUserAsync<IContentTypeDirectory, ContentType>(session.OwnerId, async types => (await types.CreateAsync(
            new NewContentType("Note", "note", new ContentTypeDefinition([new FieldDefinition("title", "text")])), TestChangeSetTargets.ContentModel)).Value);
        var page = session.Page;

        await session.SignInWithKeyboardAsync();
        await page.GotoAsync($"{session.BaseUrl}/content/note/new");
        await page.Locator("input[name='Form.Values[title]']").FocusAsync();
        await page.Keyboard.TypeAsync("Big bold jab");

        Assert.Equal("Big bold jab", await page.Locator("input[name='Form.Values[title]']").InputValueAsync());
        Assert.EndsWith("/content/note/new", page.Url);
    }

    // After a decision the next set is the one that followed this one, not the head of the queue the reviewer skipped past.
    [BrowserFact]
    public async Task Skipping_a_set_and_approving_the_next_lands_on_the_set_after_it()
    {
        await using var session = await BrowserSession.StartAsync();
        var first = await AwaitingReviewAsync(session, "first", 1);
        var second = await AwaitingReviewAsync(session, "second", 1);
        var third = await AwaitingReviewAsync(session, "third", 1);
        var page = session.Page;

        await session.SignInWithKeyboardAsync();
        await page.GotoAsync($"{session.BaseUrl}/sets/{first.Id}?queue=awaiting-me");
        await Assertions.Expect(page.Locator("[data-action=approve]")).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("n");
        await page.WaitForURLAsync(new Regex($"/sets/{second.Id}\\?queue=awaiting-me$"));
        await Assertions.Expect(page.Locator($"[data-testid=next-in-queue][href*='{third.Id}']")).ToBeAttachedAsync();
        await page.Keyboard.PressAsync("a");

        await page.WaitForURLAsync(new Regex($"/sets/{third.Id}\\?queue=awaiting-me&done=approved$"));
        Assert.Equal((ChangeSetState.AwaitingReview, ChangeSetState.Approved), (await StateAsync(session, first.Id), await StateAsync(session, second.Id)));
    }

    private static async Task<string?> FocusedTextAsync(IPage page)
    {
        return (await page.EvaluateAsync<string?>("document.activeElement?.textContent"))?.Trim();
    }

    private static Task<ChangeSetState> StateAsync(BrowserSession session, Guid id)
    {
        return session.Host.AsUserAsync<IChangeSetDirectory, ChangeSetState>(session.OwnerId, async sets => (await sets.GetAsync(id)).Value.State);
    }

    // A set awaiting review with one new content type per item.
    private static async Task<ChangeSet> AwaitingReviewAsync(BrowserSession session, string name, int items)
    {
        var types = new List<ContentType>();
        for (var index = 0; index < items; index++)
        {
            var slug = $"{name}-{index}";
            types.Add(await session.Host.AsUserAsync<IContentTypeDirectory, ContentType>(session.OwnerId, async directory =>
                (await directory.CreateAsync(new NewContentType(slug, slug, new ContentTypeDefinition([new FieldDefinition("title", "text")])), TestChangeSetTargets.ContentModel)).Value));
        }

        return await session.Host.AsUserAsync<IChangeSetDirectory, ChangeSet>(session.OwnerId, async directory =>
        {
            var set = (await directory.OpenAsync(new NewChangeSet($"Add the {name} types.", []))).Value;
            foreach (var type in types)
            {
                Assert.True((await directory.AddOperationAsync(set.Id, ChangeOperation.Create(OperationKind.Create, ItemKind.Schema, type.Id, null, 1).Value)).IsSuccess);
            }

            await directory.TransitionAsync(set.Id, ChangeSetState.ChecksRunning);
            return (await directory.TransitionAsync(set.Id, ChangeSetState.AwaitingReview)).Value;
        });
    }
}
