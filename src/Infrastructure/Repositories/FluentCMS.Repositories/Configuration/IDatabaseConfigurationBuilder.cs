using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Repositories;

/// <summary>
/// Builder interface for configuring database options in a fluent manner
/// </summary>
public interface IDatabaseConfigurationBuilder
{
    /// <summary>
    /// Gets the underlying DatabaseConfiguration being built
    /// </summary>
    DatabaseConfiguration Configuration { get; }

    IServiceCollection ServiceDescriptors { get; }
}

/// <summary>
/// Default implementation of IDatabaseConfigurationBuilder
/// </summary>
internal class DatabaseConfigurationBuilder(DatabaseConfiguration configuration, IServiceCollection serviceDescriptors) : IDatabaseConfigurationBuilder
{
    /// <summary>
    /// The configuration instance being built
    /// </summary>
    public DatabaseConfiguration Configuration { get; internal set; } = configuration;

    public IServiceCollection ServiceDescriptors { get; internal set; } = serviceDescriptors;
}
