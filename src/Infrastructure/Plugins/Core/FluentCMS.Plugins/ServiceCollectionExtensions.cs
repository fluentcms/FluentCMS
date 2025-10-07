using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins;

public static class ServiceCollectionExtensions
{
    public static void AddPlugins(this IHostApplicationBuilder builder, string[] pluginPrefixes, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger<PluginManager>();
        var pluginManager = new PluginManager(pluginPrefixes, logger);
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
