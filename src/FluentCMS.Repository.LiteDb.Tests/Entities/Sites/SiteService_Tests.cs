using FluentCMS.Application;
using FluentCMS.Application.Dtos.Sites;
using FluentCMS.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace FluentCMS.Repository.LiteDb.Tests.Entities.Sites;
public class SiteService_Tests
{
    private readonly IServiceProvider _serviceProvider;
    public SiteService_Tests()
    {
        var services = new ServiceCollection();
        services.AddFluentCMSCore()
            .AddApplication()
            .AddLiteDbRepository(x => x.UseInMemory());
        _serviceProvider = services.BuildServiceProvider();
    }

    //Should Create New Site
    [Fact]
    public async Task Should_Create_New_Site()
    {
        var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISiteService>();
        var roleId = Guid.NewGuid();
        var siteId = await service.Create(new CreateSiteRequest
        {
            Name = "test site",
            Description = "test site description",
            URLs = ["test.com"],
            RoleId = roleId,
        });
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
        var service = scope.ServiceProvider.GetRequiredService<ISiteService>();
        var roleId = Guid.NewGuid();
        var siteId = await service.Create(new CreateSiteRequest
        {
            Name = "test site",
            Description = "test site description",
            URLs = ["test.com"],
            RoleId = roleId,
        });
        var dbSite = await service.GetById(siteId);
        await service.Edit(new EditSiteRequest
        {
            Id = siteId,
            Name = "test site edited",
            Description = "test site description edited",
            URLs = dbSite.Urls.ToArray(),
            RoleId = roleId,
        });
        await service.AddSiteUrl(new AddSiteUrlRequest
        {
            SiteId = siteId,
            NewUrl = "testEdited.com",
        });
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
        var service = scope.ServiceProvider.GetRequiredService<ISiteService>();
        var roleId = Guid.NewGuid();
        var siteId = await service.Create(new CreateSiteRequest
        {
            Name = "test site",
            Description = "test site description",
            URLs = ["test.com"],
            RoleId = roleId,
        });
        await service.Delete(new DeleteSiteRequest { Id = siteId });
        var result = await service.Search(new SearchSiteRequest());
        result.Total.ShouldBe(0);
    }

    //Should Get Site
    [Fact]
    public async Task Should_Get_Site()
    {
        var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISiteService>();
        var roleId = Guid.NewGuid();
        var siteId = await service.Create(new CreateSiteRequest
        {
            Name = "test site",
            Description = "test site description",
            URLs = ["test.com"],
            RoleId = roleId,
        });
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
        var service = scope.ServiceProvider.GetRequiredService<ISiteService>();
        var roleId = Guid.NewGuid();
        var siteId = await service.Create(new CreateSiteRequest
        {
            Name = "test site",
            Description = "test site description",
            URLs = ["test.com"],
            RoleId = roleId,
        });
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
        var service = scope.ServiceProvider.GetRequiredService<ISiteService>();
        // create 10 sites
        const int count = 10;
        var roleId = Guid.NewGuid();
        for (var i = 1; i <= 10; i++)
        {
            var siteId = await service.Create(new CreateSiteRequest
            {
                Name = $"test site {i}",
                Description = $"test site description {i}",
                URLs = [$"site-{i}.com"],
                RoleId = roleId,
            });
        }
        var result = await service.Search(new SearchSiteRequest());
        result.Total.ShouldBe(count);
        result.Data.ToList().ForEach(x => x.Name.ShouldBe($"test site {x.Id}"));
        result.Data.ToList().ForEach(x => x.Description.ShouldBe($"test site description {x.Id}"));
        result.Data.ToList().ForEach(x => x.Urls.Contains($"{x.Id}.com").ShouldBeTrue());
    }
}
