using FluentCMS.Core.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Storage;

// The schema arrives through UseCmsModel on the options.
public sealed class CmsDbContext : DbContext
{
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

    internal string ModelKey => SchemaModelKey.Of(_model.Schema);

    internal string ConventionsKey => _model.Conventions.CacheKey;

    // The rows of a content table, such as cms_article or cms_article_i18n.
    public DbSet<Dictionary<string, object>> Rows(string table) => Set<Dictionary<string, object>>(table);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        _model.Conventions.ConfigureConventions(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        new SystemModelBuilder(_model.Conventions).Apply(modelBuilder);
        new ContentModelBuilder(_model.Schema, _model.Conventions).Apply(modelBuilder);
    }
}
