using FluentCMS.Core.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluentCMS.Core.Storage;

// Maps the system tables that have a CLR class. Columns are the snake_case of the property names.
internal sealed class SystemModelBuilder(IStorageConventions conventions)
{
    private const int NameLength = 64;
    private const int StatusLength = 16;
    private const int RoleNameLength = 128;
    private const int RoleDescriptionLength = 512;
    private const int PermissionLength = 128;
    private const int SiteNameLength = 128;
    private const int SlugLength = 64;
    private const int JobTypeLength = 128;
    private const int QueueLength = 64;
    private const int LeaseOwnerLength = 128;
    private const int RecurringNameLength = 128;
    private const int CronLength = 128;
    private const int EventTypeLength = 256;
    private const int HandlerLength = 128;
    private const int ActorNameLength = 128;
    private const int ClientIdLength = 128;
    private const int ActionLength = 64;
    private const int TargetIdLength = 128;
    private const int TargetLabelLength = 256;
    private const int CorrelationIdLength = 128;
    private const int IpLength = 45;

    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Job>(MapJob);
        modelBuilder.Entity<JobAttempt>(MapJobAttempt);
        modelBuilder.Entity<RecurringJob>(MapRecurringJob);
        modelBuilder.Entity<OutboxMessage>(MapOutbox);
        modelBuilder.Entity<ActivityRow>(MapActivity);
        modelBuilder.Entity<Site>(MapSite);
        modelBuilder.Entity<SiteLocale>(MapSiteLocale);
        modelBuilder.Entity<Document>(MapDocument);
        modelBuilder.Entity<Reference>(MapReference);
        modelBuilder.Entity<ContentVersion>(MapVersion);
        modelBuilder.Entity<Role>(MapRole);
        modelBuilder.Entity<RoleGrant>(MapRoleGrant);
    }

    // For the system stream only. EF puts seed data into a context's migrations and EnsureCreated even for a table
    // marked ExcludeFromMigrations, so on CmsDbContext it would insert the rows a second time.
    public static void ApplySeedData(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Site>().HasData(SiteSeed.Site());
        modelBuilder.Entity<SiteLocale>().HasData(SiteSeed.Locale());
        modelBuilder.Entity<Role>().HasData([.. RoleSeed.Roles(), SiteSeed.PublicRole()]);
        modelBuilder.Entity<RoleGrant>().HasData(RoleSeed.Permissions());
    }

    private static void MapSite(EntityTypeBuilder<Site> entity)
    {
        entity.ToTable(StorageNames.Site);
        MapColumnNames(entity);
        entity.Property(site => site.Id).HasValueGenerator<GuidV7ValueGenerator>();
        entity.Property(site => site.Name).HasMaxLength(SiteNameLength);
        entity.Property(site => site.Slug).HasMaxLength(SlugLength);
        entity.Property(site => site.DefaultLocale).HasMaxLength(StorageNames.LocaleLength);

        entity.HasIndex(site => site.Slug).IsUnique().HasDatabaseName("uq_cms_site_slug");
    }

    private void MapSiteLocale(EntityTypeBuilder<SiteLocale> entity)
    {
        entity.ToTable(StorageNames.SiteLocale);
        MapColumnNames(entity);
        entity.HasKey(locale => new { locale.SiteId, locale.Code });
        entity.Property(locale => locale.Code).HasMaxLength(StorageNames.LocaleLength);
        conventions.ConfigureJsonColumn(entity, entity.Property(locale => locale.Fallbacks));

        entity.HasOne<Site>().WithMany().HasForeignKey(locale => locale.SiteId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void MapDocument(EntityTypeBuilder<Document> entity)
    {
        entity.ToTable(StorageNames.Document);
        MapColumnNames(entity);
        entity.Property(document => document.Id).HasValueGenerator<GuidV7ValueGenerator>();
        entity.Property(document => document.Locale).HasMaxLength(StorageNames.LocaleLength);
        entity.Property(document => document.Type).HasMaxLength(NameLength);
        entity.Property(document => document.Status).HasConversion<string>().HasMaxLength(StatusLength);

        entity.HasIndex(document => new { document.DocumentId, document.Locale })
            .IsUnique()
            .HasDatabaseName("uq_cms_document_document_locale");

        // Trashed rows stay in the table; IgnoreQueryFilters() reads them.
        entity.HasQueryFilter(document => document.DeletedAt == null);
    }

    private static void MapReference(EntityTypeBuilder<Reference> entity)
    {
        entity.ToTable(StorageNames.Reference);
        MapColumnNames(entity);
        entity.Property(reference => reference.Id).HasValueGenerator<GuidV7ValueGenerator>();
        entity.Property(reference => reference.SourceType).HasMaxLength(NameLength);
        entity.Property(reference => reference.Locale).HasMaxLength(StorageNames.LocaleLength);
        entity.Property(reference => reference.Field).HasMaxLength(NameLength);
        entity.Property(reference => reference.TargetType).HasMaxLength(NameLength);

        entity.HasIndex(reference => new { reference.SourceType, reference.SourceDocumentId, reference.State, reference.Locale, reference.Field, reference.Position })
            .IsUnique()
            .HasDatabaseName("uq_cms_reference_source");

        entity.HasIndex(reference => new { reference.TargetType, reference.TargetId })
            .HasDatabaseName("ix_cms_reference_target");
    }

    private void MapVersion(EntityTypeBuilder<ContentVersion> entity)
    {
        entity.ToTable(StorageNames.Version);
        MapColumnNames(entity);
        entity.Property(version => version.Id).HasValueGenerator<GuidV7ValueGenerator>();
        entity.Property(version => version.Locale).HasMaxLength(StorageNames.LocaleLength);
        entity.Property(version => version.StatusAfter).HasConversion<string>().HasMaxLength(StatusLength);
        entity.Property(version => version.Origin).HasConversion<string>().HasMaxLength(StatusLength);
        conventions.ConfigureJsonColumn(entity, entity.Property(version => version.Snapshot));

        entity.HasIndex(version => new { version.DocumentId, version.Locale, version.Number })
            .IsUnique()
            .HasDatabaseName("uq_cms_version_document_locale_number");
    }

    private static void MapRole(EntityTypeBuilder<Role> entity)
    {
        entity.ToTable(StorageNames.Role);
        MapColumnNames(entity);
        entity.Property(role => role.Id).HasValueGenerator<GuidV7ValueGenerator>();
        entity.Property(role => role.Name).HasMaxLength(RoleNameLength);
        entity.Property(role => role.Description).HasMaxLength(RoleDescriptionLength);
        entity.Property(role => role.PermissionsVersion).IsConcurrencyToken();

        // A site's roles go with the site, and their grants with them.
        entity.HasOne<Site>().WithMany().HasForeignKey(role => role.SiteId).OnDelete(DeleteBehavior.Cascade);

        // A unique index treats nulls as distinct on some databases, so instance-wide and site roles get one each.
        entity.HasIndex(role => role.Name)
            .IsUnique()
            .HasFilter("site_id IS NULL")
            .HasDatabaseName("uq_cms_role_name");

        entity.HasIndex(role => new { role.SiteId, role.Name })
            .IsUnique()
            .HasFilter("site_id IS NOT NULL")
            .HasDatabaseName("uq_cms_role_site_name");
    }

    private void MapRoleGrant(EntityTypeBuilder<RoleGrant> entity)
    {
        entity.ToTable(StorageNames.RolePermission);
        MapColumnNames(entity);
        entity.Property(grant => grant.Id).HasValueGenerator<GuidV7ValueGenerator>();
        entity.Property(grant => grant.Permission).HasMaxLength(PermissionLength);
        conventions.ConfigureJsonColumn(entity, entity.Property(grant => grant.Constraints));

        entity.HasOne<Role>().WithMany().HasForeignKey(grant => grant.RoleId).OnDelete(DeleteBehavior.Cascade);
        entity.HasIndex(grant => new { grant.RoleId, grant.Permission })
            .HasDatabaseName("ix_cms_role_permission_role_permission");
    }

    private void MapJob(EntityTypeBuilder<Job> entity)
    {
        entity.ToTable(StorageNames.Job);
        MapColumnNames(entity);
        entity.Property(job => job.Id).HasValueGenerator<GuidV7ValueGenerator>();
        entity.Property(job => job.Type).HasMaxLength(JobTypeLength);
        entity.Property(job => job.Queue).HasMaxLength(QueueLength);
        entity.Property(job => job.Status).HasMaxLength(StatusLength);
        entity.Property(job => job.LeaseOwner).HasMaxLength(LeaseOwnerLength);
        entity.Property(job => job.LastError).HasMaxLength(Job.LastErrorMaxLength);
        entity.Property(job => job.Origin).HasConversion<string>().HasMaxLength(StatusLength);
        conventions.ConfigureJsonColumn(entity, entity.Property(job => job.Payload));

        // In the order of the claim's ORDER BY, so that it seeks and does not sort, and over live jobs only, so that
        // finished ones are never walked. The claim must repeat this filter as literals for a database to use it.
        entity.HasIndex(job => new { job.Queue, job.Priority, job.RunAt, job.Id })
            .IsDescending(false, true, false, false)
            .HasFilter("status IN ('pending', 'running')")
            .HasDatabaseName("ix_cms_job_claim");

        entity.HasIndex(job => new { job.Status, job.LeaseUntil })
            .HasDatabaseName("ix_cms_job_status_lease");
    }

    private static void MapJobAttempt(EntityTypeBuilder<JobAttempt> entity)
    {
        entity.ToTable(StorageNames.JobAttempt);
        MapColumnNames(entity);
        entity.HasKey(attempt => new { attempt.JobId, attempt.Attempt });
        entity.Property(attempt => attempt.Outcome).HasMaxLength(StatusLength);
        entity.Property(attempt => attempt.Error).HasMaxLength(Job.LastErrorMaxLength);

        entity.HasOne<Job>().WithMany().HasForeignKey(attempt => attempt.JobId).OnDelete(DeleteBehavior.Cascade);
    }

    private void MapRecurringJob(EntityTypeBuilder<RecurringJob> entity)
    {
        entity.ToTable(StorageNames.RecurringJob);
        MapColumnNames(entity);
        entity.HasKey(job => job.Name);
        entity.Property(job => job.Name).HasMaxLength(RecurringNameLength);
        entity.Property(job => job.Cron).HasMaxLength(CronLength);
        entity.Property(job => job.Type).HasMaxLength(JobTypeLength);
        entity.Property(job => job.Queue).HasMaxLength(QueueLength);
        conventions.ConfigureJsonColumn(entity, entity.Property(job => job.Payload));
    }

    private void MapOutbox(EntityTypeBuilder<OutboxMessage> entity)
    {
        entity.ToTable(StorageNames.Outbox);
        MapColumnNames(entity);
        entity.Property(message => message.Id).HasValueGenerator<GuidV7ValueGenerator>();
        entity.Property(message => message.EventType).HasMaxLength(EventTypeLength);
        entity.Property(message => message.Handler).HasMaxLength(HandlerLength);
        entity.Property(message => message.Status).HasMaxLength(StatusLength);
        entity.Property(message => message.LeaseOwner).HasMaxLength(LeaseOwnerLength);
        entity.Property(message => message.LastError).HasMaxLength(Job.LastErrorMaxLength);
        conventions.ConfigureJsonColumn(entity, entity.Property(message => message.Payload));

        // One row per event and handler, so a retry of the write that raised the event cannot add a second delivery.
        entity.HasIndex(message => new { message.EventId, message.Handler })
            .IsUnique()
            .HasDatabaseName("uq_cms_outbox_event_handler");

        // Over rows still to deliver only, in the order the drain takes them, so that delivered rows are never walked.
        entity.HasIndex(message => new { message.Sequence, message.Id })
            .HasFilter("status = 'pending'")
            .HasDatabaseName("ix_cms_outbox_pending");

        // For the prune of delivered rows.
        entity.HasIndex(message => message.DeliveredAt)
            .HasFilter("status = 'delivered'")
            .HasDatabaseName("ix_cms_outbox_delivered");
    }

    private void MapActivity(EntityTypeBuilder<ActivityRow> entity)
    {
        entity.ToTable(StorageNames.Activity);
        MapColumnNames(entity);
        entity.Property(row => row.Id).HasValueGenerator<GuidV7ValueGenerator>();
        entity.Property(row => row.ActorType).HasConversion<string>().HasMaxLength(StatusLength);
        entity.Property(row => row.ActorName).HasMaxLength(ActorNameLength);
        entity.Property(row => row.Origin).HasConversion<string>().HasMaxLength(StatusLength);
        entity.Property(row => row.ClientId).HasMaxLength(ClientIdLength);
        entity.Property(row => row.Action).HasMaxLength(ActionLength);
        entity.Property(row => row.TargetType).HasMaxLength(NameLength);
        entity.Property(row => row.TargetId).HasMaxLength(TargetIdLength);
        entity.Property(row => row.TargetLabel).HasMaxLength(TargetLabelLength);
        entity.Property(row => row.Locale).HasMaxLength(StorageNames.LocaleLength);
        entity.Property(row => row.CorrelationId).HasMaxLength(CorrelationIdLength);
        entity.Property(row => row.Ip).HasMaxLength(IpLength);
        conventions.ConfigureJsonColumn(entity, entity.Property(row => row.Details));

        // No foreign key to the site: the log outlives what it describes, and the retention job removes it by age.
        entity.HasIndex(row => new { row.SiteId, row.OccurredAt })
            .HasDatabaseName("ix_cms_activity_site_occurred");

        entity.HasIndex(row => new { row.TargetType, row.TargetId })
            .HasDatabaseName("ix_cms_activity_target");

        entity.HasIndex(row => row.ActorId)
            .HasDatabaseName("ix_cms_activity_actor");
    }

    private static void MapColumnNames<T>(EntityTypeBuilder<T> entity)
        where T : class
    {
        foreach (var property in typeof(T).GetProperties())
        {
            entity.Property(property.Name).HasColumnName(StorageNames.Column(property.Name));
        }
    }
}
