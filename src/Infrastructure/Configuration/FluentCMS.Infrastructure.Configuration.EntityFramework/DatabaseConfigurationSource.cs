namespace FluentCMS.Infrastructure.Configuration.EntityFramework;

/// <summary>
/// Configuration source for database-backed configurations
/// </summary>
public class DatabaseConfigurationSource : IConfigurationSource
{
    public DbContextOptions<ConfigurationDbContext> DbOptions { get; set; } = null!;
    public TimeSpan ReloadInterval { get; set; } = TimeSpan.Zero;

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new DatabaseConfigurationProvider(DbOptions, ReloadInterval);
    }
}
