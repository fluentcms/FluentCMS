using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluentCMS.Core.Storage;

// Turns each content type into shared-type (property bag) entity types named by table: cms_{type}, and
// cms_{type}_i18n for a localized type. Property names are the column names.
internal sealed class ContentModelBuilder(SchemaModel schema, IStorageConventions conventions)
{
    // The schema was validated when it was built, so names and columns cannot clash here.
    public void Apply(ModelBuilder modelBuilder)
    {
        foreach (var type in schema.ContentTypes)
        {
            var fields = FieldStorage.Of(schema, type).ToList();
            MapTable(modelBuilder, type, StorageNames.Table(type), fields.Where(field => !field.Localized).ToList(), hasLocale: false);

            if (type.Localized)
            {
                MapTable(modelBuilder, type, StorageNames.LocaleTable(type), fields.Where(field => field.Localized).ToList(), hasLocale: true);
            }
        }
    }

    private void MapTable(ModelBuilder modelBuilder, SchemaType type, string table, List<StoredField> fields, bool hasLocale)
    {
        modelBuilder.SharedTypeEntity<Dictionary<string, object>>(table, entity =>
        {
            entity.ToTable(table);
            entity.Property<Guid>(StorageNames.Id).HasValueGenerator<GuidV7ValueGenerator>();
            entity.HasKey(StorageNames.Id);
            entity.Property<Guid>(StorageNames.DocumentId);
            entity.Property(type.Scope == ContentScope.Global ? typeof(Guid?) : typeof(Guid), StorageNames.SiteId);
            entity.Property<short>(StorageNames.State);

            string[] scopeAndState = [.. Scope(type, hasLocale), StorageNames.State];
            if (hasLocale)
            {
                entity.Property<string>(StorageNames.Locale).HasMaxLength(StorageNames.LocaleLength).IsRequired();
                entity.HasIndex(StorageNames.DocumentId, StorageNames.Locale, StorageNames.State).IsUnique().HasDatabaseName($"uq_{table}_document_locale_state");
            }
            else
            {
                entity.HasIndex(StorageNames.DocumentId, StorageNames.State).IsUnique().HasDatabaseName($"uq_{table}_document_state");

                // Every entry has one shared row per state, whatever its locales, so this holds a singleton to one entry
                // per site. A trashed one keeps its rows and so its place, as the check before the insert counts it.
                if (type.Kind == SchemaTypeKind.Singleton)
                {
                    entity.HasIndex(scopeAndState).IsUnique().HasDatabaseName($"uq_{table}_singleton");
                }
            }

            // Without it a list that filters on no [Indexed] field reads cms_document of every type in id order. The
            // singleton key above already covers these columns, and state is a system column, so no field index takes the name.
            if (hasLocale || type.Kind != SchemaTypeKind.Singleton)
            {
                entity.HasIndex(scopeAndState).HasDatabaseName($"ix_{table}_state");
            }

            foreach (var field in fields)
            {
                MapField(entity, type, table, field, hasLocale);
            }

            FitNames(entity);
        });
    }

    // Covers the names the provider conventions gave too. A name that already fits stays as it is.
    private static void FitNames(EntityTypeBuilder entity)
    {
        foreach (var index in entity.Metadata.GetIndexes())
        {
            if (index.GetDatabaseName() is { Length: > StorageNames.MaxIdentifierLength } name)
            {
                index.SetDatabaseName(StorageNames.Identifier(name));
            }
        }

        foreach (var check in entity.Metadata.GetCheckConstraints())
        {
            if (check.Name is { Length: > StorageNames.MaxIdentifierLength } name)
            {
                check.Name = StorageNames.Identifier(name);
            }
        }
    }

    private void MapField(EntityTypeBuilder entity, SchemaType type, string table, StoredField field, bool hasLocale)
    {
        // [Indexed] on a reference needs nothing: cms_reference already has its index on the target.
        if (field.Kind == FieldStorageKind.Reference)
        {
            return;
        }

        var property = entity.Property(field.ClrType!, field.Column);
        if (field.ClrType == typeof(string))
        {
            property.IsRequired(field.Required);
        }

        if (field.MaxLength is { } maxLength)
        {
            property.HasMaxLength(maxLength);
        }

        if (field.DefaultValue is { } defaultValue)
        {
            property.HasDefaultValue(defaultValue);
        }

        if (field.Precision is { } precision)
        {
            property.HasPrecision(precision, field.Scale ?? 0);
        }

        if (field.Kind == FieldStorageKind.Json)
        {
            conventions.ConfigureJsonColumn(entity, property);
        }
        else if (field.ClrType == typeof(string))
        {
            conventions.ConfigureTextColumn(entity, property);
        }

        MapIndexes(entity, type, table, field, property.Metadata.IsNullable, hasLocale);
    }

    // A null site_id would defeat a unique index and tells an ordinary one nothing.
    private static string[] Scope(SchemaType type, bool hasLocale) =>
        [.. type.Scope == ContentScope.Global ? [] : new[] { StorageNames.SiteId }, .. hasLocale ? new[] { StorageNames.Locale } : []];

    private void MapIndexes(EntityTypeBuilder entity, SchemaType type, string table, StoredField field, bool nullable, bool hasLocale)
    {
        var scope = Scope(type, hasLocale);

        // SQL Server holds NULLs equal in a unique index, and EF adds its own IS NOT NULL only to an index with no filter.
        if (field.Field.Unique)
        {
            entity.HasIndex([.. scope, field.Column])
                .IsUnique()
                .HasFilter(nullable ? $"{StorageNames.PublishedOnly} AND \"{field.Column}\" IS NOT NULL" : StorageNames.PublishedOnly)
                .HasDatabaseName($"ux_{table}_{field.Column}");
        }

        if (field.Field.Indexed)
        {
            if (field.Kind == FieldStorageKind.Json)
            {
                conventions.ConfigureJsonIndex(entity, field.Column);
            }
            else
            {
                entity.HasIndex([.. scope, StorageNames.State, field.Column]).HasDatabaseName($"ix_{table}_{field.Column}");
            }
        }
    }
}
