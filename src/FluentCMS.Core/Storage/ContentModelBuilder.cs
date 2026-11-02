using FluentCMS.Core.Schema;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluentCMS.Core.Storage;

// Turns each content type into shared-type (property bag) entity types named by table: cms_{type}, and
// cms_{type}_i18n for a localized type. Property names are the column names.
internal sealed class ContentModelBuilder(SchemaModel schema, IStorageConventions conventions)
{
    private readonly List<SchemaError> _errors = [];

    // System entities must already be mapped, so a type whose table is taken is reported here.
    public void Apply(ModelBuilder modelBuilder)
    {
        var tables = modelBuilder.Model.GetEntityTypes()
            .Select(entity => entity.GetTableName())
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        foreach (var type in schema.ContentTypes)
        {
            var fields = FieldStorage.Of(schema, type).ToList();
            MapTable(modelBuilder, tables, type, StorageNames.Table(type), fields.Where(field => !field.Localized).ToList(), hasLocale: false);

            if (type.Localized)
            {
                MapTable(modelBuilder, tables, type, StorageNames.LocaleTable(type), fields.Where(field => field.Localized).ToList(), hasLocale: true);
            }
        }

        if (_errors.Count > 0)
        {
            throw new SchemaValidationException(_errors);
        }
    }

    private void MapTable(ModelBuilder modelBuilder, HashSet<string> tables, SchemaType type, string table, List<StoredField> fields, bool hasLocale)
    {
        if (!tables.Add(table))
        {
            Report(SchemaErrorCodes.ReservedName, $"{type.Kind.ToString().ToLowerInvariant()} '{type.Name}' needs table {table}, which is already taken");
            return;
        }

        modelBuilder.SharedTypeEntity<Dictionary<string, object>>(table, entity =>
        {
            entity.ToTable(table);
            entity.Property<Guid>(StorageNames.Id).HasValueGenerator<GuidV7ValueGenerator>();
            entity.HasKey(StorageNames.Id);
            entity.Property<Guid>(StorageNames.DocumentId);
            entity.Property(type.Scope == ContentScope.Global ? typeof(Guid?) : typeof(Guid), StorageNames.SiteId);
            entity.Property<short>(StorageNames.State);

            // Column to the field that holds it; a system column has no field.
            Dictionary<string, string?> columns = new(StringComparer.Ordinal)
            {
                [StorageNames.Id] = null,
                [StorageNames.DocumentId] = null,
                [StorageNames.SiteId] = null,
                [StorageNames.State] = null,
            };

            if (hasLocale)
            {
                entity.Property<string>(StorageNames.Locale).HasMaxLength(StorageNames.LocaleLength).IsRequired();
                columns[StorageNames.Locale] = null;
                entity.HasIndex(StorageNames.DocumentId, StorageNames.Locale, StorageNames.State).IsUnique().HasDatabaseName($"uq_{table}_document_locale_state");
            }
            else
            {
                entity.HasIndex(StorageNames.DocumentId, StorageNames.State).IsUnique().HasDatabaseName($"uq_{table}_document_state");
            }

            foreach (var field in fields)
            {
                MapField(entity, type, table, field, columns, hasLocale);
            }
        });
    }

    private void MapField(EntityTypeBuilder entity, SchemaType type, string table, StoredField field, Dictionary<string, string?> columns, bool hasLocale)
    {
        if (field.Kind == StorageKind.Reference)
        {
            // [Indexed] needs nothing: cms_reference already has its index on the target.
            if (field.Field.Unique)
            {
                Report(SchemaErrorCodes.InvalidField, $"field {field.Location} is unique, but a {field.Field.FieldType} field has no column to index");
            }

            return;
        }

        if (columns.TryGetValue(field.Column, out var holder))
        {
            if (holder is null)
            {
                Report(SchemaErrorCodes.ReservedName, $"field {field.Location} maps to column {field.Column} of {table}, which is a system column");
            }
            else
            {
                Report(SchemaErrorCodes.DuplicateField, $"fields {holder} and {field.Location} both map to column {field.Column} of {table}");
            }

            return;
        }

        columns[field.Column] = field.Location;

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

        if (field.Kind == StorageKind.Json)
        {
            conventions.ConfigureJsonColumn(entity, property);
        }

        MapIndexes(entity, type, table, field, hasLocale);
    }

    private void MapIndexes(EntityTypeBuilder entity, SchemaType type, string table, StoredField field, bool hasLocale)
    {
        // A null site_id would defeat a unique index and tells an ordinary one nothing.
        string[] scope = [.. type.Scope == ContentScope.Global ? [] : new[] { StorageNames.SiteId }, .. hasLocale ? new[] { StorageNames.Locale } : []];

        if (field.Field.Unique)
        {
            if (field.Kind == StorageKind.Json)
            {
                Report(SchemaErrorCodes.InvalidField, $"field {field.Location} is unique, but a {field.Field.FieldType} field is stored as JSON");
                return;
            }

            entity.HasIndex([.. scope, field.Column])
                .IsUnique()
                .HasFilter(StorageNames.PublishedOnly)
                .HasDatabaseName($"ux_{table}_{field.Column}");
        }

        if (field.Field.Indexed)
        {
            if (field.Kind == StorageKind.Json)
            {
                conventions.ConfigureJsonIndex(entity, field.Column);
            }
            else
            {
                entity.HasIndex([.. scope, StorageNames.State, field.Column]).HasDatabaseName($"ix_{table}_{field.Column}");
            }
        }
    }

    private void Report(string code, string message) => _errors.Add(new SchemaError(code, message));
}
