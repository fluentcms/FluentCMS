using FluentCMS.Entities;
using FluentCMS.Repositories.MongoDB;
using FluentCMS.Services;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using System.Text.Json;

namespace FluentCMS.Api;

/// <summary>
/// Extension methods for initializing and resetting the MongoDB database and loading default data for the FluentCMS application.
/// </summary>
public static class DefaultDataLoaderExtensions
{
    /// <summary>
    /// Resets the MongoDB database, dropping all collections.
    /// </summary>
    /// <param name="provider">The service provider used to access MongoDB context.</param>
    public static void ResetMongoDb(this IServiceProvider provider)
    {
        var scope = provider.CreateScope();

        var mongoDb = scope.ServiceProvider.GetRequiredService<IMongoDBContext>();

        foreach (var collectionName in mongoDb.Database.ListCollectionNames().ToList())
        {
            mongoDb.Database.DropCollection(collectionName);
        }
    }

    /// <summary>
    /// Loads initial data from JSON files located in the specified data folder.
    /// </summary>
    /// <param name="provider">The service provider used to access various services.</param>
    /// <param name="dataFolder">The folder path containing the JSON seed data files.</param>
    /// <remarks>
    /// This method initializes the database with predefined data, including users, sites, and layouts.
    /// </remarks>
    public static void LoadInitialDataFrom(this IServiceProvider provider, string dataFolder)
    {
        var scope = provider.CreateScope();

        var hostService = scope.ServiceProvider.GetRequiredService<IHostService>();
        var siteService = scope.ServiceProvider.GetRequiredService<ISiteService>();
        var pageService = scope.ServiceProvider.GetRequiredService<IPageService>();
        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
        var pluginDefinitionService = scope.ServiceProvider.GetRequiredService<IPluginDefinitionService>();
        var pluginService = scope.ServiceProvider.GetRequiredService<IPluginService>();
        var layoutService = scope.ServiceProvider.GetRequiredService<ILayoutService>();
        var appContext = scope.ServiceProvider.GetRequiredService<IApplicationContext>();

        if (!hostService.IsInitialized().GetAwaiter().GetResult())
        {
            var defaultData = LoadData<DefaultData>($@"{dataFolder}/default.json");

            var superUser = new User
            {
                UserName = defaultData.SuperAdmin.Username,
                Email = defaultData.SuperAdmin.Email
            };

            // TODO: This is a hack to get the super admin user into the context.
            ((ApiApplicationContext)appContext).SetSuperAdmin(superUser.UserName);

            // Default users creation
            userService.Create(superUser, defaultData.SuperAdmin.Password).GetAwaiter().GetResult();

            // Host creation
            hostService.Create(defaultData.Host).GetAwaiter().GetResult();

            // Site creation
            defaultData.SetLayouts(GetLayouts(dataFolder));
            var panelLayout = defaultData.GetLayout("PanelLayout");

            var site = siteService.Create(defaultData.Site.GetSite(), panelLayout).GetAwaiter().GetResult();

            defaultData.SetSite(site);

            // there is no need to create the default layout, it is created by the site creation
            var authLayout = defaultData.GetLayout("AuthLayout");
            layoutService.Create(authLayout).GetAwaiter().GetResult();

            // Plugin definition creation
            foreach (var pluginDefinition in defaultData.PluginDefinitions)
            {
                pluginDefinitionService.Create(pluginDefinition).GetAwaiter().GetResult();
            }

            // Pages creation: adding a few default pages
            var _pages = new List<Page>();
            foreach (var page in defaultData.GetPages())
            {
                _pages.Add(page);
                pageService.Create(page).GetAwaiter().GetResult();
            }

            // Plugins creation: adding a few default plugins to pages
            var _plugins = new List<Plugin>();
            for (int i = 0; i < _pages.Count; i++)
            {
                var _page = _pages[i];
                var defaultPage = defaultData.Pages[i];
                foreach (var defaultPlugin in defaultPage.Plugins)
                {
                    var _plugin = new Plugin
                    {
                        Order = defaultPlugin.Order,
                        PageId = _page.Id,
                        Section = defaultPlugin.Section,
                        DefinitionId = defaultData.PluginDefinitions.Single(x => x.Name == defaultPlugin.DefName).Id
                    };
                    _plugins.Add(_plugin);
                    pluginService.Create(_plugin).GetAwaiter().GetResult();
                }
            }
        }
    }

    private static readonly JsonSerializerOptions _serializerOptions = new() { PropertyNameCaseInsensitive = true };

    private static T LoadData<T>(string jsonFile)
    {
        try
        {
            var json = File.ReadAllText(jsonFile);
            var result = JsonSerializer.Deserialize<T>(json, _serializerOptions);

            return result == null ?
                throw new Exception($"Unable to load seed data from {jsonFile}") : result;
        }
        catch (Exception)
        {
            throw new Exception($"Unable to load seed data from {jsonFile}");
        }
    }

    private static IEnumerable<Layout> GetLayouts(string dataFolder)
    {
        foreach (var file in Directory.GetFiles(dataFolder, "*.html").Where(x => !x.Contains(".head.html") && !x.Contains(".body.html")))
        {
            var layout = new Layout
            {
                Name = Path.GetFileNameWithoutExtension(file)
            };

            var bodyFile = Path.ChangeExtension(file, ".body.html");
            if (File.Exists(bodyFile))
                layout.Body = File.ReadAllText(bodyFile);

            var headerFile = Path.ChangeExtension(file, ".head.html");
            if (File.Exists(headerFile))
                layout.Head = File.ReadAllText(headerFile);

            yield return layout;
        }
    }
}
