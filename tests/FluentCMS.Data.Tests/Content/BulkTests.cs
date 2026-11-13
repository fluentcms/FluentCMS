using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Data.Tests.Activity;
using FluentCMS.Hooks;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

public sealed class BulkTests : IDisposable
{
    private static readonly ContentFilter Every = new FieldFilter("version", FilterOperator.GreaterThan, 0);

    private readonly ServiceDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private static ContentDocument Titled(string title) => new() { ["title"] = title };

    private static Task<BulkResult> BulkAsync(ServiceDatabase database, Principal principal, BulkRequest request, string type = "article", CancellationToken? cancellationToken = null) =>
        database.AsAsync(principal, services => services.GetRequiredService<IContentService>().BulkAsync(type, request, cancellationToken ?? Token));

    private async Task<Guid> ArticleAsync(string title, Principal? by = null) => IdOf(await _database.CreateAsync(by ?? Editor, Titled(title)));

    private static BulkRequest Patch(params Guid[] ids) =>
        new() { Operation = BulkOperation.Update, Items = [.. ids.Select(id => new BulkItem(id) { Patch = new() { ["subtitle"] = "Bulk" }, ExpectedVersion = 1 })] };

    private Guid OwnEditor() => _database.Database.AddRoleForTest(
        "OwnEditor",
        ("content.article.create", null),
        ("content.article.read", null),
        ("content.article.update", new PermissionConstraints { RowRule = BuiltInRoles.OwnEntriesRule }),
        ("content.article.delete", new PermissionConstraints { RowRule = BuiltInRoles.OwnEntriesRule }));

    private sealed class RefuseTitle(string title) : IBeforeUpdateHook<ContentDocument>
    {
        public ValueTask BeforeUpdateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            if (Equals(context.Entry["title"], title))
            {
                context.Cancel("ACME0001", "No.");
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class CancelOnTitle(string title, CancellationTokenSource source) : IBeforeUpdateHook<ContentDocument>
    {
        public async ValueTask BeforeUpdateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            if (Equals(context.Entry["title"], title))
            {
                await source.CancelAsync();
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    private sealed class CancelAfterCommit(CancellationTokenSource source) : IEntryChangeSink
    {
        public bool Armed { get; set; }

        public async ValueTask PublishAsync(EntryChange change, CancellationToken cancellationToken)
        {
            if (Armed)
            {
                await source.CancelAsync();
            }
        }
    }

    private sealed class ThrowOnTitle(string title) : IBeforeUpdateHook<ContentDocument>
    {
        public ValueTask BeforeUpdateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) =>
            Equals(context.Entry["title"], title) ? throw new InvalidOperationException($"secret {title}") : ValueTask.CompletedTask;
    }

    // Changes another entry once the first publish has committed, between the selection and that entry's turn.
    private sealed class ChangeAfterPublish : IEntryChangeSink
    {
        public Func<Task>? Change { get; set; }

        public async ValueTask PublishAsync(EntryChange change, CancellationToken cancellationToken)
        {
            if (change.Kind == EntryChangeKind.Published && Change is { } run)
            {
                Change = null;
                await run();
            }
        }
    }

    // The single check refuses every delete, as it would for a row the selection query let through by mistake.
    private sealed class RefuseDeletes(AuthorizationEvaluator inner) : IAuthorizationEvaluator
    {
        public ValueTask<EffectivePermissions> ResolveAsync(OperationContext context, CancellationToken cancellationToken = default) => inner.ResolveAsync(context, cancellationToken);

        public ValueTask<bool> CanAccessEntryAsync(OperationContext context, SchemaType type, ContentOperation operation, Guid entry, AccessTarget target, QueryStatus status = QueryStatus.Published, CancellationToken cancellationToken = default) =>
            operation == ContentOperation.Delete ? ValueTask.FromResult(false) : inner.CanAccessEntryAsync(context, type, operation, entry, target, status, cancellationToken);

        public ValueTask<WritableFields> WritableFieldsAsync(OperationContext context, SchemaType type, ContentOperation operation, Guid? entry, AccessTarget target, CancellationToken cancellationToken = default) =>
            inner.WritableFieldsAsync(context, type, operation, entry, target, cancellationToken);
    }

    // Takes the article tables away once a write has committed, so that reading it back fails.
    private sealed class Armed
    {
        public bool On { get; set; }
    }

    private sealed class BreakReadsAfterCommit(CmsDbContext context, Armed armed) : IEntryChangeSink
    {
        public async ValueTask PublishAsync(EntryChange change, CancellationToken cancellationToken)
        {
            if (armed.On)
            {
                await context.Database.ExecuteSqlRawAsync("ALTER TABLE cms_article_i18n RENAME TO cms_article_i18n_gone", cancellationToken);
            }
        }
    }

    [Fact]
    public async Task AForbiddenItemFailsAndTheOthersComplete()
    {
        var ann = WithRole(OwnEditor(), Guid.CreateVersion7());
        var first = await ArticleAsync("First", ann);
        var theirs = await ArticleAsync("Theirs");
        var last = await ArticleAsync("Last", ann);

        var result = await BulkAsync(_database, ann, Patch(first, theirs, last));

        result.Cancelled.ShouldBeFalse();
        result.Items.Select(item => (item.Index, item.Id, item.Status)).ShouldBe([(0, first, BulkItemStatus.Succeeded), (1, theirs, BulkItemStatus.Failed), (2, last, BulkItemStatus.Succeeded)]);
        result.Items[0].Version.ShouldBe(2);
        result.Items[1].Problem!.Code.ShouldBe(ApiErrorCodes.NotFound);
        (await _database.GetAsync(Editor, first))!["subtitle"].ShouldBe("Bulk");
        (await _database.GetAsync(Editor, theirs))!["version"].ShouldBe(1);
        (await _database.GetAsync(Editor, last))!["subtitle"].ShouldBe("Bulk");
    }

    [Fact]
    public async Task AnItemDeniedOnItsTypeIsReportedAsDenied()
    {
        var english = _database.Database.AddRoleForTest("EnglishDeleter", ("content.article.read", null), ("content.article.delete", new PermissionConstraints { Locales = ["en"] }));
        var single = await ArticleAsync("Single");
        var translated = await ArticleAsync("Translated");
        await _database.UpdateAsync(Editor, translated, Titled("Traduit"), 0, locale: "fr");

        var result = await BulkAsync(_database, WithRole(english), new BulkRequest { Operation = BulkOperation.Delete, Items = [new BulkItem(translated), new BulkItem(single)] });

        result.Items[0].Problem!.Code.ShouldBe(ApiErrorCodes.Forbidden);
        result.Items[1].Status.ShouldBe(BulkItemStatus.Succeeded);
        (await _database.GetAsync(Editor, translated)).ShouldNotBeNull();
        (await _database.GetAsync(Editor, single)).ShouldBeNull();
    }

    [Fact]
    public async Task AnEntryThatCannotBeReadFailsAsOneThatDoesNotExist()
    {
        var author = AuthorRole(Guid.CreateVersion7());
        var theirs = await ArticleAsync("Theirs");
        var missing = Guid.CreateVersion7();

        foreach (var request in new[]
        {
            Patch(theirs, missing),
            new BulkRequest { Operation = BulkOperation.Delete, Items = [new BulkItem(theirs), new BulkItem(missing)] },
        })
        {
            var result = await BulkAsync(_database, author, request);

            var unreadable = result.Items[0].Problem.ShouldNotBeNull();
            var absent = result.Items[1].Problem.ShouldNotBeNull();
            (unreadable.Code, absent.Code).ShouldBe((ApiErrorCodes.NotFound, ApiErrorCodes.NotFound));
            unreadable.Message.Replace(theirs.ToString(), "id", StringComparison.Ordinal).ShouldBe(absent.Message.Replace(missing.ToString(), "id", StringComparison.Ordinal));
            result.Items.Select(item => (item.Status, item.Version, item.Warnings.Count)).Distinct().ShouldHaveSingleItem();
        }

        (await _database.GetAsync(Editor, theirs)).ShouldNotBeNull();
    }

    [Fact]
    public async Task ABulkByFilterWithoutACapIsRejected()
    {
        await ArticleAsync("One");
        var before = _database.Footprint();

        var refused = await Should.ThrowAsync<ValidationException>(() => BulkAsync(_database, Editor, new BulkRequest { Operation = BulkOperation.Delete, Filter = Every }));

        refused.Errors.ShouldHaveSingleItem().Path.ShouldBe("cap");
        _database.Footprint().ShouldBe(before);
    }

    [Fact]
    public async Task AFilterOverTheCapIsRefusedBeforeAnythingIsWritten()
    {
        var ids = new[] { await ArticleAsync("One"), await ArticleAsync("Two"), await ArticleAsync("Three") };
        var before = _database.Footprint();

        var refused = await Should.ThrowAsync<ValidationException>(() => BulkAsync(_database, Editor, new BulkRequest { Operation = BulkOperation.Update, Filter = Every, Cap = 2, Patch = new() { ["subtitle"] = "Bulk" } }));

        refused.Errors.ShouldHaveSingleItem().Code.ShouldBe(QueryErrorCodes.LimitExceeded);
        _database.Footprint().ShouldBe(before);

        var result = await BulkAsync(_database, Editor, new BulkRequest { Operation = BulkOperation.Update, Filter = Every, Cap = 3, Patch = new() { ["subtitle"] = "Bulk" } });

        result.Items.Select(item => item.Id!.Value).ShouldBe(ids, ignoreOrder: true);
        result.Items.ShouldAllBe(item => item.Status == BulkItemStatus.Succeeded && item.Version == 2);
    }

    [Fact]
    public async Task AFilterSelectsOnlyEntriesTheCallerMayReadAndAlsoWrite()
    {
        var ann = WithRole(OwnEditor(), Guid.CreateVersion7());
        var mine = new[] { await ArticleAsync("Mine", ann), await ArticleAsync("Mine too", ann) };
        await ArticleAsync("Readable");
        await ArticleAsync("Readable too");
        var author = AuthorRole(Guid.CreateVersion7());
        var authored = await ArticleAsync("Authored", author);

        // Ann may read five and write two: a cap of two holds, since only what she may write is selected.
        var result = await BulkAsync(_database, ann, new BulkRequest { Operation = BulkOperation.Delete, Filter = Every, Cap = 2 });

        result.Items.Select(item => item.Id!.Value).ShouldBe(mine, ignoreOrder: true);
        result.Items.ShouldAllBe(item => item.Status == BulkItemStatus.Succeeded && item.Version == null);
        (await _database.TrashAsync(Editor)).Items.Select(IdOf).ShouldBe(mine, ignoreOrder: true);

        // The Author role reads only its own entries, so the others are not there to select, let alone write.
        (await BulkAsync(_database, author, new BulkRequest { Operation = BulkOperation.Delete, Filter = Every, Cap = 1 })).Items.ShouldHaveSingleItem().Id.ShouldBe(authored);
    }

    [Fact]
    public async Task AFilterOnARelationIdMatchesOnlyTargetsTheCallerMayRead()
    {
        var hidden = IdOf(await _database.CreateAsync(Editor, new() { ["name"] = "Hidden" }, "author"));
        await _database.CreateAsync(Editor, new() { ["title"] = "Refers", ["author"] = hidden });
        var role = _database.Database.AddRoleForTest(
            "NoAuthors",
            ("content.article.read", null),
            ("content.article.update", null),
            ("content.author.read", new PermissionConstraints { RowRule = BuiltInRoles.OwnEntriesRule }));
        var byAuthor = new BulkRequest { Operation = BulkOperation.Update, Filter = new FieldFilter("author", FilterOperator.Equal, hidden), Cap = 10, Patch = new() { ["subtitle"] = "Bulk" } };

        (await BulkAsync(_database, WithRole(role), byAuthor)).Items.ShouldBeEmpty();
        (await BulkAsync(_database, Editor, byAuthor)).Items.ShouldHaveSingleItem().Status.ShouldBe(BulkItemStatus.Succeeded);
    }

    [Fact]
    public async Task EachEntryGetsItsOwnVersionAndActivityRow()
    {
        using var database = new ServiceDatabase(ActivityFixture.On());

        var created = await BulkAsync(database, Editor, new BulkRequest { Operation = BulkOperation.Create, Entries = [Titled("One"), Titled("Two"), Titled("Three")] });
        var ids = created.Items.Select(item => item.Id!.Value).ToList();
        var published = await BulkAsync(database, Editor, new BulkRequest { Operation = BulkOperation.Publish, Items = [.. ids.Select(id => new BulkItem(id))] });
        await database.DrainAsync();

        created.Items.Select(item => (item.Index, item.Status, item.Version)).ShouldBe([(0, BulkItemStatus.Succeeded, 1), (1, BulkItemStatus.Succeeded, 1), (2, BulkItemStatus.Succeeded, 1)]);
        published.Items.ShouldAllBe(item => item.Status == BulkItemStatus.Succeeded && item.Version == 1);
        ids.Distinct().Count().ShouldBe(3);
        foreach (var id in ids)
        {
            database.Versions(id).ShouldHaveSingleItem();
            database.Document(id).Status.ShouldBe(EntryStatus.Published);
            database.ActivityRows().Where(row => row.TargetId == id.ToString("D")).Select(row => row.Action).ShouldBe(["content.draft_saved", "content.published"], ignoreOrder: true);
        }
    }

    [Fact]
    public async Task ADuplicateIdRunsOnceWhereItFirstAppears()
    {
        var one = await ArticleAsync("One");
        var two = await ArticleAsync("Two");
        var request = new BulkRequest
        {
            Operation = BulkOperation.Update,
            Items =
            [
                new BulkItem(one) { Patch = new() { ["subtitle"] = "First" }, ExpectedVersion = 1 },
                new BulkItem(two) { Patch = new() { ["subtitle"] = "Two" }, ExpectedVersion = 1 },
                new BulkItem(one) { Patch = new() { ["subtitle"] = "Again" }, ExpectedVersion = 2 },
            ],
        };

        var result = await BulkAsync(_database, Editor, request);

        result.Items.Select(item => (item.Id, item.Status)).ShouldBe([(one, BulkItemStatus.Succeeded), (two, BulkItemStatus.Succeeded), (one, BulkItemStatus.Duplicate)]);
        var stored = (await _database.GetAsync(Editor, one))!;
        stored["subtitle"].ShouldBe("First");
        stored["version"].ShouldBe(2);
    }

    [Fact]
    public async Task AFailedItemRollsBackOnlyItself()
    {
        using var database = new ServiceDatabase(cms => cms.Services.AddSingleton<IBeforeUpdateHook<ContentDocument>>(new RefuseTitle("Refused")));
        var ids = new List<Guid>();
        foreach (var title in new[] { "One", "Two", "Three", "Four" })
        {
            ids.Add(IdOf(await database.CreateAsync(Editor, Titled(title))));
        }

        var before = ids.Select(database.Trace).ToList();
        var request = new BulkRequest
        {
            Operation = BulkOperation.Update,
            Items =
            [
                new BulkItem(ids[0]) { Patch = Titled("Changed"), ExpectedVersion = 1 },
                new BulkItem(ids[1]) { Patch = Titled("Refused"), ExpectedVersion = 1 },
                new BulkItem(ids[2]) { Patch = new() { ["views"] = 5000 }, ExpectedVersion = 1 },
                new BulkItem(ids[3]) { Patch = Titled("Stale"), ExpectedVersion = 7 },
            ],
        };

        var result = await BulkAsync(database, Editor, request);

        result.Items.Select(item => item.Status).ShouldBe([BulkItemStatus.Succeeded, BulkItemStatus.Failed, BulkItemStatus.Failed, BulkItemStatus.Failed]);
        result.Items[1].Problem!.Code.ShouldBe("ACME0001");
        var invalid = result.Items[2].Problem.ShouldNotBeNull();
        invalid.Errors.ShouldHaveSingleItem().Path.ShouldBe("views");
        invalid.Code.ShouldBe(invalid.Errors[0].Code);
        var stale = result.Items[3].Problem.ShouldNotBeNull();
        stale.Code.ShouldBe(ApiErrorCodes.VersionConflict);
        stale.Current.ShouldNotBeNull()["version"].ShouldBe(1);
        (await database.GetAsync(Editor, ids[0]))!["title"].ShouldBe("Changed");
        ids.Skip(1).Select(database.Trace).ShouldBe(before.Skip(1));
    }

    [Fact]
    public async Task ThePublishPolicyHoldsForEachItem()
    {
        var model = SampleSchema.NewBuilder()
            .AddCollection("author", type => type.Field("name", "text"))
            .AddCollection("blocked", type => type.PublishPolicy(UnpublishedReferencePolicy.Block, PublishPolicyAttribute.DefaultMaxVersions, PublishPolicyAttribute.DefaultTrashRetentionDays).Field("title", "text").Field("author", "relation", field => field.Relation("author")))
            .AddCollection("warned", type => type.PublishPolicy(UnpublishedReferencePolicy.Warn, PublishPolicyAttribute.DefaultMaxVersions, PublishPolicyAttribute.DefaultTrashRetentionDays).Field("title", "text").Field("author", "relation", field => field.Relation("author")))
            .Build();
        using var database = new ServiceDatabase(schema: model);
        var draft = IdOf(await database.CreateAsync(Editor, new() { ["name"] = "Draft" }, "author"));
        var live = IdOf(await database.CreateAsync(Editor, new() { ["name"] = "Live" }, "author"));
        await database.PublishAsync(Editor, live, type: "author");

        async Task<BulkResult> PublishBothAsync(string type)
        {
            var toDraft = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "A", ["author"] = draft }, type));
            var toLive = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "B", ["author"] = live }, type));
            return await BulkAsync(database, Editor, new BulkRequest { Operation = BulkOperation.Publish, Items = [new BulkItem(toDraft), new BulkItem(toLive)] }, type);
        }

        var blocked = await PublishBothAsync("blocked");
        blocked.Items[0].Problem!.Errors.ShouldNotBeEmpty();
        blocked.Items[1].Status.ShouldBe(BulkItemStatus.Succeeded);
        database.Document(blocked.Items[0].Id!.Value, "").Status.ShouldBe(EntryStatus.Draft);

        var warned = await PublishBothAsync("warned");
        warned.Items.ShouldAllBe(item => item.Status == BulkItemStatus.Succeeded);
        warned.Items[0].Warnings.ShouldHaveSingleItem().Code.ShouldBe(PublishWarningCodes.UnpublishedReference);
        warned.Items[1].Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task UnpublishWithdrawsEachEntry()
    {
        var one = await ArticleAsync("One");
        var two = await ArticleAsync("Two");
        await _database.PublishAsync(Editor, one);

        var result = await BulkAsync(_database, Editor, new BulkRequest { Operation = BulkOperation.Unpublish, Items = [new BulkItem(one), new BulkItem(two)] });

        result.Items[0].Status.ShouldBe(BulkItemStatus.Succeeded);
        result.Items[1].Problem!.Errors.ShouldNotBeEmpty();
        _database.Document(one).Status.ShouldBe(EntryStatus.Unpublished);
    }

    [Fact]
    public async Task CancellationStopsBetweenItemsAndReportsWhatRan()
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var sink = new CancelAfterCommit(source);
        using var database = new ServiceDatabase(cms => cms.Services.AddSingleton<IEntryChangeSink>(sink));
        var one = IdOf(await database.CreateAsync(Editor, Titled("One")));
        var two = IdOf(await database.CreateAsync(Editor, Titled("Two")));
        sink.Armed = true;

        var result = await BulkAsync(database, Editor, Patch(one, two, one), cancellationToken: source.Token);

        result.Cancelled.ShouldBeTrue();
        result.Items.Select(item => item.Status).ShouldBe([BulkItemStatus.Succeeded, BulkItemStatus.NotRun, BulkItemStatus.NotRun]);
        (await database.GetAsync(Editor, one))!["version"].ShouldBe(2);
        (await database.GetAsync(Editor, two))!["version"].ShouldBe(1);
    }

    [Fact]
    public async Task AnItemCancelledWhileItRunsLeavesNothingBehind()
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(Token);
        using var database = new ServiceDatabase(cms => cms.Services.AddSingleton<IBeforeUpdateHook<ContentDocument>>(new CancelOnTitle("Stop", source)));
        var one = IdOf(await database.CreateAsync(Editor, Titled("One")));
        var two = IdOf(await database.CreateAsync(Editor, Titled("Two")));
        var three = IdOf(await database.CreateAsync(Editor, Titled("Three")));
        var before = database.Trace(two);
        var request = new BulkRequest
        {
            Operation = BulkOperation.Update,
            Items = [new BulkItem(one) { Patch = Titled("Go"), ExpectedVersion = 1 }, new BulkItem(two) { Patch = Titled("Stop"), ExpectedVersion = 1 }, new BulkItem(three) { Patch = Titled("Go"), ExpectedVersion = 1 }],
        };

        var result = await BulkAsync(database, Editor, request, cancellationToken: source.Token);

        result.Cancelled.ShouldBeTrue();
        result.Items.Select(item => item.Status).ShouldBe([BulkItemStatus.Succeeded, BulkItemStatus.NotRun, BulkItemStatus.NotRun]);
        database.Trace(two).ShouldBe(before);
        (await database.GetAsync(Editor, three))!["title"].ShouldBe("Three");
    }

    private static readonly (string Path, BulkRequest Request)[] Invalid =
    [
        ("entries", new BulkRequest { Operation = BulkOperation.Create }),
        ("", new BulkRequest { Operation = BulkOperation.Create, Entries = [Titled("A")], Filter = Every, Cap = 1 }),
        ("", new BulkRequest { Operation = BulkOperation.Delete }),
        ("", new BulkRequest { Operation = BulkOperation.Delete, Items = [new BulkItem(Guid.Empty)], Filter = Every, Cap = 1 }),
        ("entries", new BulkRequest { Operation = BulkOperation.Delete, Entries = [Titled("A")], Items = [new BulkItem(Guid.Empty)] }),
        ("items", new BulkRequest { Operation = BulkOperation.Delete, Items = [] }),
        ("items", new BulkRequest { Operation = BulkOperation.Delete, Items = [.. Enumerable.Range(0, BulkRequest.MaxItems + 1).Select(_ => new BulkItem(Guid.Empty))] }),
        ("items[0].patch", new BulkRequest { Operation = BulkOperation.Update, Items = [new BulkItem(Guid.Empty) { ExpectedVersion = 1 }] }),
        ("items[0].expectedVersion", new BulkRequest { Operation = BulkOperation.Update, Items = [new BulkItem(Guid.Empty) { Patch = Titled("A") }] }),
        ("items[0].patch", new BulkRequest { Operation = BulkOperation.Delete, Items = [new BulkItem(Guid.Empty) { Patch = Titled("A") }] }),
        ("items[0].expectedVersion", new BulkRequest { Operation = BulkOperation.Unpublish, Items = [new BulkItem(Guid.Empty) { ExpectedVersion = 1 }] }),
        ("patch", new BulkRequest { Operation = BulkOperation.Update, Items = [new BulkItem(Guid.Empty) { Patch = Titled("A"), ExpectedVersion = 1 }], Patch = Titled("B") }),
        ("patch", new BulkRequest { Operation = BulkOperation.Update, Filter = Every, Cap = 1 }),
        ("patch", new BulkRequest { Operation = BulkOperation.Delete, Filter = Every, Cap = 1, Patch = Titled("B") }),
        ("cap", new BulkRequest { Operation = BulkOperation.Delete, Filter = Every, Cap = 0 }),
        ("cap", new BulkRequest { Operation = BulkOperation.Delete, Filter = Every, Cap = BulkRequest.MaxItems + 1 }),
        ("cap", new BulkRequest { Operation = BulkOperation.Delete, Items = [new BulkItem(Guid.Empty)], Cap = 1 }),
        ("locale", new BulkRequest { Operation = BulkOperation.Delete, Items = [new BulkItem(Guid.Empty)], Locale = "fr" }),
        ("operation", new BulkRequest { Operation = (BulkOperation)99, Items = [new BulkItem(Guid.Empty)] }),
    ];

    public static TheoryData<int> InvalidRequests => [.. Enumerable.Range(0, Invalid.Length)];

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task ARequestThatMeansSomethingElseIsRefusedWhole(int index)
    {
        var (path, request) = Invalid[index];
        await ArticleAsync("One");
        var before = _database.Footprint();

        var refused = await Should.ThrowAsync<ValidationException>(() => BulkAsync(_database, Editor, request));

        refused.Errors.Select(error => error.Path).ShouldContain(path);
        _database.Footprint().ShouldBe(before);
    }

    [Fact]
    public async Task APrincipalWithoutTheOperationOnTheTypeIsRefusedWhole()
    {
        var readOnly = _database.Database.AddRoleForTest("ReadOnly", ("content.article.read", null));
        var id = await ArticleAsync("One");

        await Should.ThrowAsync<ContentAccessDeniedException>(() => BulkAsync(_database, WithRole(readOnly), new BulkRequest { Operation = BulkOperation.Delete, Items = [new BulkItem(id)] }));
        await Should.ThrowAsync<ContentAccessDeniedException>(() => BulkAsync(_database, WithRole(readOnly), new BulkRequest { Operation = BulkOperation.Delete, Filter = Every, Cap = 1 }));
    }

    [Fact]
    public async Task AnUnexpectedFailureIsReportedWithoutItsCauseAndTheOthersRun()
    {
        using var database = new ServiceDatabase(cms => cms.Services.AddSingleton<IBeforeUpdateHook<ContentDocument>>(new ThrowOnTitle("Boom")));
        var one = IdOf(await database.CreateAsync(Editor, Titled("One")));
        var two = IdOf(await database.CreateAsync(Editor, Titled("Two")));
        var three = IdOf(await database.CreateAsync(Editor, Titled("Three")));
        var before = database.Trace(two);
        var request = new BulkRequest
        {
            Operation = BulkOperation.Update,
            Items = [new BulkItem(one) { Patch = Titled("Go"), ExpectedVersion = 1 }, new BulkItem(two) { Patch = Titled("Boom"), ExpectedVersion = 1 }, new BulkItem(three) { Patch = Titled("Go"), ExpectedVersion = 1 }],
        };

        var result = await BulkAsync(database, Editor, request);

        result.Items.Select(item => item.Status).ShouldBe([BulkItemStatus.Succeeded, BulkItemStatus.Failed, BulkItemStatus.Succeeded]);
        var problem = result.Items[1].Problem.ShouldNotBeNull();
        (problem.Code, problem.Message, problem.Errors.Count, problem.Current).ShouldBe((ApiErrorCodes.InternalError, ContentService.UnexpectedFailure, 0, null));
        database.Trace(two).ShouldBe(before);
        (await database.GetAsync(Editor, three))!["title"].ShouldBe("Go");
    }

    [Fact]
    public async Task APublishByFilterPublishesTheVersionTheFilterRead()
    {
        var sink = new ChangeAfterPublish();
        using var database = new ServiceDatabase(cms => cms.Services.AddSingleton<IEntryChangeSink>(sink));
        var ids = new[] { IdOf(await database.CreateAsync(Editor, Titled("One"))), IdOf(await database.CreateAsync(Editor, Titled("Two"))) };
        sink.Change = async () =>
        {
            foreach (var id in ids)
            {
                if (database.Document(id).Status == EntryStatus.Draft)
                {
                    await database.PatchAsync(Editor, id, Titled("Changed since"), 1);
                }
            }
        };

        var result = await BulkAsync(database, Editor, new BulkRequest { Operation = BulkOperation.Publish, Filter = Every, Cap = 2 });

        result.Items[0].Status.ShouldBe(BulkItemStatus.Succeeded);
        result.Items[1].Problem!.Current.ShouldNotBeNull()["version"].ShouldBe(2);
        database.Document(result.Items[1].Id!.Value).Status.ShouldBe(EntryStatus.Draft);
    }

    [Fact]
    public async Task TheSelectionQueryLeavesOutWhatTheCallerCannotChange()
    {
        var ann = WithRole(OwnEditor(), Guid.CreateVersion7());
        for (var index = 0; index < 12; index++)
        {
            await ArticleAsync($"Theirs {index}");
        }

        var mine = await ArticleAsync("Mine", ann);

        // Twelve rows Ann may read but not delete, against a cap of one: walked one by one they would pass the bound.
        var result = await BulkAsync(_database, ann, new BulkRequest { Operation = BulkOperation.Delete, Filter = Every, Cap = 1 });

        result.Items.ShouldHaveSingleItem().Id.ShouldBe(mine);
    }

    [Fact]
    public async Task AFilterThatReachesTooManyRowsTheCallerCannotChangeIsRefused()
    {
        using var database = new ServiceDatabase(cms => cms.Services
            .AddScoped<AuthorizationEvaluator>()
            .AddScoped<IAuthorizationEvaluator>(services => new RefuseDeletes(services.GetRequiredService<AuthorizationEvaluator>())));
        for (var index = 0; index < 12; index++)
        {
            await database.CreateAsync(Editor, Titled($"Entry {index}"));
        }

        var before = database.Footprint();

        var refused = await Should.ThrowAsync<ValidationException>(() => BulkAsync(database, Editor, new BulkRequest { Operation = BulkOperation.Delete, Filter = Every, Cap = 1 }));

        var error = refused.Errors.ShouldHaveSingleItem();
        (error.Path, error.Code).ShouldBe(("filter", QueryErrorCodes.LimitExceeded));
        database.Footprint().ShouldBe(before);
        (await BulkAsync(database, Editor, new BulkRequest { Operation = BulkOperation.Delete, Filter = Every, Cap = 2 })).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheRuleOfTheOperationHoldsForTheSelectedEntriesNotForTheirRelationTargets()
    {
        var model = SampleSchema.NewBuilder()
            .AddCollection("page", type => type.Field("title", "text").Field("parent", "relation", field => field.Relation("page")))
            .Build();
        using var database = new ServiceDatabase(schema: model);
        var role = database.Database.AddRoleForTest(
            "OwnPages",
            ("content.page.create", null),
            ("content.page.read", null),
            ("content.page.update", new PermissionConstraints { RowRule = BuiltInRoles.OwnEntriesRule }));
        var ann = WithRole(role, Guid.CreateVersion7());
        var theirs = IdOf(await database.CreateAsync(Editor, Titled("Theirs"), "page"));
        var child = IdOf(await database.CreateAsync(ann, new() { ["title"] = "Child", ["parent"] = theirs }, "page"));
        await database.CreateAsync(Editor, new() { ["title"] = "Their child", ["parent"] = theirs }, "page");
        var byParent = new BulkRequest { Operation = BulkOperation.Update, Filter = new FieldFilter("parent", FilterOperator.Equal, theirs), Cap = 5, Patch = Titled("Changed") };

        var result = await BulkAsync(database, ann, byParent, "page");

        result.Items.ShouldHaveSingleItem().Id.ShouldBe(child);
        result.Items[0].Status.ShouldBe(BulkItemStatus.Succeeded);
    }

    [Fact]
    public async Task AnItemThatCommittedIsDoneEvenWhenReadingItBackFails()
    {
        var armed = new Armed();
        using var database = new ServiceDatabase(cms => cms.Services.AddSingleton(armed).AddScoped<IEntryChangeSink, BreakReadsAfterCommit>());
        var id = IdOf(await database.CreateAsync(Editor, Titled("One")));
        armed.On = true;

        var result = await BulkAsync(database, Editor, Patch(id));

        var item = result.Items.ShouldHaveSingleItem();
        (item.Id, item.Status, item.Version, item.Problem).ShouldBe((id, BulkItemStatus.Succeeded, 2, null));
        database.Document(id).DraftVersion.ShouldBe(2);
    }

    [Fact]
    public async Task ABulkCannotRunInsideAnotherTransaction()
    {
        var id = await ArticleAsync("One");

        await Should.ThrowAsync<InvalidOperationException>(() => _database.AsAsync(Editor, async services =>
        {
            await using var transaction = await services.GetRequiredService<CmsDbContext>().Database.BeginTransactionAsync(Token);
            return await services.GetRequiredService<IContentService>().BulkAsync("article", new BulkRequest { Operation = BulkOperation.Delete, Items = [new BulkItem(id)] }, Token);
        }));

        (await _database.GetAsync(Editor, id)).ShouldNotBeNull();
    }

    [Fact]
    public async Task TheTypedServiceRunsTheSameBulk()
    {
        using var database = new ServiceDatabase(schema: SampleSchema.FromClasses().Build());

        var result = await database.AsAsync(Editor, services => services.GetRequiredService<IContentService<FluentCMS.Core.Tests.Schema.Samples.Author>>().BulkAsync(
            new BulkRequest { Operation = BulkOperation.Create, Entries = [new() { ["name"] = "Ann" }, new() { ["name"] = "Bob" }] },
            Token));

        result.Items.ShouldAllBe(item => item.Status == BulkItemStatus.Succeeded && item.Version == 1);
        (await database.QueryAsync(Editor, type: "author")).Items.Count.ShouldBe(2);
    }
}
