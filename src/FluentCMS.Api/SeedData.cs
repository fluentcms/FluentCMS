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
        var roleService = scope.ServiceProvider.GetRequiredService<IRoleService>();
        var appContext = scope.ServiceProvider.GetRequiredService<IApplicationContext>();

        if (!hostService.IsInitialized().GetAwaiter().GetResult())
        {
            var superAdmin = LoadData<DefaultUser>($@"{dataFolder}\superadmin.json");
            var host = LoadData<Host>($@"{dataFolder}\host.json");
            var site = LoadData<Site>($@"{dataFolder}\site.json");
            var admin = LoadData<DefaultUser>($@"{dataFolder}\admin.json");
            var adminRole = LoadData<Role>($@"{dataFolder}\adminrole.json");

            var superUser = new User
            {
                UserName = superAdmin.UserName,
                Email = superAdmin.Email
            };

            var adminUser = new User
            {
                UserName = admin.UserName,
                Email = admin.Email
            };

            appContext.Current = new CurrentContext
            {
                User = superUser,
                Host = host
            };

            // Super Admin creation
            userService.Create(superUser, superAdmin.Password).GetAwaiter().GetResult();

            // Admin Role creation
            roleService.Create(adminRole).GetAwaiter().GetResult();

            // Admin creation
            adminUser.RoleIds = [adminRole.Id];
            userService.Create(adminUser, admin.Password).GetAwaiter().GetResult();

            // Host creation
            hostService.Create(host).GetAwaiter().GetResult();

            // Site creation            
            site.AdminRoleIds = [adminRole.Id];
            siteService.Create(site).GetAwaiter().GetResult();

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

    private class DefaultUser
    {
        public required string UserName { get; set; }
        public required string Email { get; set; }
        public required string Password { get; set; }
    }

}
