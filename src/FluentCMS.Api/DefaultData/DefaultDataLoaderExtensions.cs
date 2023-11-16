using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace FluentCMS.Api;

public static class DefaultDataLoaderExtensions
{
    public static void LoadInitialDataFrom(this IServiceProvider provider, string dataFolder)
    {
        var scope = provider.CreateScope();

        var hostService = scope.ServiceProvider.GetRequiredService<IHostService>();
        var siteService = scope.ServiceProvider.GetRequiredService<ISiteService>();
        var pageService = scope.ServiceProvider.GetRequiredService<IPageService>();
        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
        var roleService = scope.ServiceProvider.GetRequiredService<IRoleService>();
        var appContext = scope.ServiceProvider.GetRequiredService<IApplicationContext>();

        if (!hostService.IsInitialized().GetAwaiter().GetResult())
        {
            var defaultData = LoadData<DefaultData>($@"{dataFolder}\default.json");

            var superUser = new User
            {
                UserName = defaultData.SuperAdmin.UserName,
                Email = defaultData.SuperAdmin.Email
            };

            var adminUser = new User
            {
                UserName = defaultData.Admin.UserName,
                Email = defaultData.Admin.Email
            };

            appContext.Current = new CurrentContext
            {
                User = superUser,
                Host = defaultData.Host
            };

            // Default users creation
            userService.Create(superUser, defaultData.SuperAdmin.Password).GetAwaiter().GetResult();
            userService.Create(adminUser, defaultData.Admin.Password).GetAwaiter().GetResult();

            // Host creation
            hostService.Create(defaultData.Host).GetAwaiter().GetResult();

            // Site creation            
            siteService.Create(defaultData.Site).GetAwaiter().GetResult();

            // Admin Role creation
            defaultData.AdminRole.SiteId = defaultData.Site.Id;
            roleService.Create(defaultData.AdminRole).GetAwaiter().GetResult();

            // Add admin role to admin user 
            adminUser.RoleIds = [defaultData.AdminRole.Id];
            userService.Update(adminUser).GetAwaiter().GetResult();

            // Updating site with admin role
            defaultData.Site.AdminRoleIds = [defaultData.AdminRole.Id];
            siteService.Update(defaultData.Site).GetAwaiter().GetResult();

            // Pages creation: adding a few default pages
            foreach (var page in defaultData.Pages)
            {
                page.SiteId = defaultData.Site.Id;
                pageService.Create(page).GetAwaiter().GetResult();
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
}
