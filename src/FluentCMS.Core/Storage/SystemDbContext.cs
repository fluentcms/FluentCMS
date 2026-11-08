using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Storage;

// The system migration stream: only what SystemModelBuilder maps, never a content table. Each data provider
// package holds the migrations of this model for its database; docs/system-migrations.md says how to add one.
public sealed class SystemDbContext : DbContext
{
    public const string HistoryTable = "cms__migrations";

    private readonly CmsModelOptionsExtension _model;

    public SystemDbContext(DbContextOptions<SystemDbContext> options)
        : base(options)
    {
        _model = options.FindExtension<CmsModelOptionsExtension>()
            ?? throw new InvalidOperationException($"{nameof(SystemDbContext)} needs conventions; call {nameof(CmsDbContextOptionsExtensions.UseSystemModel)} on its options.");
    }

    internal string ConventionsKey => _model.Conventions.CacheKey;

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        StorageConventions.Configure(_model.Conventions, configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        new SystemModelBuilder(_model.Conventions).Apply(modelBuilder);
        SystemModelBuilder.ApplySeedData(modelBuilder);
    }
}
