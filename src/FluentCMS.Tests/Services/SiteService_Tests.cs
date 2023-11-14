using FluentCMS.Entities;
using FluentCMS.Services;
using FluentCMS.Tests.Helpers.ApplicationContext;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace FluentCMS.Tests.Services;

public class SiteService_Tests
{
    private readonly IServiceProvider _serviceProvider;

    public SiteService_Tests()
    {
        var services = new ServiceCollection();
        // TODO: write tests for LiteDbInMemoryRepository
        services.AddApplicationServices()
            .AddInMemoryLiteDbRepositories()
            .AddTestApplicationContext();
        _serviceProvider = services.BuildServiceProvider();
    }

    //Should Create New Site
    [Fact]
    public async Task Should_Create_New_Site()
    {
        var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISiteService>();
        Site site = new Site
        {
            Name = "test site",
            Description = "test site description",
            Urls = ["test.com"],
            AdminRoleIds = new List<Guid>() { ApplicationContextDefaults.Admins.TestAdminRole.Id }
        };
        scope.SetupMockApplicationContextForSuperUser();
        var createdSite = await service.Create(site);
        var result = await service.GetById(createdSite.Id);
        result.Name.ShouldBe("test site");
        result.Description.ShouldBe("test site description");
        result.Urls.Contains("test.com").ShouldBeTrue();
        //result.RoleId.ShouldBe(roleId);
    }

    [Fact]
    public async Task ShouldNot_Create_New_Site()
    {
        var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISiteService>();
        Site site = new Site
        {
            Name = "test site",
            Description = "test site description",
            Urls = ["test.com"],
            AdminRoleIds = new List<Guid>() { ApplicationContextDefaults.Admins.TestAdminRole.Id }
        };
        scope.SetupMockApplicationContextForNonAdminUser();
        await service.Create(site).ShouldThrowAsync<Exception>();
    }

    //Should Update Site
    [Fact]
    public async Task Should_Update_Site()
    {
        var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISiteService>();
        scope.SetupMockApplicationContextForSuperUser();
        
        Site site = new Site
        {
            Name = "test site",
            Description = "test site description",
            Urls = ["test.com"],
            AdminRoleIds = new List<Guid>() { ApplicationContextDefaults.Admins.TestAdminRole.Id }
        };
        
        var editSite = await service.Create(site);
        var dbSite = await service.GetById(editSite.Id);
        var updatedSite = await service.Update(new Site
        {
            Id = dbSite.Id,
            Name = "test site updated",
            Description = "test site description updated",
            Urls = dbSite.Urls.ToList(),
            AdminRoleIds = new List<Guid>() { ApplicationContextDefaults.Admins.TestAdminRole.Id, Guid.NewGuid() }
        });
        var result = await service.GetById(updatedSite.Id);
        result.Name.ShouldBe("test site updated");
        result.Description.ShouldBe("test site description updated");
        result.Urls.Contains("test.com").ShouldBeTrue();
        result.AdminRoleIds.Count.ShouldBe(2);
    }
    [Fact]
    public async Task ShouldNot_Update_Site()
    {
        var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISiteService>();
        scope.SetupMockApplicationContextForSuperUser();

        Site site = new Site
        {
            Name = "test site",
            Description = "test site description",
            Urls = ["test.com"],
            AdminRoleIds = new List<Guid>() { ApplicationContextDefaults.Admins.TestAdminRole.Id }
        };

        var editSite = await service.Create(site);
        var dbSite = await service.GetById(editSite.Id);
        scope.SetupMockApplicationContextForNonAdminUser();
        await service.Update(new Site
        {
            Id = dbSite.Id,
            Name = "test site updated",
            Description = "test site description updated",
            Urls = dbSite.Urls.ToList(),
            AdminRoleIds = new List<Guid>() { ApplicationContextDefaults.Admins.TestAdminRole.Id, Guid.NewGuid() }
        }).ShouldThrowAsync<Exception>();
    }
    //Should Delete Site
    [Fact]
    public async Task Should_Delete_Site()
    {
        var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISiteService>();
        scope.SetupMockApplicationContextForSuperUser();
        var site = await service.Create(new Site
        {
            Name = "test site",
            Description = "test site description",
            Urls = ["test.com"],
            AdminRoleIds = [ApplicationContextDefaults.Admins.TestAdminRole.Id]
        });
        var result = await service.GetById(site.Id);
        await service.Delete(result);
        var sites = await service.GetAll();
        sites.Count().ShouldBe(0);
    }
    [Fact]
    public async Task ShouldNot_Delete_Site()
    {
        var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISiteService>();
        scope.SetupMockApplicationContextForSuperUser();
        var site = await service.Create(new Site
        {
            Name = "test site",
            Description = "test site description",
            Urls = ["test.com"],
            AdminRoleIds = [ApplicationContextDefaults.Admins.TestAdminRole.Id]
        });
        scope.SetupMockApplicationContextForNonAdminUser();
        var result = await service.GetById(site.Id);
        await service.Delete(result).ShouldThrowAsync<Exception>();
    }
    //Should Get Site
    [Fact]
    public async Task Should_Get_Site()
    {
        var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISiteService>();
        scope.SetupMockApplicationContextForSuperUser();
        var site = await service.Create(new Site
        {
            Name = "test site",
            Description = "test site description",
            Urls = ["test.com"],
            AdminRoleIds = [ApplicationContextDefaults.Admins.TestAdminRole.Id]
        });
        scope.SetupMockApplicationContextForNonAdminUser();
        var result = await service.GetById(site.Id);
        result.Name.ShouldBe("test site");
        result.Description.ShouldBe("test site description");
        result.Urls.Contains("test.com").ShouldBeTrue();
        //result.RoleId.ShouldBe(roleId);
    }

    //Should Get Site By Url
    [Fact]
    public async Task Should_GetByUrl_Site()
    {
        var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISiteService>();
        scope.SetupMockApplicationContextForSuperUser();
        var site = await service.Create(new Site
        {
            Name = "test site",
            Description = "test site description",
            Urls = ["test.com"],
            AdminRoleIds = [ApplicationContextDefaults.Admins.TestAdminRole.Id]
        });
        scope.SetupMockApplicationContextForNonAdminUser();
        var result = await service.GetByUrl("test.com");
        result.Name.ShouldBe("test site");
        result.Description.ShouldBe("test site description");
        result.Urls.Contains("test.com").ShouldBeTrue();
        //result.RoleId.ShouldBe(roleId);
    }

    //Should Get All Sites
    [Fact]
    public async Task Should_GetAll_Site()
    {
        var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISiteService>();
        scope.SetupMockApplicationContextForSuperUser();
        // create 10 sites
        const int count = 10;
        var roleId = Guid.NewGuid();
        for (var i = 1; i <= count; i++)
        {
            await service.Create(new Site
            {
                Name = $"test site {i}",
                Description = $"test site description {i}",
                Urls = [$"site-{i}.com"],
                AdminRoleIds = [ApplicationContextDefaults.Admins.TestAdminRole.Id]
            });
        }
        var result = await service.GetAll();
        result.Count().ShouldBe(count);
        result.ToList().ForEach(x => x.Name.ShouldStartWith("test site"));
        result.ToList().ForEach(x => x.Description.ShouldStartWith("test site description"));
        result.ToList().ForEach(x =>
        {
            x.Urls.First().ShouldStartWith("site-");
            x.Urls.First().ShouldEndWith(".com");
        });
        scope.SetupMockApplicationContextForAdminUser();
        result = await service.GetAll();
        result.Count().ShouldBe(count);
        result.ToList().ForEach(x => x.Name.ShouldStartWith("test site"));
        result.ToList().ForEach(x => x.Description.ShouldStartWith("test site description"));
        result.ToList().ForEach(x =>
        {
            x.Urls.First().ShouldStartWith("site-");
            x.Urls.First().ShouldEndWith(".com");
        });
    }
}
