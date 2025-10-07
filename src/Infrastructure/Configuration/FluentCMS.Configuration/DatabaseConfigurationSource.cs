using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace FluentCMS.Configuration;

/// <summary>
/// Configuration source for database-backed configurations
/// </summary>
public class DatabaseConfigurationSource : IConfigurationSource
{
    public DbContextOptions<ConfigurationDbContext> DbOptions { get; set; } = null!;
    public TimeSpan ReloadInterval { get; set; } = TimeSpan.Zero;
    public IConfiguration? SeedConfiguration { get; set; }
    public List<string> DynamicSections { get; set; } = [];

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new DatabaseConfigurationProvider(DbOptions, ReloadInterval, SeedConfiguration, DynamicSections);
    }
}
