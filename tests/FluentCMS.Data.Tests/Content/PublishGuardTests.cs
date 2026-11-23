using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Content;
using FluentCMS.Core.Content.Hooks;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Hooks;
using FluentCMS.Publishing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

// #3536: a publish reads the draft again before it copies. What another request committed between the two reads is a 412,
// and what this transaction saved there is a hook's. Both are made to land between the reads, by a validator or a hook.
public sealed class PublishGuardTests
{
    // What runs once between the two reads of a publish, with the services of the publish.
    private sealed class Between
    {
        public Func<IServiceProvider, Task>? Run { get; set; }

        // What a hook then changes in the entry it publishes.
        public Action<ContentDocument>? Change { get; set; }

        public async ValueTask RunAsync(IServiceProvider services, ContentDocument? entry = null)
        {
            if (Run is { } run)
            {
                Run = null;
                await run(services);
                if (entry is not null)
                {
                    Change?.Invoke(entry);
                }
            }
        }
    }

    private sealed class BetweenValidator(IServiceProvider services, Between between) : IFieldValidator<object>
    {
        public ValueTask ValidateAsync(object? value, ValidationContext context, CancellationToken cancellationToken) =>
            context.Mode == ValidationMode.Publish ? between.RunAsync(services) : ValueTask.CompletedTask;
    }

    private sealed class BetweenHook(IServiceProvider services, Between between) : IBeforePublishHook<ContentDocument>
    {
        public ValueTask BeforePublishAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) => between.RunAsync(services, context.Entry);
    }

    private static ServiceDatabase WithoutHooks(Between between) => new(cms =>
    {
        cms.Services.AddSingleton(between);
        cms.Services.AddScoped<IFieldValidator<object>, BetweenValidator>();
    });

    private static ServiceDatabase WithHook(Between between) => new(cms =>
    {
        cms.Services.AddSingleton(between);
        cms.Services.AddScoped<IBeforePublishHook<ContentDocument>, BetweenHook>();
    });

    private static IContentService Content(IServiceProvider services) => services.GetRequiredService<IContentService>();

    // English at version 2, since adding French moved it, and French at version 1.
    private static async Task<Guid> EnglishAndFrenchAsync(ServiceDatabase database)
    {
        var id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Hello", ["views"] = 1 }));
        await database.UpdateAsync(Editor, id, new() { ["title"] = "Bonjour" }, 0, locale: "fr");
        return id;
    }

    // At read committed the publish's next read sees what another request committed. A SQLite publish holds the one write
    // lock from its BEGIN, so there it lets go of it for that request, having written nothing yet, and takes it again.
    private static async Task CommittedByAnotherRequestAsync(IServiceProvider publish, Func<Task> write)
    {
        if (!TestProvider.IsSqlite)
        {
            await write();
            return;
        }

        var transaction = publish.GetRequiredService<CmsDbContext>().Database.CurrentTransaction!.GetDbTransaction();
        await using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "COMMIT";
        await command.ExecuteNonQueryAsync(Token);
        await write();
        command.CommandText = "BEGIN IMMEDIATE";
        await command.ExecuteNonQueryAsync(Token);
    }

    private static Task SharedChangeInEnglishAsync(IServiceProvider publish, ServiceDatabase database, Guid id) =>
        CommittedByAnotherRequestAsync(publish, () => database.PatchAsync(Editor, id, new() { ["views"] = 2 }, 2));

    // The publish runs as English, so a write of French from inside it names that locale itself.
    private static async Task PatchAsAsync(IServiceProvider services, string locale, Guid id, ContentDocument patch, int version)
    {
        var accessor = services.GetRequiredService<IOperationContextAccessor>();
        var outer = accessor.Current!;
        accessor.Current = outer with { Locale = locale };
        try
        {
            await Content(services).PatchAsync("article", id, patch, version, Token);
        }
        finally
        {
            accessor.Current = outer;
        }
    }

    private static async Task ShouldConflictWithTheSharedChangeAsync(ServiceDatabase database, Guid id, Func<Task> publish)
    {
        var conflict = await Should.ThrowAsync<VersionConflictException>(publish);

        (conflict.ExpectedVersion, conflict.CurrentVersion).ShouldBe((1, 2));
        var current = conflict.Current.ShouldNotBeNull();
        (current["locale"], current["version"], current["title"], current["views"]).ShouldBe(("fr", 2, "Bonjour", 2L));
        database.Document(id, "fr").PublishedVersion.ShouldBeNull();
        database.PublishedRow("cms_article", id).ShouldBeNull();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(null)]
    public async Task ASharedChangeCommittedBetweenTheReadsIsAConflictWithTheStoredVersion(int? expected)
    {
        var between = new Between();
        using var database = WithoutHooks(between);
        var id = await EnglishAndFrenchAsync(database);
        between.Run = services => SharedChangeInEnglishAsync(services, database, id);

        await ShouldConflictWithTheSharedChangeAsync(database, id, () => database.PublishAsync(Editor, id, "fr", expected));

        between.Run.ShouldBeNull();
    }

    [Fact]
    public async Task ASaveOfThePublishedLocaleCommittedBetweenTheReadsIsAConflictToo()
    {
        var between = new Between();
        using var database = WithoutHooks(between);
        var id = await EnglishAndFrenchAsync(database);
        between.Run = services => CommittedByAnotherRequestAsync(services, () => database.PatchAsync(Editor, id, new() { ["title"] = "Salut" }, 1, locale: "fr"));

        var conflict = await Should.ThrowAsync<VersionConflictException>(() => database.PublishAsync(Editor, id, "fr"));

        (conflict.ExpectedVersion, conflict.CurrentVersion).ShouldBe((1, 2));
        var current = conflict.Current.ShouldNotBeNull();
        (current["version"], current["title"]).ShouldBe((2, "Salut"));
    }

    [Fact]
    public async Task AHookThatWroteAnotherEntryDoesNotMakeTheConflictItsOwn()
    {
        var between = new Between();
        using var database = WithHook(between);
        var id = await EnglishAndFrenchAsync(database);
        between.Run = async services =>
        {
            await SharedChangeInEnglishAsync(services, database, id);
            await Content(services).CreateAsync("author", new() { ["name"] = "Ada" }, Token);
        };

        await ShouldConflictWithTheSharedChangeAsync(database, id, () => database.PublishAsync(Editor, id, "fr"));

        database.Read(context => context.Documents.Count(document => document.Type == "author")).ShouldBe(0);
    }

    [Fact]
    public async Task AHookWhoseSaveOfTheDraftRolledBackDoesNotMakeTheConflictItsOwn()
    {
        var between = new Between();
        using var database = WithHook(between);
        var id = await EnglishAndFrenchAsync(database);
        between.Run = async services =>
        {
            await SharedChangeInEnglishAsync(services, database, id);
            await Should.ThrowAsync<InvalidOperationException>(() => services.GetRequiredService<ContentService>().InTransactionAsync<bool>(
                async () =>
                {
                    await PatchAsAsync(services, "fr", id, new() { ["title"] = "Salut" }, 2);
                    throw new InvalidOperationException("The hook's own write failed.");
                },
                Token));
        };

        await ShouldConflictWithTheSharedChangeAsync(database, id, () => database.PublishAsync(Editor, id, "fr"));
    }

    [Fact]
    public async Task TheConflictShowsTheEntryAsStoredAndNotWhatAHookOfThePublishWrote()
    {
        var between = new Between();
        using var database = WithHook(between);
        var id = await EnglishAndFrenchAsync(database);
        await database.PublishAsync(Editor, id, "fr");
        between.Run = async services =>
        {
            await SharedChangeInEnglishAsync(services, database, id);
            await Content(services).UnpublishAsync("article", id, new LocaleOptions { Locale = "fr" }, Token);
        };

        var conflict = await Should.ThrowAsync<VersionConflictException>(() => database.PublishAsync(Editor, id, "fr"));

        var current = conflict.Current.ShouldNotBeNull();
        (current["version"], current["status"]).ShouldBe((2, EntryStatus.Modified));
        database.Document(id, "fr").PublishedVersion.ShouldBe(1);
    }

    [Fact]
    public async Task AHookThatMovesTheVersionPastTheServiceIsRefusedAsTheHook()
    {
        var between = new Between();
        using var database = WithHook(between);
        var id = await EnglishAndFrenchAsync(database);
        between.Run = async services =>
        {
            var context = services.GetRequiredService<CmsDbContext>();
            await context.Rows("cms_article")
                .Where(row => EF.Property<Guid>(row, StorageNames.DocumentId) == id)
                .ExecuteUpdateAsync(set => set.SetProperty(row => EF.Property<long?>(row, "views"), 9L), Token);
            await context.Documents
                .Where(document => document.DocumentId == id && document.Locale == "fr")
                .ExecuteUpdateAsync(set => set.SetProperty(document => document.DraftVersion, 2), Token);
        };
        var before = database.Trace(id);

        (await Should.ThrowAsync<HookRejectedException>(() => database.PublishAsync(Editor, id, "fr"))).Code.ShouldBe(ContentErrorCodes.DraftMovedByHook);

        database.Trace(id).ShouldBe(before);
    }

    // A write nested in a validator can leave a hook running, and no pipeline of the publish looks again before the guard.
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task APublishRefusedInAMarkedScopeKeepsTheTimeoutsCode(bool inAHook, bool hookSavedTheDraft)
    {
        var between = new Between();
        using var database = inAHook ? WithHook(between) : WithoutHooks(between);
        var id = await EnglishAndFrenchAsync(database);
        between.Run = async services =>
        {
            if (hookSavedTheDraft)
            {
                await PatchAsAsync(services, "fr", id, new() { ["title"] = "Salut" }, 1);
            }
            else
            {
                await SharedChangeInEnglishAsync(services, database, id);
            }

            services.GetRequiredService<HookScopeGuard>().MarkLeftRunning("Left");
        };

        (await Should.ThrowAsync<HookRejectedException>(() => database.PublishAsync(Editor, id, "fr"))).Code.ShouldBe(ContentErrorCodes.HookTimedOut);

        database.Document(id, "fr").PublishedVersion.ShouldBeNull();
    }

    // A caller's transaction ends unseen, so what an earlier one moved is no mark of this publish.
    [Fact]
    public async Task ASaveEarlierInTheScopeDoesNotMakeTheConflictThePublishesOwn()
    {
        var between = new Between();
        using var database = WithoutHooks(between);
        var id = await EnglishAndFrenchAsync(database);

        var conflict = await Should.ThrowAsync<VersionConflictException>(() => database.AsAsync(Editor, async services =>
        {
            await using (var transaction = await services.GetRequiredService<CmsDbContext>().Database.BeginTransactionAsync(Token))
            {
                await PatchAsAsync(services, "fr", id, new() { ["title"] = "Salut" }, 1);
                await transaction.CommitAsync(Token);
            }

            between.Run = publish => SharedChangeInEnglishAsync(publish, database, id);
            return await Content(services).PublishAsync("article", id, new LocaleWriteOptions { Locale = "fr" }, Token);
        }));

        (conflict.ExpectedVersion, conflict.CurrentVersion).ShouldBe((2, 3));
    }

    [Fact]
    public async Task APublishInABulkFailsItsItemAsTheConflict()
    {
        var between = new Between();
        using var database = WithoutHooks(between);
        var id = await EnglishAndFrenchAsync(database);
        between.Run = services => SharedChangeInEnglishAsync(services, database, id);

        var result = await database.AsAsync(Editor, services => Content(services).BulkAsync("article", new BulkRequest { Operation = BulkOperation.Publish, Locale = "fr", Items = [new BulkItem(id)] }, Token));

        var problem = result.Items.ShouldHaveSingleItem().Problem.ShouldNotBeNull();
        problem.Code.ShouldBe(ApiErrorCodes.VersionConflict);
        problem.Current.ShouldNotBeNull()["version"].ShouldBe(2);
        database.Document(id, "fr").PublishedVersion.ShouldBeNull();
    }

    [Theory]
    [InlineData("fr", "title", false)]
    [InlineData("fr", "title", true)]
    [InlineData("en", "views", false)]
    [InlineData("en", "views", true)]
    public async Task AHookThatSavesTheDraftBeingPublishedIsRefusedAsTheHook(string locale, string field, bool inCallersTransaction)
    {
        var between = new Between();
        using var database = WithHook(between);
        var id = await EnglishAndFrenchAsync(database);
        ContentDocument patch = new() { [field] = field == "title" ? "Salut" : 7 };
        between.Run = services => PatchAsAsync(services, locale, id, patch, locale == "fr" ? 1 : 2);
        var before = database.Trace(id);

        var refused = await Should.ThrowAsync<HookRejectedException>(() => database.AsAsync(Editor, async services =>
        {
            await using var transaction = inCallersTransaction ? await services.GetRequiredService<CmsDbContext>().Database.BeginTransactionAsync(Token) : null;
            return await Content(services).PublishAsync("article", id, new LocaleWriteOptions { Locale = "fr" }, Token);
        }));

        refused.Code.ShouldBe(ContentErrorCodes.DraftMovedByHook);
        database.Trace(id).ShouldBe(before);
    }

    // The stored version has moved in both ways here, so it alone cannot name the writer.
    [Fact]
    public async Task AHookThatSavesOverAnotherRequestsChangeIsRefusedAsTheHook()
    {
        var between = new Between();
        using var database = WithHook(between);
        var id = await EnglishAndFrenchAsync(database);
        between.Run = async services =>
        {
            await SharedChangeInEnglishAsync(services, database, id);
            await PatchAsAsync(services, "fr", id, new() { ["title"] = "Salut" }, 2);
        };

        (await Should.ThrowAsync<HookRejectedException>(() => database.PublishAsync(Editor, id, "fr"))).Code.ShouldBe(ContentErrorCodes.DraftMovedByHook);

        database.Document(id, "fr").DraftVersion.ShouldBe(2);
    }

    [Fact]
    public async Task AHookThatWritesTheDraftPastTheServiceIsRefusedAsTheHook()
    {
        var between = new Between();
        using var database = WithHook(between);
        var id = await EnglishAndFrenchAsync(database);
        between.Run = services => services.GetRequiredService<CmsDbContext>().Rows("cms_article")
            .Where(row => EF.Property<Guid>(row, StorageNames.DocumentId) == id)
            .ExecuteUpdateAsync(set => set.SetProperty(row => EF.Property<long?>(row, "views"), 9L), Token);
        var before = database.Trace(id);

        (await Should.ThrowAsync<HookRejectedException>(() => database.PublishAsync(Editor, id, "fr"))).Code.ShouldBe(ContentErrorCodes.DraftMovedByHook);

        database.Trace(id).ShouldBe(before);
    }

    [Fact]
    public async Task AScheduledPublishWhoseHookSavesTheDraftFailsAsTheHook()
    {
        var between = new Between();
        using var fixture = new SchedulingFixture(more: cms =>
        {
            cms.Services.AddSingleton(between);
            cms.Services.AddScoped<IBeforePublishHook<ContentDocument>, BetweenHook>();
        });
        var user = fixture.Publisher("content.article.update");
        var id = await fixture.DraftAsync();
        await fixture.ScheduleAsync(user, id, SchedulingFixture.At(SchedulingFixture.Start.AddHours(1)));
        var row = fixture.Schedules(id).Single();
        between.Run = services => PatchAsAsync(services, "en", id, new() { ["title"] = "Two" }, 1);

        (await fixture.RunAsync(row.Id, row.JobId!.Value, user)).ShouldBe(ScheduleOutcome.Failed);

        var failed = fixture.Schedules(id).Single();
        (failed.Status, failed.Error).ShouldBe((ScheduleRow.Failed, $"HookRejectedException {ContentErrorCodes.DraftMovedByHook}"));
        fixture.Database.Document(id).DraftVersion.ShouldBe(1);
    }

    // #3546: a hook that saves the same values moves the version and leaves the draft as it was read, so the guard passes, and
    // the publish's swap then loses to this transaction: where the publish takes its row, or where its write over the draft,
    // for what the hook then changed in the entry, does. A 412 would name a version rolled back with the publish.
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AHookThatSavesTheSameValuesIsRefusedAsTheHookAtEitherSwap(bool hookChangesTheEntry, bool inCallersTransaction)
    {
        var between = new Between();
        using var database = WithHook(between);
        var id = await EnglishAndFrenchAsync(database);
        between.Run = services => PatchAsAsync(services, "fr", id, new() { ["title"] = "Bonjour" }, 1);
        between.Change = hookChangesTheEntry ? entry => entry["subtitle"] = "Changed" : null;
        var before = database.Trace(id);

        var refused = await Should.ThrowAsync<HookRejectedException>(() => database.AsAsync(Editor, async services =>
        {
            await using var transaction = inCallersTransaction ? await services.GetRequiredService<CmsDbContext>().Database.BeginTransactionAsync(Token) : null;
            return await Content(services).PublishAsync("article", id, new LocaleWriteOptions { Locale = "fr" }, Token);
        }));

        refused.Code.ShouldBe(ContentErrorCodes.DraftMovedByHook);
        between.Run.ShouldBeNull();
        database.Trace(id).ShouldBe(before);
    }

    // #3546: in a transaction the publish did not begin, it cannot roll that back to read what is stored, so the 412 reads as
    // that transaction sees it, and the transaction stays the caller's to go on with. SQLite has one writer, which the caller's
    // transaction holds from its BEGIN, so no other request can commit in it: there the change is made in that transaction by
    // SQL of its own, which the publish cannot tell from another's commit either, and rolls back with the publish's savepoint.
    [Fact]
    public async Task APublishRefusedForAnotherRequestsChangeInACallersTransactionLeavesItOpen()
    {
        var between = new Between();
        using var database = WithoutHooks(between);
        var id = await EnglishAndFrenchAsync(database);

        var author = await database.AsAsync(Editor, async services =>
        {
            var context = services.GetRequiredService<CmsDbContext>();
            await using var transaction = await context.Database.BeginTransactionAsync(Token);
            between.Run = publish => TestProvider.IsSqlite ? SharedChangeBySqlAsync(context, id) : database.PatchAsync(Editor, id, new() { ["views"] = 2 }, 2);

            var conflict = await Should.ThrowAsync<VersionConflictException>(() => Content(services).PublishAsync("article", id, new LocaleWriteOptions { Locale = "fr" }, Token));

            (conflict.ExpectedVersion, conflict.CurrentVersion).ShouldBe((1, 2));
            conflict.Current.ShouldNotBeNull()["views"].ShouldBe(2L);
            context.Database.CurrentTransaction.ShouldBeSameAs(transaction);
            transaction.GetDbTransaction().Connection.ShouldNotBeNull();
            var created = await Content(services).CreateAsync("author", new() { ["name"] = "Ada" }, Token);
            await transaction.CommitAsync(Token);
            return IdOf(created);
        });

        between.Run.ShouldBeNull();
        database.Read(context => context.Documents.Count(document => document.DocumentId == author)).ShouldBe(1);
        database.Row("cms_article", id)["views"].ShouldBe(TestProvider.IsSqlite ? 1L : 2L);
        database.Document(id, "fr").PublishedVersion.ShouldBeNull();
        database.PublishedRow("cms_article", id).ShouldBeNull();
    }

    // What the shared change in English leaves, views 2 and every locale's version moved, written in the transaction given.
    private static async Task SharedChangeBySqlAsync(CmsDbContext context, Guid id)
    {
        await context.Rows("cms_article")
            .Where(row => EF.Property<Guid>(row, StorageNames.DocumentId) == id && EF.Property<short>(row, StorageNames.State) == ContentState.Draft)
            .ExecuteUpdateAsync(set => set.SetProperty(row => EF.Property<long?>(row, "views"), 2L), Token);
        await context.Documents
            .Where(document => document.DocumentId == id)
            .ExecuteUpdateAsync(set => set.SetProperty(document => document.DraftVersion, document => document.DraftVersion + 1), Token);
    }

    // A trash that commits while a publish runs leaves no version moved and no row to hold: the entry is not found, as a
    // publish of an entry in the trash answers, where it was taken for a hook's write (#3524).
    [Fact]
    public async Task APublishThatATrashOvertookFindsNoEntry()
    {
        var between = new Between();
        using var database = WithoutHooks(between);
        var id = await EnglishAndFrenchAsync(database);
        between.Run = services => CommittedByAnotherRequestAsync(services, () => database.DeleteAsync(Editor, id));

        await Should.ThrowAsync<EntryNotFoundException>(() => database.PublishAsync(Editor, id, "fr"));

        between.Run.ShouldBeNull();
        database.Document(id, "fr").PublishedVersion.ShouldBeNull();
        database.PublishedRow("cms_article", id).ShouldBeNull();
    }
}
