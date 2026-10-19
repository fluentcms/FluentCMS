using System.Text.Json;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Content;
using FluentCMS.Core.Identity;
using FluentCMS.Infrastructure.Persistence;
using FluentCMS.IntegrationTests.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Forms;

public sealed class EntryEditsTests
{
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_person_saves_an_entry_into_a_new_open_set_that_holds_its_create_operation(string provider)
    {
        await using var scenario = await CreateAsync(provider);

        var saved = (await SaveAsync(scenario, new EntryEdit("blog-post", null, null, Json("""{"title": "Hello"}"""), new NewChangeSet("Write the welcome post.", ["notes/welcome.md"])))).Value;

        Assert.Equal(ChangeSetState.Open, saved.ChangeSet.State);
        Assert.Equal(("Write the welcome post.", "notes/welcome.md"), (saved.ChangeSet.Rationale, Assert.Single(saved.ChangeSet.Sources)));
        Assert.IsType<UserInitiator>(saved.ChangeSet.Initiator);
        var operation = Assert.Single(saved.ChangeSet.Operations);
        Assert.Equal((OperationKind.Create, ItemKind.Entry, saved.Entry.Id, (int?)null, (int?)1), (operation.Kind, operation.ItemKind, operation.ItemId, operation.BaseVersion, operation.ProposedVersion));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_edit_of_a_draft_never_published_is_a_create_of_its_next_version(string provider)
    {
        await using var scenario = await CreateAsync(provider);
        var first = (await SaveAsync(scenario, new EntryEdit("blog-post", null, null, Json("""{"title": "Hello"}"""), new NewChangeSet("Draft.", [])))).Value;

        var second = (await SaveAsync(scenario, new EntryEdit("blog-post", first.Entry.Id, 1, Json("""{"title": "Hello again"}"""), new NewChangeSet("Retitle.", [])))).Value;
        var stale = await SaveAsync(scenario, new EntryEdit("blog-post", first.Entry.Id, 1, Json("""{"title": "Lost"}"""), new NewChangeSet("Retitle.", [])));

        var operation = Assert.Single(second.ChangeSet.Operations);
        Assert.Equal((OperationKind.Create, (int?)2), (operation.Kind, operation.ProposedVersion));
        Assert.NotEqual(first.ChangeSet.Id, second.ChangeSet.Id);
        Assert.Equal(ContentErrorCodes.VersionConflict, Assert.Single(stale.Errors).Code);
    }

    // The version is written first; a set that cannot be opened rolls it back, so no version exists outside a set (AUD-N1).
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_refused_save_stores_neither_the_version_nor_a_set(string provider)
    {
        await using var scenario = await CreateAsync(provider);

        var noReason = await SaveAsync(scenario, new EntryEdit("blog-post", null, null, Json("""{"title": "Hello"}"""), new NewChangeSet(" ", [])));
        var invalid = await SaveAsync(scenario, new EntryEdit("blog-post", null, null, Json("""{"title": 5}"""), new NewChangeSet("Why.", [])));
        var blankSource = await SaveAsync(scenario, new EntryEdit("blog-post", null, null, Json("""{"title": "Hello"}"""), new NewChangeSet("Why.", ["a.csv", " "])));

        Assert.Equal(ChangeSetErrorCodes.RationaleMissing, Assert.Single(noReason.Errors).Code);
        Assert.Equal(ChangeSetErrorCodes.SourceBlank, Assert.Single(blankSource.Errors).Code);
        Assert.NotEmpty(invalid.Errors);
        Assert.Empty(await scenario.EntryRowsAsync());
        Assert.Equal(0, await scenario.Schema.QueryAsync(context => context.ChangeSets.CountAsync(row => row.SessionId != TestChangeSetTargets.ContentModelSession)));
    }

    // The published version the values came from is checked in the same unit of work as the write, which it rolls back.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_update_from_a_published_version_that_is_not_the_published_one_is_a_conflict(string provider)
    {
        await using var scenario = await CreateAsync(provider);
        var first = (await SaveAsync(scenario, new EntryEdit("blog-post", null, null, Json("""{"title": "Hello"}"""), new NewChangeSet("Draft.", [])))).Value;

        var refused = await SaveAsync(scenario, new EntryEdit("blog-post", first.Entry.Id, 1, Json("""{"title": "Old values"}"""), new NewChangeSet("Retitle.", []))
        {
            PublishedBase = 1,
        });

        Assert.Equal(ContentErrorCodes.VersionConflict, Assert.Single(refused.Errors).Code);
        Assert.Single(await scenario.EntryRowsAsync());
        Assert.Equal(1, await scenario.Schema.QueryAsync(context => context.ChangeSets.CountAsync(row => row.SessionId != TestChangeSetTargets.ContentModelSession)));
    }

    private static async Task<EntryScenario> CreateAsync(string provider)
    {
        var scenario = await EntryScenario.CreateAsync(provider);
        await scenario.Schema.CreateReferenceModelAsync();
        return scenario;
    }

    private static async Task<FluentCMS.Core.Results.Result<SavedEntryEdit>> SaveAsync(EntryScenario scenario, EntryEdit edit)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IEntryEdits>().SaveAsync(edit);
    }

    private static JsonElement Json(string json)
    {
        return JsonSerializer.Deserialize<JsonElement>(json);
    }
}
