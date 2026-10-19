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
