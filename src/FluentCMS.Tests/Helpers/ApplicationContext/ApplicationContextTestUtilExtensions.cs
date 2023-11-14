using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Tests.Helpers.ApplicationContext;

public static partial class ApplicationContextTestUtilExtensions
{
    public static void SetupMockApplicationContext(this IServiceScope scope, Host? host = null, Site? site = null, User? user = null, IEnumerable<Role>? roles = null)
    {

        var applicationContext = scope.ServiceProvider.GetRequiredService<IApplicationContext>();
        applicationContext.Current = new CurrentTestContext()
        {
            Host = host ?? ApplicationContextDefaults.DefaultHost,
            Site = site ?? ApplicationContextDefaults.DefaultSite,
            User = user ?? ApplicationContextDefaults.NonAdmins.TestUser,
            Roles = (roles ?? new List<Role>() { ApplicationContextDefaults.NonAdmins.TestRole }).ToList(),
        };
    }
    public static void SetupMockApplicationContextForSuperUser(this IServiceScope scope, Host? host = null, Site? site = null)
    {

        scope.SetupMockApplicationContext(host, site, ApplicationContextDefaults.SuperAdmins.TestSuperAdminUser, [ApplicationContextDefaults.SuperAdmins.TestSuperAdminRole]);
    }
    public static void SetupMockApplicationContextForAdminUser(this IServiceScope scope, Host? host = null, Site? site = null)
    {

        scope.SetupMockApplicationContext(host, site, ApplicationContextDefaults.Admins.TestAdminUser, [ApplicationContextDefaults.Admins.TestAdminRole]);
    }
    public static void SetupMockApplicationContextForNonAdminUser(this IServiceScope scope, Host? host = null, Site? site = null)
    {

        scope.SetupMockApplicationContext(host, site, ApplicationContextDefaults.NonAdmins.TestUser, [ApplicationContextDefaults.NonAdmins.TestRole]);
    }
}

