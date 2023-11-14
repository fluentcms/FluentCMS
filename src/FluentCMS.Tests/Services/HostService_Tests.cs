using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.Tests.Services;
public class HostService_Tests
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

    private static void SetupMockApplicationContext(IApplicationContext applicationContext, Host host)
    {
        applicationContext.Current = new CurrentTestContext()
        {
            Host = host,
            Site = null,
            User = new User("test"),
            Roles = new() { new Role() { } },
        };
    }
    //Create
    [Fact]
    public async Task Should_Create()
    {
        var scope = _serviceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var applicationContext = scope.ServiceProvider.GetRequiredService<IApplicationContext>();
        var service = scope.ServiceProvider.GetRequiredService<IHostService>();
        var result = await service.IsInitialized();
        result.ShouldBeFalse();
        var host = new Host
        {
            SuperUsers = new List<string> { "test" },
        };
        SetupMockApplicationContext(applicationContext,host);
        await service.Create(host);
        result = await service.IsInitialized();
        result.ShouldBeTrue();
    }

    //Update
    [Fact]
    public async Task Should_Update()
    {
        var scope = _serviceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var applicationContext = scope.ServiceProvider.GetRequiredService<IApplicationContext>();
        var service = scope.ServiceProvider.GetRequiredService<IHostService>();
        var host = new Host
        {
            SuperUsers = new List<string> { "test" },
        };
        SetupMockApplicationContext(applicationContext, host);
        await service.Create(host);
        //edit host
        var editedHost = await service.Get();
        editedHost.SuperUsers.Add("test2");
        await service.Update(editedHost);
        var result = await service.Get();
        result.SuperUsers.Count.ShouldBe(2);

    }
    
}
