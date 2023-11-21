using FluentCMS.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Tests;
public static class TestApplicationContextExtensions
{
    // AddTestApplicationContext
    public static IServiceCollection AddTestApplicationContext(this IServiceCollection services)
    {
        services.AddScoped<IApplicationContext, TestApplicationContext>();
        return services;
    }
}
public class TestApplicationContext : IApplicationContext
{
    public ICurrentContext Current { get; set; }
}
public class CurrentTestContext : ICurrentContext
{
    //public User? User { get; set; }
    public IEnumerable<Guid> RoleIds { get; set; } = [];
    //public required Host Host { get; set; }
    public string UserName { get; set; }
    public bool IsAuthenticated => !string.IsNullOrEmpty(UserName);
    public bool IsSuperAdmin => false; // Host.SuperUsers.Contains(UserName);

    //public required Site Site { get; set; }
    public bool IsSiteAdmin => false; // IsInRole(Site.AdminRoleIds);

    public bool IsInRole(Guid roleId)
    {
        if (IsSuperAdmin)
            return true;

        if (RoleIds == null || !RoleIds.Any())
            return false;

        return RoleIds.Any(x => x == roleId);
    }

    public bool IsInRole(IEnumerable<Guid> roleIds)
    {
        if (IsSuperAdmin)
            return true;

        return roleIds.Any(IsInRole);
    }
}
