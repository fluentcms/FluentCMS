using FluentCMS.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Publishing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Storage;

public class SystemModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 30, 0, TimeSpan.Zero);

    private static StorageDatabase NewDatabase() => new(SampleSchema.FromClasses().Build());

    private static string[] IndexColumns(IEntityType entity, string name) =>
        [.. entity.GetIndexes().Single(index => index.GetDatabaseName() == name).Properties.Select(property => property.GetColumnName())];

    private static Document NewDocument(Guid? documentId = null, string locale = "") => new()
    {
        DocumentId = documentId ?? Guid.CreateVersion7(),
        Locale = locale,
        Type = "tag",
        Status = EntryStatus.Draft,
        DraftVersion = 1,
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    [Fact]
    public void DocumentHasTheColumnsAndKeysOfTheSpecification()
    {
        using var database = NewDatabase();
        using var context = database.CreateContext();
        var entity = context.Model.FindEntityType(typeof(Document))!;

        entity.GetTableName().ShouldBe("cms_document");
        entity.GetProperties().Select(property => property.GetColumnName()).Order(StringComparer.Ordinal).ShouldBe(
        [
            "created_at", "created_by", "deleted_at", "deleted_by", "document_id", "draft_version", "first_published_at", "id", "locale",
            "published_at", "published_version", "schedule_publish_at", "schedule_unpublish_at", "site_id", "status", "type", "updated_at", "updated_by",
        ]);
        entity.FindPrimaryKey()!.Properties.Select(property => property.GetColumnName()).ShouldBe(["id"]);
        entity.FindProperty(nameof(Document.SiteId))!.IsNullable.ShouldBeTrue();
        entity.FindProperty(nameof(Document.Locale))!.IsNullable.ShouldBeFalse();
        IndexColumns(entity, "uq_cms_document_document_locale").ShouldBe(["document_id", "locale"]);
    }

    [Fact]
    public void ReferenceHasTheColumnsAndKeysOfTheSpecification()
    {
        using var database = NewDatabase();
        using var context = database.CreateContext();
        var entity = context.Model.FindEntityType(typeof(Reference))!;

        entity.GetTableName().ShouldBe("cms_reference");
        entity.GetProperties().Select(property => property.GetColumnName()).Order(StringComparer.Ordinal).ShouldBe(
            ["field", "id", "locale", "position", "source_document_id", "source_type", "state", "target_id", "target_type"]);
        entity.FindProperty(nameof(Reference.Locale))!.IsNullable.ShouldBeFalse();
        IndexColumns(entity, "uq_cms_reference_source").ShouldBe(["source_type", "source_document_id", "state", "locale", "field", "position"]);
        IndexColumns(entity, "ix_cms_reference_target").ShouldBe(["target_type", "target_id"]);
    }

    [Fact]
    public void VersionHasTheColumnsAndKeysOfTheSpecification()
    {
        using var database = NewDatabase();
        using var context = database.CreateContext();
        var entity = context.Model.FindEntityType(typeof(ContentVersion))!;

        entity.GetTableName().ShouldBe("cms_version");
        entity.GetProperties().Select(property => property.GetColumnName()).Order(StringComparer.Ordinal).ShouldBe(
            ["author_id", "client_id", "created_at", "document_id", "id", "locale", "message", "number", "origin", "snapshot", "status_after"]);
        IndexColumns(entity, "uq_cms_version_document_locale_number").ShouldBe(["document_id", "locale", "number"]);
    }

    [Fact]
    public void JobTablesHaveTheColumnsOfTheSpecification()
    {
        using var database = NewDatabase();
        using var context = database.CreateContext();

        var job = context.Model.FindEntityType(typeof(Job))!;
        job.GetTableName().ShouldBe("cms_job");
        job.GetProperties().Select(property => property.GetColumnName()).Order(StringComparer.Ordinal).ShouldBe(
            ["attempts", "completed_at", "created_at", "created_by", "id", "last_error", "lease_owner", "lease_until", "max_attempts", "origin", "payload", "priority", "queue", "run_at", "site_id", "status", "type"]);
        var claim = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Job))!.GetIndexes().Single(index => index.GetDatabaseName() == "ix_cms_job_claim");
        claim.Properties.Select(property => property.GetColumnName()).ShouldBe(["queue", "priority", "run_at", "id"]);
        claim.IsDescending.ShouldNotBeNull().ShouldBe([false, true, false, false]);
        claim.GetFilter().ShouldBe("status IN ('pending', 'running')");
        IndexColumns(job, "ix_cms_job_status_lease").ShouldBe(["status", "lease_until"]);

        var attempt = context.Model.FindEntityType(typeof(JobAttempt))!;
        attempt.GetTableName().ShouldBe("cms_job_attempt");
        attempt.GetProperties().Select(property => property.GetColumnName()).Order(StringComparer.Ordinal).ShouldBe(
            ["attempt", "error", "finished_at", "job_id", "outcome", "started_at"]);
        attempt.FindPrimaryKey()!.Properties.Select(property => property.GetColumnName()).ShouldBe(["job_id", "attempt"]);

        // The specification lists no type, but a schedule has to say which job it fires.
        var recurring = context.Model.FindEntityType(typeof(RecurringJob))!;
        recurring.GetTableName().ShouldBe("cms_recurring_job");
        recurring.GetProperties().Select(property => property.GetColumnName()).Order(StringComparer.Ordinal).ShouldBe(
            ["cron", "enabled", "name", "next_run_at", "payload", "queue", "type"]);
    }

    [Fact]
    public void DocumentRoundTripsWithAGeneratedVersion7Id()
    {
        using var database = NewDatabase();
        Guid id;
        using (var context = database.CreateCreatedContext())
        {
            var document = NewDocument();
            document.Status = EntryStatus.Modified;
            document.SchedulePublishAt = Now.AddDays(1);
            context.Documents.Add(document);
            context.SaveChanges();
            id = document.Id;
        }

        id.Version.ShouldBe(7);

        using var reader = database.CreateContext();
        var stored = reader.Documents.Single(document => document.Id == id);
        stored.Locale.ShouldBe("");
        stored.SiteId.ShouldBeNull();
        stored.Status.ShouldBe(EntryStatus.Modified);
        stored.SchedulePublishAt.ShouldBe(Now.AddDays(1));
    }

    [Fact]
    public void DocumentStatusIsStoredByName()
    {
        using var database = NewDatabase();
        using var context = database.CreateCreatedContext();
        var document = NewDocument();
        document.Status = EntryStatus.Unpublished;
        context.Documents.Add(document);
        context.SaveChanges();

        using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT status FROM cms_document";
        command.ExecuteScalar().ShouldBe("Unpublished");
    }

    [Fact]
    public void DocumentIsUniquePerLocale()
    {
        using var database = NewDatabase();
        using var context = database.CreateCreatedContext();
        var documentId = Guid.CreateVersion7();
        context.Documents.AddRange(NewDocument(documentId, "en"), NewDocument(documentId, "fr"));
        context.SaveChanges();
        context.ChangeTracker.Clear();

        context.Documents.Add(NewDocument(documentId, "en"));

        Should.Throw<DbUpdateException>(() => context.SaveChanges());
    }

    [Fact]
    public void TrashedDocumentIsHiddenUntilFiltersAreIgnored()
    {
        using var database = NewDatabase();
        Guid live, trashed;
        using (var context = database.CreateCreatedContext())
        {
            var first = NewDocument();
            var second = NewDocument();
            second.DeletedAt = Now;
            second.DeletedBy = Guid.CreateVersion7();
            context.Documents.AddRange(first, second);
            context.SaveChanges();
            (live, trashed) = (first.DocumentId, second.DocumentId);
        }

        using var reader = database.CreateContext();
        reader.Documents.Select(document => document.DocumentId).ShouldBe([live]);
        reader.Documents.IgnoreQueryFilters().Select(document => document.DocumentId).ToList().ShouldBe([live, trashed], ignoreOrder: true);
    }

    [Fact]
    public void ReferenceRoundTripsWithAnEmptyLocale()
    {
        using var database = NewDatabase();
        var source = Guid.CreateVersion7();
        var target = Guid.CreateVersion7();
        using (var context = database.CreateCreatedContext())
        {
            context.References.Add(new Reference
            {
                SourceType = "article",
                SourceDocumentId = source,
                State = ContentState.Draft,
                Field = "author",
                TargetType = "author",
                TargetId = target,
            });
            context.SaveChanges();
        }

        using var reader = database.CreateContext();
        var stored = reader.References.Single(reference => reference.TargetType == "author" && reference.TargetId == target);
        stored.Id.Version.ShouldBe(7);
        stored.SourceDocumentId.ShouldBe(source);
        stored.Locale.ShouldBe("");
    }

    [Fact]
    public void ReferenceKeyIsEnforcedForNonLocalizedValues()
    {
        using var database = NewDatabase();
        using var context = database.CreateCreatedContext();
        var source = Guid.CreateVersion7();
        Reference Duplicate() => new()
        {
            SourceType = "article",
            SourceDocumentId = source,
            State = ContentState.Draft,
            Field = "tags",
            Position = 0,
            TargetType = "tag",
            TargetId = Guid.CreateVersion7(),
        };

        context.References.Add(Duplicate());
        context.SaveChanges();
        context.References.Add(Duplicate());

        Should.Throw<DbUpdateException>(() => context.SaveChanges());
    }

    [Fact]
    public void VersionRoundTrips()
    {
        using var database = NewDatabase();
        var documentId = Guid.CreateVersion7();
        var author = Guid.CreateVersion7();
        using (var context = database.CreateCreatedContext())
        {
            context.Versions.Add(new ContentVersion
            {
                DocumentId = documentId,
                Number = 3,
                Snapshot = """{"title":"Hello"}""",
                StatusAfter = EntryStatus.Published,
                AuthorId = author,
                Origin = Origin.Mcp,
                ClientId = "agent",
                Message = "Fix typo",
                CreatedAt = Now,
            });
            context.SaveChanges();
        }

        using var reader = database.CreateContext();
        var stored = reader.Versions.Single(version => version.DocumentId == documentId && version.Number == 3);
        stored.Snapshot.ShouldBe("""{"title":"Hello"}""");
        stored.StatusAfter.ShouldBe(EntryStatus.Published);
        stored.Origin.ShouldBe(Origin.Mcp);
        stored.AuthorId.ShouldBe(author);
        stored.ClientId.ShouldBe("agent");
        stored.Message.ShouldBe("Fix typo");
        stored.CreatedAt.ShouldBe(Now);
    }
}
