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
    public List<Role> Roles { get; set; } = [];
    public required Host Host { get; set; }
    public required Site Site { get; set; }
    public string UserName => User?.UserName ?? string.Empty;
    public bool IsAuthenticated => !string.IsNullOrEmpty(UserName);
    public bool IsSuperAdmin => Host.SuperUsers.Contains(UserName);

    public bool IsInRole(Guid roleId)
    {
        if (IsSuperAdmin)
            return true;

        if (Roles == null || !Roles.Any())
            return false;

        return Roles.Any(x => x.Id == roleId);
    }

    public bool IsInRole(IEnumerable<Guid> roleIds)
    {
        if (IsSuperAdmin)
            return true;

        return roleIds.Any(IsInRole);
    }
}
