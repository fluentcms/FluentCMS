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

    public void Apply(ModelBuilder modelBuilder)
    {
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
        modelBuilder.Entity<Role>().HasData(RoleSeed.Roles());
        modelBuilder.Entity<RoleGrant>().HasData(RoleSeed.Permissions());
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

    private static void MapColumnNames<T>(EntityTypeBuilder<T> entity)
        where T : class
    {
        foreach (var property in typeof(T).GetProperties())
        {
            entity.Property(property.Name).HasColumnName(StorageNames.Column(property.Name));
        }
    }
}
