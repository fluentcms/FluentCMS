using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Schema;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.Playwright;

namespace FluentCMS.IntegrationTests.Browser;

public sealed class SetViewBrowserTests
{
    // PERF-04: a 200-item set arrives in batches over the circuit. The circuit starts from the items the page arrived with, so the list
    // never empties when it attaches; on PostgreSQL a second read used to show "Loading the items…" in their place.
    [BrowserFact]
    public async Task A_large_set_arrives_in_batches_and_the_list_never_empties_when_the_circuit_attaches()
    {
        var provider = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresFactAttribute.VariableName))
            ? DatabaseProviders.Sqlite
            : DatabaseProviders.PostgreSql;
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        await using var session = await BrowserSession.StartAsync(database.Configuration);
        var set = await AwaitingReviewAsync(session, 200);
        var page = session.Page;
        await session.SignInWithKeyboardAsync();

        // The fewest items shown at any moment after the page arrived with its prerendered items.
        await page.AddInitScriptAsync("""
            document.addEventListener("DOMContentLoaded", () => {
                const shown = () => document.querySelectorAll('[data-slot="diff-view-item"]').length;
                window.fewestItems = shown();
                new MutationObserver(() => { window.fewestItems = Math.min(window.fewestItems, shown()); })
                    .observe(document.body, { childList: true, subtree: true });
            });
            """);
        await page.GotoAsync($"{session.BaseUrl}/sets/{set.Id}");

        await Assertions.Expect(page.Locator("[data-testid=change-set-items][data-interactive=true]")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-slot=diff-view-item]")).ToHaveCountAsync(200);
        Assert.True(await page.EvaluateAsync<double>("window.fewestItems") >= 25, "The list emptied when the circuit attached.");
    }

    // A set whose diff is too large to carry into the circuit still gets its circuit: the circuit reads the diff again.
    [BrowserFact]
    public async Task A_set_whose_diff_is_too_large_to_keep_still_attaches_and_shows_every_item()
    {
        await using var session = await BrowserSession.StartAsync();
        await session.Host.AsUserAsync<IContentTypeDirectory, ContentType>(session.OwnerId, async directory => (await directory.CreateAsync(
            new NewContentType("Page", "page", new ContentTypeDefinition([new FieldDefinition("body", "richText")])), TestChangeSetTargets.ContentModel)).Value);
        var body = new string('x', 6500);
        var entries = new List<Guid>();
        for (var index = 0; index < 100; index++)
        {
            entries.Add(await session.Host.AsUserAsync<FluentCMS.Core.Content.IEntryDirectory, Guid>(session.OwnerId, async directory => (await directory.CreateAsync(
                new FluentCMS.Core.Content.NewEntry("page", System.Text.Json.JsonSerializer.SerializeToElement(new { body = $"{index} {body}" })))).Value.Id));
        }

        var set = await session.Host.AsUserAsync<IChangeSetDirectory, ChangeSet>(session.OwnerId, async directory =>
        {
            var opened = (await directory.OpenAsync(new NewChangeSet("Add a hundred long pages.", []))).Value;
            foreach (var entry in entries)
            {
                await directory.AddOperationAsync(opened.Id, ChangeOperation.Create(OperationKind.Create, ItemKind.Entry, entry, null, 1).Value);
            }

            await directory.TransitionAsync(opened.Id, ChangeSetState.ChecksRunning);
            return (await directory.TransitionAsync(opened.Id, ChangeSetState.AwaitingReview)).Value;
        });
        await session.SignInWithKeyboardAsync();

        await session.Page.GotoAsync($"{session.BaseUrl}/sets/{set.Id}");

        await Assertions.Expect(session.Page.Locator("[data-testid=change-set-items][data-interactive=true]")).ToBeVisibleAsync();
        await Assertions.Expect(session.Page.Locator("[data-slot=diff-view-item]")).ToHaveCountAsync(100);
    }

    private static async Task<ChangeSet> AwaitingReviewAsync(BrowserSession session, int items)
    {
        var types = new List<ContentType>();
        for (var index = 0; index < items; index++)
        {
            var slug = $"type-{index}";
            types.Add(await session.Host.AsUserAsync<IContentTypeDirectory, ContentType>(session.OwnerId, async directory =>
                (await directory.CreateAsync(new NewContentType(slug, slug, new ContentTypeDefinition([new FieldDefinition("title", "text")])), TestChangeSetTargets.ContentModel)).Value));
        }

        return await session.Host.AsUserAsync<IChangeSetDirectory, ChangeSet>(session.OwnerId, async directory =>
        {
            var set = (await directory.OpenAsync(new NewChangeSet("Add two hundred types.", []))).Value;
            foreach (var type in types)
            {
                Assert.True((await directory.AddOperationAsync(set.Id, ChangeOperation.Create(OperationKind.Create, ItemKind.Schema, type.Id, null, 1).Value)).IsSuccess);
            }

            await directory.TransitionAsync(set.Id, ChangeSetState.ChecksRunning);
            return (await directory.TransitionAsync(set.Id, ChangeSetState.AwaitingReview)).Value;
        });
    }
}
