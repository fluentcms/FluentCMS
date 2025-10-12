using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Infrastructure.Plugins.Abstractions;

/// <summary>
/// Central interface for all plugins in the FluentCMS plugin system.
/// Plugins must implement this interface and be marked with the [Plugin] attribute.
/// </summary>
public interface IPluginStartup
{
    /// <summary>
    /// Gets the display name of the plugin. Defaults to the assembly name.
    /// </summary>
    public virtual string Name => GetType().Assembly.GetName().Name ?? string.Empty;

    /// <summary>
    /// Gets the version of the plugin. Defaults to the assembly version.
    /// </summary>
    public virtual string Version => GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0";

    /// <summary>
    /// Gets the priority for service registration. Lower values are processed first.
    /// Default value is 1000.
    /// </summary>
    public virtual int ConfigureServicesPriority => 1000;

    /// <summary>
    /// Gets the priority for middleware configuration. Lower values are processed first.
    /// Default value is 1000.
    /// </summary>
    public virtual int ConfigurePriority => 1000;

    /// <summary>
    /// Configures the services for this plugin. This is called during the service registration phase.
    /// The configuration parameter is pre-scoped to this plugin's configuration section.
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    /// <param name="configuration">Plugin-specific configuration, scoped to "Plugins:{PluginName}".</param>
    void ConfigureServices(IServiceCollection services, IConfiguration? configuration);

    /// <summary>
    /// Configures the application pipeline for this plugin. This is called during the middleware configuration phase.
    /// Use this to register middleware, endpoints, or other application features.
    /// </summary>
    /// <param name="app">The application builder to configure.</param>
    void Configure(IApplicationBuilder app);
}
