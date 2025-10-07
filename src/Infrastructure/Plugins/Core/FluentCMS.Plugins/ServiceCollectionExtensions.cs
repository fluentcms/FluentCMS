using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins;

public static class ServiceCollectionExtensions
{
    public static void AddPlugins(this IHostApplicationBuilder builder, Action<PluginOptions> config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var options = new PluginOptions();
        config(options);

        ArgumentNullException.ThrowIfNull(options.PluginPrefixes);
        ArgumentNullException.ThrowIfNull(options.LoggerFactory);

        var logger = options.LoggerFactory.CreateLogger<PluginManager>();
        var pluginManager = new PluginManager(options.PluginPrefixes, logger);
        builder.Services.AddSingleton<IPluginManager>(pluginManager);
        pluginManager.ConfigureServices(builder);
    }

    public static IApplicationBuilder UsePlugins(this IApplicationBuilder app)
    {
        var pluginLoader = app.ApplicationServices.GetRequiredService<IPluginManager>();
        pluginLoader.Configure(app);
        return app;
    }

}

public class PluginOptions
{
    public string[] PluginPrefixes { get; set; } = [];
    public ILoggerFactory LoggerFactory { get; set; } = default!;
}
