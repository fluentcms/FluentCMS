using System.Collections.Frozen;
using System.Reflection;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Sdk.Tests.Schema;

public class ContentItemTests
{
    private sealed class Article : ContentItem
    {
        public string Title { get; set; } = "";
    }

    [Fact]
    public void ReservedFieldNamesAreTheSystemFieldsFallbacksTheSchedulesAndTheTrashMarks()
    {
        ContentItem.ReservedFieldNames.Order(StringComparer.Ordinal).ShouldBe(
            ["createdAt", "createdBy", "deletedAt", "deletedBy", "fallbacks", "id", "locale", "publishedAt", "scheduledPublishAt", "scheduledUnpublishAt", "status", "updatedAt", "updatedBy", "version"]);
    }

    [Fact]
    public void EverySystemPropertyIsReserved()
    {
        var properties = typeof(ContentItem).GetProperties(BindingFlags.Instance | BindingFlags.Public).Select(p => p.Name);

        foreach (var name in properties)
        {
            ContentItem.ReservedFieldNames.ShouldContain(name);
        }
    }

    [Fact]
    public void ReservedNamesMatchWithoutRegardToCase()
    {
        ContentItem.ReservedFieldNames.Contains("Id").ShouldBeTrue();
        ContentItem.ReservedFieldNames.Contains("CREATEDAT").ShouldBeTrue();
        ContentItem.ReservedFieldNames.Contains("title").ShouldBeFalse();
        ContentItem.ReservedFieldNames.Contains("").ShouldBeFalse();
    }

    [Fact]
    public void ReservedNamesAreAFrozenSet()
    {
        ContentItem.ReservedFieldNames.ShouldBeAssignableTo<FrozenSet<string>>();
        ContentItem.ReservedFieldNames.ShouldBeSameAs(ContentItem.ReservedFieldNames);
    }

    [Fact]
    public void DerivedTypesInheritTheSystemFields()
    {
        var published = new DateTimeOffset(2026, 11, 1, 10, 41, 0, TimeSpan.Zero);
        var id = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var article = new Article
        {
            Id = id,
            Status = EntryStatus.Published,
            Locale = "fr-CA",
            Version = 7,
            CreatedAt = published.AddDays(-1),
            CreatedBy = id,
            UpdatedAt = published.AddMinutes(-1),
            UpdatedBy = id,
            PublishedAt = published,
            Title = "Bonjour",
        };

        article.Id.ShouldBe(id);
        article.Status.ShouldBe(EntryStatus.Published);
        article.Locale.ShouldBe("fr-CA");
        article.Version.ShouldBe(7);
        article.PublishedAt.ShouldBe(published);
    }

    [Fact]
    public void NewItemsStartAsUnpublishedDrafts()
    {
        var article = new Article();

        article.Status.ShouldBe(EntryStatus.Draft);
        article.Locale.ShouldBe("");
        article.PublishedAt.ShouldBeNull();
        article.Id.ShouldBe(Guid.Empty);
    }
}
