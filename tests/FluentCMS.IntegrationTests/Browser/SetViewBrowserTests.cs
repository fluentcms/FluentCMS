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
