using FluentCMS.Content;
using FluentCMS.Core.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

// A job acts for someone: what it writes records them, and what it may do stays the system principal's.
public sealed class ContentServiceAuditTests : IDisposable
{
    private readonly ServiceDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private static readonly Principal Scheduler = new(PrincipalKind.AdminUser, Guid.CreateVersion7(), "Scheduler");

    private async Task<T> AsJobAsync<T>(Principal? actor, Func<IContentService, Task<T>> work)
    {
        using var scope = _database.Database.NewScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>();
        accessor.Current = new OperationContext(Principal.System, Origin.Job, SiteA, "en", null, "job", DateTimeOffset.UtcNow) { Actor = actor };
        try
        {
            return await work(scope.ServiceProvider.GetRequiredService<IContentService>());
        }
        finally
        {
            accessor.Current = null;
        }
    }

    private List<Document> Documents(Guid id) =>
        _database.Read(context => context.Documents.IgnoreQueryFilters().AsNoTracking().Where(document => document.DocumentId == id).ToList());

    [Fact]
    public async Task AJobWithAnActorRecordsTheActorAndKeepsTheSystemPrincipalsAccess()
    {
        // The actor holds no role, so all of this is the system principal's access: a hidden and a read-only field,
        // which no client may write, and the trash, with no grant behind any of it.
        var entry = await AsJobAsync(Scheduler, service => service.CreateAsync("article", new() { ["title"] = "T", ["secret"] = "s", ["stamp"] = "x" }, Token));
        var id = IdOf(entry);

        var created = Documents(id).ShouldHaveSingleItem();
        created.CreatedBy.ShouldBe(Scheduler.Id);
        created.UpdatedBy.ShouldBe(Scheduler.Id);
        _database.Versions(id).ShouldHaveSingleItem().AuthorId.ShouldBe(Scheduler.Id);
        _database.Row("cms_article", id)["secret"].ShouldBe("s");

        var editor = await _database.CreateAsync(Editor, new() { ["title"] = "By an editor" });
        await AsJobAsync(Scheduler, service => service.PatchAsync("article", IdOf(editor), new() { ["stamp"] = "y" }, 1, Token));
        var updated = Documents(IdOf(editor)).ShouldHaveSingleItem();
        updated.CreatedBy.ShouldBe(Editor.Id);
        updated.UpdatedBy.ShouldBe(Scheduler.Id);
        _database.Versions(IdOf(editor)).Last().AuthorId.ShouldBe(Scheduler.Id);

        await AsJobAsync<object?>(Scheduler, async service =>
        {
            await service.DeleteAsync("article", id, Token);
            return null;
        });
        Documents(id).ShouldHaveSingleItem().DeletedBy.ShouldBe(Scheduler.Id);

        await AsJobAsync(Scheduler, service => service.RestoreAsync("article", id, Token));
        Documents(id).ShouldHaveSingleItem().DeletedBy.ShouldBeNull();
    }

    [Fact]
    public async Task AnActorGivesAClientPrincipalNothingEither()
    {
        var refused = _database.AsAsync(
            Principal.Anonymous,
            services =>
            {
                var accessor = services.GetRequiredService<IOperationContextAccessor>();
                accessor.Current = accessor.Current! with { Actor = Scheduler };
                return services.GetRequiredService<IContentService>().CreateAsync("article", new() { ["title"] = "T" }, Token);
            });

        await Should.ThrowAsync<ContentAccessDeniedException>(() => refused);
    }

    [Fact]
    public async Task AnEntryAJobWroteForAnActorIsTheActorsOwn()
    {
        var author = AuthorRole(Guid.CreateVersion7());

        var entry = await AsJobAsync(author, service => service.CreateAsync("article", new() { ["title"] = "T" }, Token));

        (await _database.GetAsync(author, IdOf(entry))).ShouldNotBeNull();
        (await _database.GetAsync(AuthorRole(Guid.CreateVersion7()), IdOf(entry))).ShouldBeNull();
    }

    [Fact]
    public async Task ASystemOperationWithNoActorPassesAsAJobContextAndRecordsTheSystemPrincipal()
    {
        var context = new OperationContext(Principal.System, Origin.Job, SiteA, "en", null, "job", DateTimeOffset.UtcNow);
        context.Actor.ShouldBeNull();
        context.AuditPrincipal.ShouldBeSameAs(Principal.System);
        SystemOperation.Allows(context.Origin).ShouldBeTrue();

        var entry = await AsJobAsync(null, service => service.CreateAsync("article", new() { ["title"] = "T" }, Token));

        var created = Documents(IdOf(entry)).ShouldHaveSingleItem();
        created.CreatedBy.ShouldBe(Principal.System.Id);
        created.UpdatedBy.ShouldBe(Principal.System.Id);
        _database.Versions(IdOf(entry)).ShouldHaveSingleItem().AuthorId.ShouldBe(Principal.System.Id);
    }

    [Fact]
    public async Task AJobContextBegunThroughSystemOperationIsTheSame()
    {
        using var scope = _database.Database.NewScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>();

        using (SystemOperation.Begin(accessor, Origin.Job, SiteA))
        {
            accessor.Current!.Actor.ShouldBeNull();
            accessor.Current.AuditPrincipal.ShouldBeSameAs(Principal.System);

            var entry = await scope.ServiceProvider.GetRequiredService<IContentService>().CreateAsync("article", new() { ["title"] = "T" }, Token);
            entry["title"].ShouldBe("T");
        }
    }
}
