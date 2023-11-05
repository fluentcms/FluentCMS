using FluentCMS.Application;
using FluentCMS.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace FluentCMS.Repository.LiteDb.Tests.Entities.Pages;
public class PageService_Tests
{
    private ServiceProvider _serviceProvider;

    public PageService_Tests()
    {
        var services = new ServiceCollection();
        services.AddFluentCMSCore()
            .AddApplication()
            .AddLiteDbRepository(b => b.UseInMemory());
        _serviceProvider = services.BuildServiceProvider();
    }


    [Fact]
    public async Task Should_CreatePage()
    {
        var pageService = _serviceProvider.GetRequiredService<IPageService>();
        var pageRepository = _serviceProvider.GetRequiredService<IPageRepository>();
        Guid siteId = Guid.NewGuid();
        var page = await pageService.Create("test", siteId, null);
        await pageRepository.Create(page);
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
        var page = await pageService.Create("test", siteId, null);
        await pageRepository.Create(page);
        var dbEntity = await pageRepository.GetById(page.Id);
        dbEntity?.SetTitle("test2");
        await pageService.ValidateEdit(dbEntity);
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
        var page = await pageService.Create("test", siteId, null);
        await pageRepository.Create(page);
        Guid siteId2 = Guid.NewGuid();
        var taskToCreateDuplicatePage = pageService.Create("test2", siteId, null, path: "/test");
        await taskToCreateDuplicatePage.ShouldThrowAsync<ApplicationException>();
        var taskToEditDuplicatePage = pageService.Create("test2", siteId, null, path: "/test");
        var page2 = await pageService.Create("test2", siteId, null);
        await pageRepository.Create(page2);
        page.SetPath("/test2");
        var taskToValidateEdit_DuplicatePath = pageService.ValidateEdit(page);
        await taskToValidateEdit_DuplicatePath.ShouldThrowAsync<ApplicationException>();

    }
}
