namespace FluentCMS.Infrastructure.Plugins;

public static class PluginSystemExtensions
{
    public static IServiceCollection AddPluginSystem(this IServiceCollection services, IConfiguration configuration, Action<PluginSystemOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new PluginSystemOptions();
        configureOptions?.Invoke(options);

        var pluginManager = new PluginManager(
            new PluginDiscovery(options.CreateLogger<PluginDiscovery>(), options),
            new PluginInitializer(options.CreateLogger<PluginInitializer>(), options),
            new PluginLoader(options.CreateLogger<PluginLoader>(), options),
            options.CreateLogger<PluginManager>(),
            options
        );

        pluginManager.Configure(services, configuration, CancellationToken.None);

        services.AddSingleton<IPluginManager>(pluginManager);

        return services;
    }

    private static ILogger<T> CreateLogger<T>(this PluginSystemOptions options)
    {
        return options.LoggerFactory.CreateLogger<T>();
    }

    public static IApplicationBuilder UsePluginSystem(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var pluginManager = app.ApplicationServices.GetRequiredService<IPluginManager>();
        pluginManager.Start(app, CancellationToken.None);

        return app;
    }
}
