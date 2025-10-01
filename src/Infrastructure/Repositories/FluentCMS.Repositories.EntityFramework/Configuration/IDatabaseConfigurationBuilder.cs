namespace FluentCMS.Repositories.EntityFramework.Configuration;

/// <summary>
/// Builder interface for configuring database options in a fluent manner
/// </summary>
public interface IDatabaseConfigurationBuilder
{
    /// <summary>
    /// Gets the underlying DatabaseConfiguration being built
    /// </summary>
    DatabaseConfiguration Configuration { get; }
}

/// <summary>
/// Default implementation of IDatabaseConfigurationBuilder
/// </summary>
internal class DatabaseConfigurationBuilder : IDatabaseConfigurationBuilder
{
    /// <summary>
    /// The configuration instance being built
    /// </summary>
    public DatabaseConfiguration Configuration { get; internal set; } = new();
}
