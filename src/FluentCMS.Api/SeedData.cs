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
        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
        var appContext = scope.ServiceProvider.GetRequiredService<IApplicationContext>();

        if (!hostService.IsInitialized().GetAwaiter().GetResult())
        {
            // User creation
            var defaultUser = LoadData<DefaultUser>($@"{dataFolder}\user.json");
            var user = new User
            {
                UserName = defaultUser.UserName,
                Email = defaultUser.Email
            };
            user = userService.Create(user, defaultUser.Password).GetAwaiter().GetResult();

            // Host creation
            var host = LoadData<Host>($@"{dataFolder}\host.json");

            appContext.Current = new CurrentContext { User = user, Host = host };

            hostService.Create(host).GetAwaiter().GetResult();

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

    private static JsonSerializerOptions serializerOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

    private static T LoadData<T>(string jsonFile)
    {
        try
        {
            var json = File.ReadAllText(jsonFile);
            var result = JsonSerializer.Deserialize<T>(json, serializerOptions);

            return result == null ?
                throw new Exception($"Unable to load seed data from {jsonFile}") : result;
        }
        catch (Exception)
        {
            throw new Exception($"Unable to load seed data from {jsonFile}");
        }
    }

    private class DefaultUser : User
    {
        public required string Password { get; set; }
    }

}
