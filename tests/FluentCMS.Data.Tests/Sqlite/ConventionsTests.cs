using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Sqlite;

public sealed class ConventionsTests : IDisposable
{
    private const string Post = "cms_post";

    // SQLITE_CONSTRAINT_CHECK.
    private const int CheckConstraintFailed = 275;

    private static readonly SchemaModel Schema = SampleSchema.NewBuilder()
        .AddCollection("post", type => type
            .Field("publishedOn", "dateTime")
            .Field("metadata", "json"))
        .Build();

    // In UTC order, which is not the order of their local times.
    private static readonly DateTimeOffset Earliest = new(2026, 11, 1, 12, 0, 0, TimeSpan.FromHours(5));
    private static readonly DateTimeOffset Middle = new(2026, 11, 1, 8, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Latest = new(2026, 11, 1, 6, 0, 0, TimeSpan.FromHours(-5));

    private readonly SqliteFileDatabase _database = new();
    private readonly ServiceProvider _services;
    private readonly IServiceScope _scope;
    private readonly CmsDbContext _context;

    public ConventionsTests()
    {
        _services = _database.CreateServices(Schema);
        _scope = _services.CreateScope();
        _scope.ServiceProvider.GetRequiredService<SystemDbContext>().Database.Migrate();
        _context = _scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        _context.GetService<IRelationalDatabaseCreator>().CreateTables();
    }

    public void Dispose()
    {
        _scope.Dispose();
        _services.Dispose();
        _database.Dispose();
    }

    private static Document NewDocument(DateTimeOffset schedule) => new()
    {
        DocumentId = Guid.CreateVersion7(),
        Type = "post",
        Status = EntryStatus.Draft,
        CreatedAt = schedule,
        UpdatedAt = schedule,
        SchedulePublishAt = schedule,
    };

    private void AddPost(DateTimeOffset publishedOn, string? metadata = null) =>
        _context.Rows(Post).Add(new Dictionary<string, object>
        {
            ["document_id"] = Guid.CreateVersion7(),
            ["site_id"] = Guid.Empty,
            ["state"] = ContentState.Published,
            ["published_on"] = publishedOn,
            ["metadata"] = metadata!,
        });

    private string ColumnType(string table, string column)
    {
        _context.Database.OpenConnection();
        using var command = _context.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"select type from pragma_table_info('{table}') where name = '{column}'";
        return (string)command.ExecuteScalar()!;
    }

    [SqliteFact("the SQLite storage conventions")]
    public void ContentDatesCompareAndSortAcrossOffsets()
    {
        AddPost(Latest);
        AddPost(Earliest);
        AddPost(Middle);
        _context.SaveChanges();

        var sorted = _context.Rows(Post)
            .OrderBy(row => EF.Property<DateTimeOffset?>(row, "published_on"))
            .Select(row => EF.Property<DateTimeOffset?>(row, "published_on"))
            .ToList();
        sorted.ShouldBe([Earliest, Middle, Latest]);

        _context.Rows(Post).Count(row => EF.Property<DateTimeOffset?>(row, "published_on") > Middle).ShouldBe(1);
        ColumnType(Post, "published_on").ShouldBe("INTEGER");
    }

    [SqliteFact("the SQLite storage conventions")]
    public void SystemDatesCompareAndSortAcrossOffsets()
    {
        _context.Documents.AddRange(NewDocument(Latest), NewDocument(Earliest), NewDocument(Middle));
        _context.SaveChanges();

        var sorted = _context.Documents.OrderByDescending(document => document.SchedulePublishAt).Select(document => document.SchedulePublishAt).ToList();
        sorted.ShouldBe([Latest, Middle, Earliest]);

        _context.Documents.Count(document => document.SchedulePublishAt <= Middle).ShouldBe(2);
        ColumnType("cms_document", "schedule_publish_at").ShouldBe("INTEGER");
        ColumnType("cms_version", "created_at").ShouldBe("INTEGER");
    }

    [SqliteFact("the SQLite storage conventions")]
    public void ADateReadsBackAsTheSameInstantInUtc()
    {
        _context.Documents.Add(NewDocument(Earliest));
        _context.SaveChanges();
        _context.ChangeTracker.Clear();

        var read = _context.Documents.Single().SchedulePublishAt!.Value;
        read.ShouldBe(Earliest);
        read.Offset.ShouldBe(TimeSpan.Zero);
    }

    [SqliteFact("the SQLite storage conventions")]
    public void AContentJsonColumnRejectsMalformedJson()
    {
        AddPost(Middle, "{not json");

        var exception = Should.Throw<DbUpdateException>(() => _context.SaveChanges());
        exception.InnerException.ShouldBeOfType<SqliteException>().SqliteExtendedErrorCode.ShouldBe(CheckConstraintFailed);
    }

    [SqliteFact("the SQLite storage conventions")]
    public void ASystemJsonColumnRejectsMalformedJson()
    {
        _context.Versions.Add(new ContentVersion { DocumentId = Guid.CreateVersion7(), Number = 1, Snapshot = "{not json", CreatedAt = Middle });

        var exception = Should.Throw<DbUpdateException>(() => _context.SaveChanges());
        exception.InnerException.ShouldBeOfType<SqliteException>().SqliteExtendedErrorCode.ShouldBe(CheckConstraintFailed);
    }

    [SqliteFact("the SQLite storage conventions")]
    public void AJsonColumnAcceptsValidJsonAndNull()
    {
        AddPost(Middle, """{"a":1}""");
        AddPost(Middle);

        _context.SaveChanges().ShouldBe(2);
    }
}
