namespace FluentCMS.Plugins;

public static class ServiceCollectionExtensions
{
    public static void AddPlugins(this IHostApplicationBuilder builder, string[] pluginPrefixes)
    {
        var pluginManager = new PluginManager(pluginPrefixes);
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