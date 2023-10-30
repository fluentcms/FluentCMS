using FluentCMS.Entities.Sites;
using FluentCMS.Services;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.Repository.LiteDb.Tests.Entities.Sites;
public class SiteService_Tests
{
    private readonly IServiceProvider _serviceProvider;
    public SiteService_Tests()
    {
        var services = new ServiceCollection();
        services.AddFluentCMSCore()
                .AddLiteDbRepository(x => x.UseInMemory());
        _serviceProvider = services.BuildServiceProvider();
    }
    //Should Create New Site
    [Fact]
    public async Task Should_Create_New_Site()
    {
        var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<SiteService>();
        var siteId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var site = new Site(siteId, "test site", "test site description", ["test.com"], roleId);
        await service.Create(site);
        var result = await service.GetById(siteId);
        result.Name.ShouldBe("test site");
        result.Description.ShouldBe("test site description");
        result.Urls.Contains("test.com").ShouldBeTrue();
        result.RoleId.ShouldBe(roleId);
    }
    //Should Update Site
    [Fact]
    public async Task Should_Update_Site()
    {
        var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<SiteService>();
        var siteId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var site = new Site(siteId, "test site", "test site description", ["test.com"], roleId);
        await service.Create(site);
        var dbSite = await service.GetById(siteId);
        dbSite.SetName("test site edited");
        dbSite.SetDescription("test site description edited");
        dbSite.AddUrl("testEdited.com");
        dbSite.SetRoleId(roleId);
        await service.Update(dbSite);
        var result = await service.GetById(siteId);
        result.Name.ShouldBe("test site edited");
        result.Description.ShouldBe("test site description edited");
        result.Urls.Contains("test.com").ShouldBeTrue();
        result.RoleId.ShouldBe(roleId);
    }
    //Should Delete Site
    [Fact]
    public async Task Should_Delete_Site()
    {
        var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<SiteService>();
        var siteId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var site = new Site(siteId, "test site", "test site description", ["test.com"], roleId);
        await service.Create(site);
        await service.Delete(siteId);
        var result = await service.GetAll();
        result.Count().ShouldBe(0);
    }
    //Should Get Site
    [Fact]
    public async Task Should_Get_Site()
    {
        var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<SiteService>();
        var siteId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var site = new Site(siteId, "test site", "test site description", ["test.com"], roleId);
        await service.Create(site);
        var result = await service.GetById(siteId);
        result.Name.ShouldBe("test site");
        result.Description.ShouldBe("test site description");
        result.Urls.Contains("test.com").ShouldBeTrue();
        result.RoleId.ShouldBe(roleId);
    }
    //Should Get Site By Url
    [Fact]
    public async Task Should_GetByUrl_Site()
    {
        var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<SiteService>();
        var siteId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var site = new Site(siteId, "test site", "test site description", ["test.com"], roleId);
        await service.Create(site);
        var result = await service.GetByUrl("test.com");
        result.Name.ShouldBe("test site");
        result.Description.ShouldBe("test site description");
        result.Urls.Contains("test.com").ShouldBeTrue();
        result.RoleId.ShouldBe(roleId);
    }
    //Should Get All Sites
    [Fact]
    public async Task Should_GetAll_Site()
    {
        var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<SiteService>();
        // create 10 sites
        const int count = 10;
        var siteIds = Enumerable.Range(1, count).Select(_ => Guid.NewGuid());
        var roleId = Guid.NewGuid();
        foreach (var siteId in siteIds)
        {
            var site = new Site(siteId,
                $"test site {siteId}",
                $"test site description {siteId}",
                [$"{siteId}.com"],
                roleId);
            await service.Create(site);
        }
        var result = (await service.GetAll()).ToList();
        result.Count.ShouldBe(count);
        result.ForEach(x => x.Name.ShouldBe($"test site {x.Id}"));
        result.ForEach(x => x.Description.ShouldBe($"test site description {x.Id}"));
        result.ForEach(x => x.Urls.Contains($"{x.Id}.com").ShouldBeTrue());
    }
}
