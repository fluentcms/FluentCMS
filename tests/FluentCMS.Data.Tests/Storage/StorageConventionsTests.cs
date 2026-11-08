using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Publishing;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Storage;

public class StorageConventionsTests
{
    private static StorageDatabase NewDatabase() => new(SampleSchema.FromClasses().Build(), new SqliteLikeConventions());

    private static Dictionary<string, object> Row(DateTimeOffset publishedOn) => new()
    {
        ["document_id"] = Guid.CreateVersion7(),
        ["site_id"] = Guid.CreateVersion7(),
        ["state"] = ContentState.Published,
        ["published_on"] = publishedOn,
    };

    [Fact]
    public void ModelWideConventionReachesContentAndSystemColumns()
    {
        using var database = NewDatabase();
        using var context = database.CreateContext();

        context.Model.FindEntityType("cms_article")!.FindProperty("published_on")!.GetTypeMapping().Converter!.ProviderClrType.ShouldBe(typeof(long));
        context.Model.FindEntityType(typeof(Document))!.FindProperty(nameof(Document.SchedulePublishAt))!.GetTypeMapping().Converter!.ProviderClrType.ShouldBe(typeof(long));
    }

    [Fact]
    public void DatesCanBeComparedAndSorted()
    {
        using var database = NewDatabase();
        using var context = database.CreateCreatedContext();
        var earlier = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.FromHours(5));
        var later = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        context.Rows("cms_article").AddRange(Row(later), Row(earlier));
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var dates = context.Rows("cms_article")
            .Where(row => EF.Property<DateTimeOffset?>(row, "published_on") > earlier.AddMinutes(-1))
            .OrderBy(row => EF.Property<DateTimeOffset?>(row, "published_on"))
            .Select(row => EF.Property<DateTimeOffset?>(row, "published_on"))
            .ToList();

        dates.ShouldBe([earlier, later]);
    }

    [Fact]
    public void ADateIsKeptToTheMicrosecondAndAParameterIsCutAsTheColumn()
    {
        using var database = NewDatabase();
        using var context = database.CreateCreatedContext();
        var written = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.FromHours(5)).AddTicks(1_234_567);
        context.Rows("cms_article").Add(Row(written));
        context.SaveChanges();
        context.ChangeTracker.Clear();

        context.Rows("cms_article").Select(row => EF.Property<DateTimeOffset?>(row, "published_on")).Single().ShouldBe(written.AddTicks(-7));
        context.Rows("cms_article").Count(row => EF.Property<DateTimeOffset?>(row, "published_on") == written).ShouldBe(1);
    }

    [Fact]
    public void JsonColumnHookReachesContentAndSystemColumns()
    {
        using var database = NewDatabase();
        using var context = database.CreateCreatedContext();

        var row = Row(DateTimeOffset.UnixEpoch);
        row["sections"] = "not json";
        context.Rows("cms_article").Add(row);
        Should.Throw<DbUpdateException>(() => context.SaveChanges());
        context.ChangeTracker.Clear();

        context.Versions.Add(new ContentVersion { DocumentId = Guid.CreateVersion7(), Number = 1, Snapshot = "not json", StatusAfter = EntryStatus.Draft });
        Should.Throw<DbUpdateException>(() => context.SaveChanges());
    }

    [Fact]
    public void JsonIndexHookReceivesIndexedJsonFields()
    {
        var schema = SampleSchema.NewBuilder().AddCollection("page", type => type.Field("data", "json", field => field.Indexed())).Build();
        using var database = new StorageDatabase(schema, new SqliteLikeConventions());
        using var context = database.CreateContext();

        context.Model.FindEntityType("cms_page")!.GetIndexes().Select(index => index.GetDatabaseName()).ShouldContain("gin_cms_page_data");
    }
}
