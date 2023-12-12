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
        var scope = _serviceProvider.CreateScope();

        //Seed Test Site and Host to InMemory Db
        _ = await SeedDefaultHost(scope);
        var defaultSite = await SeedDefaultSite(scope);

        var pageService = scope.ServiceProvider.GetRequiredService<IPageService>();
        var pageRepository = scope.ServiceProvider.GetRequiredService<IPageRepository>();
        scope.SetupMockApplicationContextForAdminUser();
        Guid siteId = defaultSite.Id;
        var page = new Page() { Title = "test", SiteId = siteId, Path = "/test" };
        await pageService.Create(page);
        var result = await pageRepository.GetById(page.Id);
        result.ShouldNotBeNull();
        result.Title.ShouldBe("test");
        result.SiteId.ShouldBe(siteId);
        result.Path.ShouldBe("/test");
        result.Order.ShouldBe(0);
    }

    private static async Task<Host> SeedDefaultHost(IServiceScope scope)
    {
        //setup Default Host
        var hostRepository = scope.ServiceProvider.GetRequiredService<IHostRepository>();
        var defaultHost = await hostRepository.Create(ApplicationContextDefaults.DefaultHost);
        return defaultHost!;
    }

    private static async Task<Site> SeedDefaultSite(IServiceScope scope)
    {
        //Setup Default Site
        var siteRepository = scope.ServiceProvider.GetRequiredService<ISiteRepository>();
        var defaultSite = await siteRepository.Create(ApplicationContextDefaults.DefaultSite);
        return defaultSite!;
    }

    [Fact]
    public async Task ShouldNot_CreatePage_NonAdminUser()
    {
        var scope = _serviceProvider.CreateScope();

        //Seed Test Site and Host to InMemory Db
        _ = await SeedDefaultHost(scope);
        var defaultSite = await SeedDefaultSite(scope);

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

        //Seed Test Site and Host to InMemory Db
        _ = await SeedDefaultHost(scope);
        var defaultSite = await SeedDefaultSite(scope);

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

        //Seed Test Site and Host to InMemory Db
        _ = await SeedDefaultHost(scope);
        var defaultSite = await SeedDefaultSite(scope);

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

        //Seed Test Site and Host to InMemory Db
        _ = await SeedDefaultHost(scope);
        var defaultSite = await SeedDefaultSite(scope);

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


    [Fact]
    public async Task ShouldNot_CreateChildOnOtherSite()
    {
        var scope = _serviceProvider.CreateScope();

        //Seed Test Site and Host to InMemory Db
        _ = await SeedDefaultHost(scope);
        var defaultSite = await SeedDefaultSite(scope);

        // setup other site
        var siteRepository = scope.ServiceProvider.GetRequiredService<ISiteRepository>();
        var otherSite = await siteRepository.Create(new Site() { Id = Guid.NewGuid(), Name = "OtherSite" });

        //create parent page
        var pageRepository = scope.ServiceProvider.GetRequiredService<IPageRepository>();
        var parentPage = await pageRepository.Create(new Page() { Title = "ParentPage", Path = "parent", SiteId = otherSite.Id });

        // try to create child
        scope.SetupMockApplicationContextForSuperUser();
        var pageService = scope.ServiceProvider.GetRequiredService<IPageService>();
        var exception = await pageService.Create(new Page() { Title = "ChildPage", Path = "child-page", ParentId = parentPage.Id, SiteId = defaultSite.Id })
                                         .ShouldThrowAsync<AppException>();

        //Check ErrorCode
        exception.Errors.ShouldContain(x => x.Code == ExceptionCodes.PageParentMustBeOnTheSameSite);
    }

    [Fact]
    public async Task ShouldNot_AllowRecursiveMatchingUrls()
    {
        var scope = _serviceProvider.CreateScope();

        //Seed Test Site and Host to InMemory Db
        _ = await SeedDefaultHost(scope);
        var defaultSite = await SeedDefaultSite(scope);


        //create parent page
        var pageRepository = scope.ServiceProvider.GetRequiredService<IPageRepository>();
        var parentPage = await pageRepository.Create(new Page() { Id = Guid.NewGuid(), Title = "ParentPage", Path = "parent", SiteId = defaultSite.Id });

        // create a child
        var childPage = await pageRepository.Create(new Page()
        {
            Id = Guid.NewGuid(),
            Title = "ChildPage",
            Path = "child-page",
            ParentId = parentPage!.Id,
            SiteId = defaultSite.Id
        });

        // try to create child with the same path
        scope.SetupMockApplicationContextForSuperUser();
        var pageService = scope.ServiceProvider.GetRequiredService<IPageService>();
        var exception = await pageService.Create(new Page()
        {
            Id = Guid.NewGuid(),
            Title = "ChildPage",
            Path = "child-page",
            ParentId = parentPage!.Id,
            SiteId = defaultSite.Id
        }).ShouldThrowAsync<AppException>();

        //Check ErrorCode
        exception.Errors.ShouldContain(x => x.Code == ExceptionCodes.PagePathMustBeUnique);

        // try to create a child with matching path recursively 
        var secondException = await pageService.Create(new Page()
        {
            Title = "ChildPage",
            Path = "parent/child-page",
            SiteId = defaultSite.Id
        }).ShouldThrowAsync<AppException>();

        //Check ErrorCode
        secondException.Errors.ShouldContain(x => x.Code == ExceptionCodes.PagePathMustBeUnique);

    }

    [Fact]
    public async Task ShouldNot_AllowMoreThanParentPermission()
    {
        var scope = _serviceProvider.CreateScope();

        //Seed Test Site and Host to InMemory Db
        _ = await SeedDefaultHost(scope);
        var defaultSite = await SeedDefaultSite(scope);

        //roles To Use
        var roles = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();

        //create parent page
        var pageRepository = scope.ServiceProvider.GetRequiredService<IPageRepository>();
        var parentPage = await pageRepository.Create(new Page()
        {
            Id = Guid.NewGuid(),
            Title = "ParentPage",
            Path = "parent",
            SiteId = defaultSite.Id,
            ViewRoleIds = [roles[0], roles[1]]
        });

        // try to create a child with more required permissions than parent
        scope.SetupMockApplicationContextForSuperUser();
        var pageService = scope.ServiceProvider.GetRequiredService<IPageService>();
        var exception = await pageService.Create(new Page()
        {
            Id = Guid.NewGuid(),
            Title = "ChildPage",
            Path = "child-page",
            ParentId = parentPage!.Id,
            SiteId = defaultSite.Id,
            ViewRoleIds = [roles[0], roles[1], roles[2]]
        }).ShouldThrowAsync<AppException>();

        exception.Errors.ShouldContain(x => x.Code == ExceptionCodes.PageViewPermissionsAreNotASubsetOfParent);
    }

    [Fact]
    public async Task ShouldNot_AllowModificationOfSiteId()
    {
        var scope = _serviceProvider.CreateScope();

        //Seed Test Site and Host to InMemory Db
        _ = await SeedDefaultHost(scope);
        var defaultSite = await SeedDefaultSite(scope);

        // setup other site
        var siteRepository = scope.ServiceProvider.GetRequiredService<ISiteRepository>();
        var otherSite = await siteRepository.Create(new Site() { Id = Guid.NewGuid(), Name = "OtherSite" });

        //roles To Use
        var roles = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();

        //create a page
        var pageRepository = scope.ServiceProvider.GetRequiredService<IPageRepository>();
        var page = await pageRepository.Create(new Page()
        {
            Id = Guid.NewGuid(),
            Title = "ParentPage",
            Path = "parent",
            SiteId = defaultSite.Id,
            ViewRoleIds = [roles[0], roles[1]]
        });
        // Try Modification of SiteId
        scope.SetupMockApplicationContextForSuperUser();
        var pageService = scope.ServiceProvider.GetRequiredService<IPageService>();
        var pageToEdit = new Page()
        {
            Id = page!.Id,
            Title = "ParentPage",
            Path = "parent",
            SiteId = otherSite!.Id,
            ViewRoleIds = [roles[0], roles[1]]
        };
        var exception = await pageService.Update(pageToEdit).ShouldThrowAsync<AppException>();

        exception.Errors.ShouldContain(x => x.Code == ExceptionCodes.PageSiteIdCannotBeChanged);

    }

    [Fact]
    public async Task ShouldNot_AllowRemovalOfAParentPageWithChildren()
    {
        var scope = _serviceProvider.CreateScope();

        //Seed Test Site and Host to InMemory Db
        _ = await SeedDefaultHost(scope);
        var defaultSite = await SeedDefaultSite(scope);


        //create parent page
        var pageRepository = scope.ServiceProvider.GetRequiredService<IPageRepository>();
        var parentPage = await pageRepository.Create(new Page() { Id = Guid.NewGuid(), Title = "ParentPage", Path = "parent", SiteId = defaultSite.Id });

        // create a child
        var childPage = await pageRepository.Create(new Page()
        {
            Id = Guid.NewGuid(),
            Title = "ChildPage",
            Path = "child-page",
            ParentId = parentPage!.Id,
            SiteId = defaultSite.Id
        });

        //try to delete parent page
        scope.SetupMockApplicationContextForSuperUser();
        var pageService = scope.ServiceProvider.GetRequiredService<IPageService>();
        var exception = await pageService.Delete(parentPage.Id).ShouldThrowAsync<AppException>();

        exception.Errors.ShouldContain(x => x.Code == ExceptionCodes.PageHasChildren);
    }
}
