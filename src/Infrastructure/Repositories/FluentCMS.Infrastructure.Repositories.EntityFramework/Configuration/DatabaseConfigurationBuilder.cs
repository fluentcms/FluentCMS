namespace FluentCMS.Infrastructure.Repositories.EntityFramework.Configuration;

/// <summary>
/// Default implementation of IDatabaseConfigurationBuilder
/// </summary>
public class DatabaseConfigurationBuilder(DatabaseConfiguration configuration, IServiceCollection serviceDescriptors)
{
    /// <summary>
    /// The configuration instance being built
    /// </summary>
    public DatabaseConfiguration Configuration { get; internal set; } = configuration;

    public IServiceCollection ServiceDescriptors { get; internal set; } = serviceDescriptors;
}
