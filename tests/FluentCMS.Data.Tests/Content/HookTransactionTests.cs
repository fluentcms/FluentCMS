using FluentCMS.Content;
using FluentCMS.Core.Content.Hooks;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Data.Tests.Storage;
using FluentCMS.Hooks;
using FluentCMS.Schema;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Content;

// Plays the part of the content service: begin a transaction, run the before hooks, write, run the after hooks, commit.
public class HookTransactionTests
{
    private const string Article = "cms_article";

    private static readonly SchemaModel Model = SampleSchema.FromClasses().Build();

    private static readonly OperationContext Operation = new(
        new Principal(PrincipalKind.AdminUser, Guid.Parse("00000000-0000-0000-0000-000000000001"), "Amir"),
        Origin.Admin,
        new SiteId(Guid.Parse("00000000-0000-0000-0000-0000000000a1")),
        "en",
        null,
        "corr-1",
        new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));

    private sealed class Calls
    {
        public List<string> Entries { get; } = [];
    }

    private sealed class RejectBeforeCreate : IBeforeCreateHook<ContentDocument>
    {
        public ValueTask BeforeCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            context.Cancel("ACME0100", "Not today.");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SetCategory : IBeforeCreateHook<ContentDocument>
    {
        public ValueTask BeforeCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            context.Entry["category"] = "video";
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailAfterCreate : IAfterCreateHook<ContentDocument>
    {
        public ValueTask AfterCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A plugin table is gone.");
    }

    private sealed class CancelAfterCreate : IAfterCreateHook<ContentDocument>
    {
        public ValueTask AfterCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            context.Cancel("ACME0101", "Rolled back.");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordWrites(Calls calls, StorageDatabase database) : IAfterCreateHook<ContentDocument>
    {
        public ValueTask AfterCreateAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
        {
            // A second context on the same connection joins the transaction, so it sees the row written before the hook.
            using var reader = database.CreateContext();
            calls.Entries.Add($"after:{reader.Rows(Article).Count()}");
            return ValueTask.CompletedTask;
        }
    }

    private static HookPipeline Pipeline(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);
        return new HookPipeline(services.BuildServiceProvider());
    }

    private static async Task CreateAsync(StorageDatabase database, HookPipeline pipeline, ContentDocument entry, Guid documentId)
    {
        var type = Model.FindContentType("article")!;
        using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        await pipeline.RunAsync(HookKind.BeforeCreate, type, Operation, entry, null, CancellationToken.None);

        context.Rows(Article).Add(new Dictionary<string, object>
        {
            ["document_id"] = documentId,
            ["site_id"] = Operation.Site.Value,
            ["state"] = ContentState.Draft,
            ["category"] = entry.Get<string>("category")!,
        });
        await context.SaveChangesAsync();

        await pipeline.RunAsync(HookKind.AfterCreate, type, Operation, entry, null, CancellationToken.None);

        await transaction.CommitAsync();
    }

    private static List<Dictionary<string, object>> Stored(StorageDatabase database)
    {
        using var reader = database.CreateContext();
        return [.. reader.Rows(Article)];
    }

    [Fact]
    public async Task ACancellingBeforeHookLeavesNoRowAndSurfacesItsCodeAndMessage()
    {
        using var database = new StorageDatabase(Model);
        database.CreateCreatedContext().Dispose();
        var pipeline = Pipeline(services => services.AddSingleton<IBeforeCreateHook<ContentDocument>, RejectBeforeCreate>());

        var exception = await Should.ThrowAsync<HookRejectedException>(
            CreateAsync(database, pipeline, new ContentDocument { ["category"] = "news" }, Guid.CreateVersion7()));

        exception.Code.ShouldBe("ACME0100");
        exception.Message.ShouldBe("Not today.");
        Stored(database).ShouldBeEmpty();
    }

    [Fact]
    public async Task AFailingAfterHookRollsTheWriteBack()
    {
        using var database = new StorageDatabase(Model);
        database.CreateCreatedContext().Dispose();
        var pipeline = Pipeline(services => services.AddSingleton<IAfterCreateHook<ContentDocument>, FailAfterCreate>());

        await Should.ThrowAsync<InvalidOperationException>(
            CreateAsync(database, pipeline, new ContentDocument { ["category"] = "news" }, Guid.CreateVersion7()));

        Stored(database).ShouldBeEmpty();
    }

    [Fact]
    public async Task ACancellingAfterHookRollsTheWriteBack()
    {
        using var database = new StorageDatabase(Model);
        database.CreateCreatedContext().Dispose();
        var pipeline = Pipeline(services => services.AddSingleton<IAfterCreateHook<ContentDocument>, CancelAfterCreate>());

        var exception = await Should.ThrowAsync<HookRejectedException>(
            CreateAsync(database, pipeline, new ContentDocument { ["category"] = "news" }, Guid.CreateVersion7()));

        exception.Code.ShouldBe("ACME0101");
        Stored(database).ShouldBeEmpty();
    }

    [Fact]
    public async Task WithoutRejectionTheRowIsCommittedWithWhatTheBeforeHookSet()
    {
        using var database = new StorageDatabase(Model);
        database.CreateCreatedContext().Dispose();
        var calls = new Calls();
        var pipeline = Pipeline(services =>
        {
            services.AddSingleton(calls);
            services.AddSingleton<IBeforeCreateHook<ContentDocument>, SetCategory>();
            services.AddSingleton<IAfterCreateHook<ContentDocument>>(new RecordWrites(calls, database));
        });
        var documentId = Guid.CreateVersion7();

        await CreateAsync(database, pipeline, new ContentDocument { ["category"] = "news" }, documentId);

        var row = Stored(database).ShouldHaveSingleItem();
        row["document_id"].ShouldBe(documentId);
        row["category"].ShouldBe("video");
        calls.Entries.ShouldBe(["after:1"]);
    }
}
