using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Data.Tests.Authorization;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

public sealed class ContentServiceTransactionTests
{
    private static ContentDocument Titled(string title) => new() { ["title"] = title };

    private sealed class Recorder : IEntryChangeSink
    {
        public List<EntryChange> Changes { get; } = [];

        public ValueTask PublishAsync(EntryChange change, CancellationToken cancellationToken)
        {
            Changes.Add(change);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Throwing : IEntryChangeSink
    {
        public ValueTask PublishAsync(EntryChange change, CancellationToken cancellationToken) => throw new InvalidOperationException("The sink is down.");
    }

    private sealed class Cancelling(CancellationTokenSource source) : IEntryChangeSink
    {
        public async ValueTask PublishAsync(EntryChange change, CancellationToken cancellationToken)
        {
            await source.CancelAsync();
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    [FluentCMS.Schema.Collection("article")]
    private sealed class Regional : ContentItem
    {
        public string? Region { get; set; }
    }

    [Fact]
    public async Task ARefusedWriteCaughtInsideAnOuterTransactionLeavesNothingToCommit()
    {
        using var database = new ServiceDatabase(cms => cms.Authorization.AddRowRule<Regional>("region", ctx => article => article.Region == ctx.Principal.Claim("region")));
        var entry = await database.CreateAsync(Editor, new() { ["title"] = "T", ["region"] = "eu" });
        var id = IdOf(entry);
        var role = database.Database.AddRoleForTest("Regional", ("content.article.read", new PermissionConstraints { RowRule = "region" }), ("content.article.update", new PermissionConstraints { RowRule = "region" }));
        var regional = AuthorizationDatabase.User([role], [TokenScopes.Management], claims: new() { ["region"] = "eu" });

        await database.AsAsync(regional, async services =>
        {
            var service = services.GetRequiredService<IContentService>();
            await using var transaction = await services.GetRequiredService<CmsDbContext>().Database.BeginTransactionAsync(Token);

            await Should.ThrowAsync<ContentAccessDeniedException>(() => service.PatchAsync("article", id, new() { ["region"] = "us", ["subtitle"] = "Refused" }, 1, Token));
            var kept = await service.PatchAsync("article", id, new() { ["subtitle"] = "Kept" }, 1, Token);

            await transaction.CommitAsync(Token);
            return kept;
        });

        var stored = (await database.GetAsync(Editor, id))!;
        stored["region"].ShouldBe("eu");
        stored["subtitle"].ShouldBe("Kept");
        stored["version"].ShouldBe(2);
        database.Versions(id).Count.ShouldBe(2);
    }

    [Fact]
    public async Task AWriteInATransactionSomeoneElseBeganIsNeverTold()
    {
        var recorder = new Recorder();
        using var database = new ServiceDatabase(cms => cms.Services.AddSingleton<IEntryChangeSink>(recorder));

        var (rolledBack, next) = await database.AsAsync(Editor, async services =>
        {
            var context = services.GetRequiredService<CmsDbContext>();
            var service = services.GetRequiredService<IContentService>();
            Guid first;
            await using (var transaction = await context.Database.BeginTransactionAsync(Token))
            {
                first = IdOf(await service.CreateAsync("article", Titled("Rolled back"), Token));
                recorder.Changes.ShouldBeEmpty();
                await transaction.RollbackAsync(Token);
            }

            var second = IdOf(await service.CreateAsync("article", Titled("Next"), Token));
            return (first, second);
        });

        recorder.Changes.ShouldHaveSingleItem().Id.ShouldBe(next);
        recorder.Changes.ShouldNotContain(change => change.Id == rolledBack);

        // The end of that transaction cannot be seen, so a commit is not told either.
        recorder.Changes.Clear();
        await database.AsAsync(Editor, async services =>
        {
            await using var transaction = await services.GetRequiredService<CmsDbContext>().Database.BeginTransactionAsync(Token);
            await services.GetRequiredService<IContentService>().CreateAsync("article", Titled("Committed"), Token);
            await transaction.CommitAsync(Token);
            return true;
        });

        recorder.Changes.ShouldBeEmpty();
        database.Read(context => context.Documents.Count()).ShouldBe(2);
    }

    [Fact]
    public async Task WritesInTheServicesOwnTransactionAreToldAfterItCommits()
    {
        var recorder = new Recorder();
        using var database = new ServiceDatabase(cms => cms.Services.AddSingleton<IEntryChangeSink>(recorder));

        await database.AsAsync(Editor, async services =>
        {
            var service = services.GetRequiredService<ContentService>();
            return await service.InTransactionAsync(
                async () =>
                {
                    await service.CreateAsync("article", Titled("One"), Token);
                    await Should.ThrowAsync<ValidationException>(() => service.CreateAsync("article", new() { ["views"] = 5000 }, Token));
                    await service.CreateAsync("article", Titled("Two"), Token);
                    recorder.Changes.ShouldBeEmpty();
                    return true;
                },
                Token);
        });

        recorder.Changes.Count.ShouldBe(2);
        database.Read(context => context.Documents.Count()).ShouldBe(2);
    }

    [Fact]
    public async Task WritesInTheServicesOwnTransactionAreNotToldWhenItRollsBack()
    {
        var recorder = new Recorder();
        using var database = new ServiceDatabase(cms => cms.Services.AddSingleton<IEntryChangeSink>(recorder));

        await Should.ThrowAsync<InvalidOperationException>(() => database.AsAsync<bool>(Editor, async services =>
        {
            var service = services.GetRequiredService<ContentService>();
            return await service.InTransactionAsync<bool>(
                async () =>
                {
                    await service.CreateAsync("article", Titled("One"), Token);
                    throw new InvalidOperationException("Not this time.");
                },
                Token);
        }));

        recorder.Changes.ShouldBeEmpty();
        database.Read(context => context.Documents.Count()).ShouldBe(0);
    }

    [Fact]
    public async Task AThrowingSinkDoesNotFailACommittedWriteAndTheOthersStillHear()
    {
        var recorder = new Recorder();
        using var database = new ServiceDatabase(cms =>
        {
            cms.Services.AddSingleton<IEntryChangeSink, Throwing>();
            cms.Services.AddSingleton<IEntryChangeSink>(recorder);
        });

        var entry = await database.CreateAsync(Editor, Titled("Hello"));

        entry["title"].ShouldBe("Hello");
        recorder.Changes.ShouldHaveSingleItem();
        database.Read(context => context.Documents.Count()).ShouldBe(1);
    }

    [Fact]
    public async Task ACallerCancelledAfterTheCommitStillGetsItsResult()
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(Token);
        using var database = new ServiceDatabase(cms => cms.Services.AddSingleton<IEntryChangeSink>(new Cancelling(source)));

        var entry = await database.AsAsync(Editor, services => services.GetRequiredService<IContentService>().CreateAsync("article", Titled("Hello"), source.Token));

        source.IsCancellationRequested.ShouldBeTrue();
        entry["title"].ShouldBe("Hello");
    }

    [Fact]
    public async Task DeleteAndRestoreTellOneChangePerLocale()
    {
        var recorder = new Recorder();
        using var database = new ServiceDatabase(cms => cms.Services.AddSingleton<IEntryChangeSink>(recorder));
        var entry = await database.CreateAsync(Editor, Titled("Hello"));
        var id = IdOf(entry);
        await database.UpdateAsync(Editor, id, Titled("Bonjour"), 0, locale: "fr");
        recorder.Changes.Clear();

        await database.DeleteAsync(Editor, id);
        recorder.Changes.Select(change => (change.Kind, change.Locale)).ShouldBe([(EntryChangeKind.Trashed, "en"), (EntryChangeKind.Trashed, "fr")], ignoreOrder: true);

        recorder.Changes.Clear();
        await database.RestoreAsync(Editor, id);
        recorder.Changes.Select(change => (change.Kind, change.Locale)).ShouldBe([(EntryChangeKind.Restored, "en"), (EntryChangeKind.Restored, "fr")], ignoreOrder: true);
    }
}
