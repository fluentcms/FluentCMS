using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace FluentCMS.Storage.Spike.Tests;

public enum SpikeTables
{
    All,
    System,
    Content,
}

public sealed class JsonEachRow
{
    [Column("value")]
    public string? Value { get; set; }
}

public sealed class SpikeDbContext(DbContextOptions<SpikeDbContext> options, SpikeSchema schema, SpikeTables stream = SpikeTables.All)
    : DbContext(options)
{
    public const string Document = "cms_document";
    public const string Reference = "cms_reference";

    public string ModelKey => $"{stream}|{schema.Key}";

    public DbSet<Dictionary<string, object>> Rows(string table) => Set<Dictionary<string, object>>(table);

    public IQueryable<JsonEachRow> JsonEach(string? json) => FromExpression(() => JsonEach(json));

    public static string? JsonText(string? json, string path) => throw new NotSupportedException();

    public static long? JsonInteger(string? json, string path) => throw new NotSupportedException();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        if (schema.UtcTicksDates)
        {
            configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcTicksConverter>();
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<JsonEachRow>().HasNoKey().ToTable((string?)null);
        modelBuilder.HasDbFunction(typeof(SpikeDbContext).GetMethod(nameof(JsonEach))!).HasName("json_each").IsBuiltIn();
        modelBuilder.HasDbFunction(typeof(SpikeDbContext).GetMethod(nameof(JsonText))!).HasName("json_extract").IsBuiltIn();
        modelBuilder.HasDbFunction(typeof(SpikeDbContext).GetMethod(nameof(JsonInteger))!).HasName("json_extract").IsBuiltIn();

        foreach (var type in schema.Types)
        {
            modelBuilder.SharedTypeEntity<Dictionary<string, object>>(type.Table, b =>
            {
                MapTable(b, type, type.Table, type.Fields.Where(f => !f.Localized), stream == SpikeTables.System);
                b.HasIndex("document_id", "state").IsUnique();
                MapUniqueIndexes(b, type.Table, type.Fields.Where(f => !f.Localized), type.Global ? [] : ["site_id"]);
            });

            modelBuilder.SharedTypeEntity<Dictionary<string, object>>(type.LocaleTable, b =>
            {
                MapTable(b, type, type.LocaleTable, type.Fields.Where(f => f.Localized), stream == SpikeTables.System);
                b.Property<string>("locale").HasMaxLength(16);
                b.HasIndex("document_id", "locale", "state").IsUnique();
                MapUniqueIndexes(b, type.LocaleTable, type.Fields.Where(f => f.Localized), type.Global ? ["locale"] : ["site_id", "locale"]);
            });
        }

        modelBuilder.SharedTypeEntity<Dictionary<string, object>>(Document, b =>
        {
            b.ToTable(Document, t => ExcludeIf(t, stream == SpikeTables.Content));
            b.Property<Guid>("id").HasValueGenerator<GuidV7ValueGenerator>();
            b.HasKey("id");
            b.Property<Guid>("document_id");
            b.Property<string>("locale").HasMaxLength(16);
            b.Property<string>("type").HasMaxLength(64);
            b.Property<Guid?>("site_id");
            b.Property<string>("status").HasMaxLength(16);
            b.Property<int>("draft_version");
            b.Property<int?>("published_version");
            b.Property<DateTimeOffset?>("published_at");
            b.Property<DateTimeOffset?>("first_published_at");
            b.Property<DateTimeOffset?>("deleted_at");
            b.Property<string?>("deleted_by");
            b.Property<DateTimeOffset>("created_at");
            b.Property<DateTimeOffset>("updated_at");
            b.Property<DateTimeOffset?>("schedule_publish_at");
            b.Property<DateTimeOffset?>("schedule_unpublish_at");
            b.HasIndex("document_id", "locale").IsUnique();
        });

        modelBuilder.SharedTypeEntity<Dictionary<string, object>>(Reference, b =>
        {
            b.ToTable(Reference, t => ExcludeIf(t, stream == SpikeTables.Content));
            b.Property<Guid>("id").HasValueGenerator<GuidV7ValueGenerator>();
            b.HasKey("id");
            b.Property<string>("source_type").HasMaxLength(64);
            b.Property<Guid>("source_document_id");
            b.Property<int>("state");
            b.Property<string>("locale").HasMaxLength(16);
            b.Property<string>("field").HasMaxLength(64);
            b.Property<int>("position");
            b.Property<string>("target_type").HasMaxLength(64);
            b.Property<Guid>("target_id");
            b.HasIndex("source_type", "source_document_id", "state", "locale", "field", "position").IsUnique();
            b.HasIndex("target_type", "target_id");
        });
    }

    private static void MapTable(EntityTypeBuilder b, SpikeContentType type, string table, IEnumerable<SpikeField> fields, bool excluded)
    {
        var mapped = fields.ToList();
        b.ToTable(table, t =>
        {
            ExcludeIf(t, excluded);
            foreach (var field in mapped.Where(f => f.Json))
            {
                t.HasCheckConstraint($"ck_{table}_{field.Column}_json", $"json_valid({field.Column})");
            }
        });

        b.Property<Guid>("id").HasValueGenerator<GuidV7ValueGenerator>();
        b.HasKey("id");
        b.Property<Guid>("document_id");
        b.Property<int>("state");
        b.Property(type.Global ? typeof(Guid?) : typeof(Guid), "site_id");

        foreach (var field in mapped)
        {
            var property = b.Property(field.ClrType, field.Column).IsRequired(field.Required);
            if (field.MaxLength is { } maxLength)
            {
                property.HasMaxLength(maxLength);
            }

            if (field.Converter is { } converter)
            {
                property.HasConversion(converter);
            }
        }
    }

    private static void MapUniqueIndexes(EntityTypeBuilder b, string table, IEnumerable<SpikeField> fields, string[] scope)
    {
        foreach (var field in fields.Where(f => f.Unique))
        {
            b.HasIndex([.. scope, field.Column])
                .IsUnique()
                .HasFilter("state = 1")
                .HasDatabaseName($"ux_{table}_{field.Column}");
        }
    }

    private static void ExcludeIf(TableBuilder table, bool excluded)
    {
        if (excluded)
        {
            table.ExcludeFromMigrations();
        }
    }
}

public sealed class GuidV7ValueGenerator : ValueGenerator<Guid>
{
    public override bool GeneratesTemporaryValues => false;

    public override Guid Next(EntityEntry entry) => Guid.CreateVersion7();
}

// DateTimeOffset cannot be compared or sorted by the SQLite provider, so it is stored as UTC ticks.
public sealed class UtcTicksConverter() : ValueConverter<DateTimeOffset, long>(
    value => value.UtcTicks,
    value => new DateTimeOffset(value, TimeSpan.Zero));


public sealed class SpikeModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        (context.GetType(), (context as SpikeDbContext)?.ModelKey, designTime);
}

// Used only by `dotnet ef`, to check that the CLI path works against a runtime-built model.
public sealed class SpikeDesignTimeFactory : IDesignTimeDbContextFactory<SpikeDbContext>
{
    public SpikeDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SpikeDbContext>()
            .UseSqlite(new SqliteConnection("Data Source=:memory:"))
            .ReplaceService<IModelCacheKeyFactory, SpikeModelCacheKeyFactory>()
            .Options;

        return new SpikeDbContext(options, SpikeSchema.Article);
    }
}
