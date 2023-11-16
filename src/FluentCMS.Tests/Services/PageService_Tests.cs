using FluentCMS.Entities;
using FluentCMS.Repositories;
using FluentCMS.Services;
using FluentCMS.Tests.Helpers.ApplicationContext;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace FluentCMS.Tests.Services;
public class PageService_Tests
{
    private ServiceProvider _serviceProvider;

    public PageService_Tests()
    {
        var services = new ServiceCollection();
        services.AddApplicationServices()
            .AddInMemoryLiteDbRepositories()
            .AddTestApplicationContext();
        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Should_CreatePage()
    {
        var a = _serviceProvider.GetRequiredService<IApplicationContext>();
        var scope = _serviceProvider.CreateScope();
        var pageService = scope.ServiceProvider.GetRequiredService<IPageService>();
        var pageRepository = scope.ServiceProvider.GetRequiredService<IPageRepository>();
        scope.SetupMockApplicationContextForAdminUser();
        Guid siteId = ApplicationContextDefaults.DefaultSite.Id;
        var page = new Page() { Title = "test", SiteId = siteId, Path = "/test" };
        await pageService.Create(page);
        var result = await pageRepository.GetById(page.Id);
        result.ShouldNotBeNull();
        result.Title.ShouldBe("test");
        result.SiteId.ShouldBe(siteId);
        result.Path.ShouldBe("/test");
        result.Order.ShouldBe(0);
    }
    [Fact]
    public async Task ShouldNot_CreatePage_NonAdminUser()
    {
        var scope = _serviceProvider.CreateScope();
        var pageService = scope.ServiceProvider.GetRequiredService<IPageService>();
        var pageRepository = scope.ServiceProvider.GetRequiredService<IPageRepository>();
        scope.SetupMockApplicationContextForNonAdminUser();
        Guid siteId = ApplicationContextDefaults.DefaultSite.Id;
        var page = new Page() { Title = "test", SiteId = siteId, Path = "/test" };
        await pageService.Create(page).ShouldThrowAsync<Exception>();
    }

    [Fact]
    public async Task Should_UpdatePage()
    {
        var scope = _serviceProvider.CreateScope();
        var pageService = scope.ServiceProvider.GetRequiredService<IPageService>();
        var pageRepository = scope.ServiceProvider.GetRequiredService<IPageRepository>();
        Guid siteId = ApplicationContextDefaults.DefaultSite.Id;
        var page = new Page() { Title = "test", SiteId = siteId, Path = "/test" };
        scope.SetupMockApplicationContextForAdminUser();
        await pageService.Create(page);
        var dbEntity = await pageRepository.GetById(page.Id);
        dbEntity.ShouldNotBeNull();
        dbEntity.Title = "test2";
        await pageRepository.Update(dbEntity);
        var result = await pageRepository.GetById(page.Id);
        result.ShouldNotBeNull();
        result.Title.ShouldBe("test2");
        result.SiteId.ShouldBe(siteId);
        result.Path.ShouldBe("/test");
        result.Order.ShouldBe(0);
    }

    [Fact]
    public async Task ShouldNot_UpdatePage_NonAdminUser()
    {
        var scope = _serviceProvider.CreateScope();
        var pageService = scope.ServiceProvider.GetRequiredService<IPageService>();
        var pageRepository = scope.ServiceProvider.GetRequiredService<IPageRepository>();
        Guid siteId = ApplicationContextDefaults.DefaultSite.Id;
        scope.SetupMockApplicationContextForNonAdminUser();
        var page = new Page() { Title = "test", SiteId = siteId, Path = "/test" };
        await pageService.Create(page).ShouldThrowAsync<Exception>();
    }

    [Fact]
    public async Task ShouldNot_CreateOrUpdate_DuplicatePath()
    {
        var scope = _serviceProvider.CreateScope();
        var pageService = scope.ServiceProvider.GetRequiredService<IPageService>();
        var pageRepository = scope.ServiceProvider.GetRequiredService<IPageRepository>();
        Guid siteId = ApplicationContextDefaults.DefaultSite.Id;
        var page = new Page() { Title = "test", SiteId = siteId, Path = "/test" };
        scope.SetupMockApplicationContextForAdminUser();
        await pageService.Create(page);
        Guid siteId2 = Guid.NewGuid();
        var duplciatePage = new Page() { Title = "test2", SiteId = siteId2, Path = "/test" };
        var taskToCreateDuplicatePage = pageService.Create(duplciatePage);
        await taskToCreateDuplicatePage.ShouldThrowAsync<ApplicationException>();

    }
}
