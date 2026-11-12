using FluentCMS.Core.Schema;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Storage;

// The schema arrives through UseCmsModel on the options.
public sealed class CmsDbContext : DbContext
{
    public const string HistoryTable = "cms_content__migrations";

    // The snapshot of the schema on the content model, so that each migration's target model and the model snapshot carry
    // the schema they were generated from, which `migrations add` compares the next schema with. No migration operation
    // follows from a model annotation.
    internal const string SchemaAnnotation = "FluentCMS:Schema";

    private readonly CmsModelOptionsExtension _model;

    public CmsDbContext(DbContextOptions<CmsDbContext> options)
        : base(options)
    {
        _model = options.FindExtension<CmsModelOptionsExtension>()
            ?? throw new InvalidOperationException($"{nameof(CmsDbContext)} needs a schema; call {nameof(CmsDbContextOptionsExtensions.UseCmsModel)} on its options.");
    }

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<Reference> References => Set<Reference>();

    public DbSet<ContentVersion> Versions => Set<ContentVersion>();

    internal SchemaModel Schema => _model.Schema;

    internal string ModelKey => SchemaSnapshot.Of(_model.Schema).Hash;

    internal string ConventionsKey => _model.Conventions.CacheKey;

    internal bool ReordersJson => _model.Conventions.ReordersJson;

    internal string? JsonDialectKey => _model.JsonDialect?.GetType().FullName;

    internal string PluginModelKey => _model.Plugins.Key;

    // The rows of a content table, such as cms_article or cms_article_i18n.
    public DbSet<Dictionary<string, object>> Rows(string table) => Set<Dictionary<string, object>>(table);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        StorageConventions.Configure(_model.Conventions, configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        new SystemModelBuilder(_model.Conventions).Apply(modelBuilder);
        foreach (var contributor in _model.Plugins.Contributors)
        {
            contributor.Configure(modelBuilder);
        }

        // What is mapped before the content tables belongs to another stream: the system tables and the plugins' tables.
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            entity.SetIsTableExcludedFromMigrations(true);
        }

        new ContentModelBuilder(_model.Schema, _model.Conventions).Apply(modelBuilder);
        modelBuilder.HasAnnotation(SchemaAnnotation, SchemaSnapshot.Of(_model.Schema).Json);
        _model.JsonDialect?.ConfigureFunctions(modelBuilder);
    }
}
