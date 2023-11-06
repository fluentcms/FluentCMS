using FluentCMS.Entities.Pages;
using FluentCMS.Repositories;
using FluentCMS.Repositories.Abstractions;
using FluentCMS.Services;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace FluentCMS.Repository.LiteDb.Tests.Entities.Pages;
public class PageService_Tests
{
    private ServiceProvider _serviceProvider;

    public PageService_Tests()
    {
        var services = new ServiceCollection();
        services.AddApplicationServices()
            .AddLiteDbInMemoryRepository();
        _serviceProvider = services.BuildServiceProvider();
    }


    [Fact]
    public async Task Should_CreatePage()
    {
        var pageService = _serviceProvider.GetRequiredService<IPageService>();
        var pageRepository = _serviceProvider.GetRequiredService<IPageRepository>();
        Guid siteId = Guid.NewGuid();
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
    public async Task Should_EditPage()
    {
        var pageService = _serviceProvider.GetRequiredService<IPageService>();
        var pageRepository = _serviceProvider.GetRequiredService<IPageRepository>();
        Guid siteId = Guid.NewGuid();
        var page = new Page() { Title = "test", SiteId = siteId, Path = "/test" };
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
    public async Task ShouldNot_CreateOrEdit_DuplicatePath()
    {
        var pageService = _serviceProvider.GetRequiredService<IPageService>();
        var pageRepository = _serviceProvider.GetRequiredService<IPageRepository>();
        Guid siteId = Guid.NewGuid();
        var page = new Page() { Title = "test", SiteId = siteId, Path = "/test" };
        await pageService.Create(page);
        Guid siteId2 = Guid.NewGuid();
        var duplciatePage = new Page() { Title = "test2", SiteId = siteId2, Path = "/test" };
        var taskToCreateDuplicatePage = pageService.Create(duplciatePage);
        await taskToCreateDuplicatePage.ShouldThrowAsync<ApplicationException>();

    }
}
