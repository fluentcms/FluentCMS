using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace FluentCMS.Api;

public static class SeedData
{
    // TODO: in production we should delete the folder after seeding
    // TODO: check if at least one user if initialized
    // TODO: check this for the file path on deployment
    public static void SeedDefaultData(this IServiceProvider provider, string dataFolder)
    {
        var scope = provider.CreateScope();

        var hostService = scope.ServiceProvider.GetRequiredService<IHostService>();
        var siteService = scope.ServiceProvider.GetRequiredService<ISiteService>();
        var pageService = scope.ServiceProvider.GetRequiredService<IPageService>();
        var appContext = scope.ServiceProvider.GetRequiredService<IApplicationContext>();

        if (!hostService.IsInitialized().GetAwaiter().GetResult())
        {
            // User creation
            var user = LoadData<User>($@"{dataFolder}\user.json");
            user.Id = Guid.Empty;
            // TODO: call service to create user

            // TODO: is this the correct way? We may use proxy objects instead
            appContext.Current = new CurrentContext { User = user, };

            // Host creation
            var host = LoadData<Host>($@"{dataFolder}\host.json");
            hostService.Create(host).GetAwaiter().GetResult();
            appContext.Current.Host = host;

            // Site creation
            var site = LoadData<Site>($@"{dataFolder}\site.json");
            siteService.Create(site).GetAwaiter().GetResult();
            appContext.Current.Site = site;

            // Pages creation: adding a few default pages
            var pages = LoadData<List<Page>>($@"{dataFolder}\pages.json");
            foreach (var page in pages)
            {
                page.SiteId = site.Id;
                pageService.Create(page).GetAwaiter().GetResult();
            }
        }
    }

    private static T LoadData<T>(string jsonFile)
    {
        try
        {
            var json = File.ReadAllText(jsonFile);
            var result = JsonSerializer.Deserialize<T>(json);

            return result == null ?
                throw new Exception($"Unable to load seed data from {jsonFile}") : result;
        }
        catch (Exception)
        {
            throw new Exception($"Unable to load seed data from {jsonFile}");
        }
    }

}
