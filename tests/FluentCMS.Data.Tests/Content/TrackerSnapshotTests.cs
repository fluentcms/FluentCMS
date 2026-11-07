using FluentCMS.Core.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

public sealed class TrackerSnapshotTests
{
    private static Site NewSite(string slug) => new() { Id = Guid.CreateVersion7(), Name = slug, Slug = slug, DefaultLocale = "en", CreatedAt = DateTimeOffset.UtcNow };

    // What a write that failed did to the caller's entries, saved before its rollback or not, is undone, and what it added goes.
    [Fact]
    public async Task ACallersEntriesGoBackToTheirStateAndValuesAndTheWritesGo()
    {
        using var database = new ServiceDatabase();
        using var scope = database.Database.NewScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        var unchanged = await context.Set<Site>().SingleAsync(site => site.Slug == "a", Token);
        var modified = await context.Set<Site>().SingleAsync(site => site.Slug == "b", Token);
        modified.Name = "Renamed";
        var added = NewSite("added");
        context.Add(added);
        var deleted = await context.Set<SiteLocale>().SingleAsync(locale => locale.SiteId == unchanged.Id && locale.Code == "fr-CA", Token);
        context.Remove(deleted);

        var snapshot = TrackerSnapshot.Take(context);
        await using (var transaction = await context.Database.BeginTransactionAsync(Token))
        {
            unchanged.Name = "Changed by the write";
            modified.Name = "Renamed again";
            added.Name = "Changed too";
            context.Add(NewSite("the-writes"));
            await context.SaveChangesAsync(Token);
            await transaction.RollbackAsync(Token);
        }

        snapshot.Restore();

        context.ChangeTracker.Entries().Select(entry => entry.Entity).ShouldBe([unchanged, modified, added, deleted], ignoreOrder: true);
        (context.Entry(unchanged).State, unchanged.Name).ShouldBe((EntityState.Unchanged, "a"));
        (context.Entry(modified).State, modified.Name).ShouldBe((EntityState.Modified, "Renamed"));
        context.Entry(modified).Property(site => site.Name).OriginalValue.ShouldBe("b");
        context.Entry(modified).Properties.Where(property => property.IsModified).Select(property => property.Metadata.Name).ShouldBe([nameof(Site.Name)]);
        (context.Entry(added).State, added.Name).ShouldBe((EntityState.Added, "added"));
        context.Entry(deleted).State.ShouldBe(EntityState.Deleted);

        await context.SaveChangesAsync(Token);
        string[] slugs = ["a", "b", "added", "the-writes"];
        database.Read(read => read.Set<Site>().Where(site => slugs.Contains(site.Slug)).Select(site => site.Slug + "=" + site.Name).ToList())
            .ShouldBe(["a=a", "added=added", "b=Renamed"], ignoreOrder: true);
        database.Read(read => read.Set<SiteLocale>().Count(locale => locale.SiteId == unchanged.Id)).ShouldBe(2);
    }
}
