using FluentCMS.Entities;
using FluentCMS.Entities.Permissions;
using FluentCMS.Repositories.Abstractions;
using FluentCMS.Services;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.Repository.LiteDb.Tests.Entities.PermissionTests;
public class Permission_Tests
{
    private ServiceProvider _serviceProvider;

    public Permission_Tests()
    {
        var services = new ServiceCollection();
        services
            .AddApplicationServices();
        _serviceProvider = services.BuildServiceProvider();
    }
    [Fact]
    public async Task Should_AssignPermission()
    {
        var role = new Role()
        {
            AutoAssigned = true,
            CreatedAt = DateTime.Now,
            CreatedBy = "",
            Id = Guid.NewGuid(),
            LastUpdatedAt = DateTime.Now,
            LastUpdatedBy = "",
            Name = "Name",
            Description = "Description",
            SiteId = Guid.NewGuid(),
        };
        var service = _serviceProvider.GetRequiredService<IPermissionService>();
        await service.AddPermission(PermissionDefinition.Users.Creation, role);
        (await service.CheckPermission(PermissionDefinition.Users.Creation, role)).ShouldBeTrue();

    }
}
