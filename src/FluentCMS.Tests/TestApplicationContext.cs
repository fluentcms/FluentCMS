using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
    public User? User { get; set; }
    public List<Guid> RoleIds { get; set; } = [];
    public required Host Host { get; set; }
    public string UserName => User?.UserName ?? string.Empty;
    public bool IsAuthenticated => !string.IsNullOrEmpty(UserName);
    public bool IsSuperAdmin => Host.SuperUsers.Contains(UserName);

    public required Site Site { get; set; }
    public bool IsSiteAdmin => IsInRole(Site.AdminRoleIds);

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
