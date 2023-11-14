using FluentCMS.Entities;
using FluentCMS.Services;
using FluentCMS.Tests.Helpers.ApplicationContext;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.Tests.Services;
public partial class HostService_Tests
{
    private IServiceProvider _serviceProvider;
    public HostService_Tests()
    {
        var services = new ServiceCollection();
        services.AddApplicationServices()
            .AddInMemoryLiteDbRepositories()
            .AddTestApplicationContext();
        _serviceProvider = services.BuildServiceProvider();
    }

    //Create
    [Fact]
    public async Task Should_Create()
    {
        var scope = _serviceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IHostService>();
        var result = await service.IsInitialized();
        scope.SetupMockApplicationContextForSuperUser();
        result.ShouldBeFalse();
        var host = new Host
        {
            SuperUsers = [ApplicationContextDefaults.SuperAdmins.TestSuperAdminUser.UserName!],
        };
        await service.Create(host);
        result = await service.IsInitialized();
        result.ShouldBeTrue();
    }

    //Update
    [Fact]
    public async Task Should_Update()
    {
        var scope = _serviceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        scope.SetupMockApplicationContextForSuperUser();
        var service = scope.ServiceProvider.GetRequiredService<IHostService>();
        var host = new Host
        {
            SuperUsers = [ApplicationContextDefaults.SuperAdmins.TestSuperAdminUser.UserName!]
        };

        await service.Create(host);
        //edit host
        scope.SetupMockApplicationContextForSuperUser(host: host);
        var editedHost = await service.Get();
        editedHost.SuperUsers.Add("test2");
        await service.Update(editedHost);
        var result = await service.Get();
        result.SuperUsers.Count.ShouldBe(2);
    }

    //Limited Access
    [Fact]
    public async Task ShouldNot_Get()
    {
        var scope = _serviceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        scope.SetupMockApplicationContextForSuperUser();
        var service = scope.ServiceProvider.GetRequiredService<IHostService>();
        var host = new Host
        {
            SuperUsers = [ApplicationContextDefaults.SuperAdmins.TestSuperAdminUser.UserName!]
        };

        await service.Create(host);
        scope.SetupMockApplicationContextForNonAdminUser(host: host);
        await service.Get().ShouldThrowAsync<Exception>();
    }
    [Fact]
    public async Task ShouldNot_Update()
    {
        var scope = _serviceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        scope.SetupMockApplicationContextForSuperUser();
        var service = scope.ServiceProvider.GetRequiredService<IHostService>();
        var host = new Host
        {
            SuperUsers = [ApplicationContextDefaults.SuperAdmins.TestSuperAdminUser.UserName!]
        };

        await service.Create(host);
        //edit host
        var editedHost = await service.Get();
        scope.SetupMockApplicationContextForNonAdminUser(host: host);
        editedHost.SuperUsers.Add("test2");
        await service.Update(editedHost).ShouldThrowAsync<Exception>();
    }

}
