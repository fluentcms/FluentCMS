using FluentCMS.Entities.ContentTypes;
using FluentCMS.Services;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.Repository.LiteDb.Tests.Entities.ContentTypes;
public class ContentType_Tests
{
    IServiceProvider _serviceProvider;
    public ContentType_Tests()
    {
        var services = new ServiceCollection();
        services.AddFluentCMSCore().AddLiteDbRepository(b => b.UseInMemory());
        _serviceProvider = services.BuildServiceProvider();
    }
    [Fact]
    public async Task Should_Create()
    {
        var service = _serviceProvider.GetRequiredService<ContentTypeService>();
        var id = Guid.NewGuid();
        var title = "Test 1";
        var contentType = new ContentType(id, title);
        await service.Create(contentType);
        var result = await service.GetById(id);
        result.ShouldNotBeNull();
        result.Id.ShouldBe(id);
        result.Title.ShouldBe(title);
        result.Slug.ShouldBe("test-1");
    }
    [Fact]
    public async Task Should_Delete()
    {
        var service = _serviceProvider.GetRequiredService<ContentTypeService>();
        var id = Guid.NewGuid();
        var title = "Test 1";
        var contentType = new ContentType(id, title);
        await service.Create(contentType);
        var result = await service.GetById(id);
        result.ShouldNotBeNull();
        result.Id.ShouldBe(id);
        result.Title.ShouldBe(title);
        result.Slug.ShouldBe("test-1");
        await service.Delete(id);
        var all = await service.GetAll();
        all.Count().ShouldBe(0);
    }
    [Fact]
    public async Task Should_Update()
    {
        var service = _serviceProvider.GetRequiredService<ContentTypeService>();
        var id = Guid.NewGuid();
        var title = "Test 1";
        var contentType = new ContentType(id, title);
        await service.Create(contentType);
        var result = await service.GetById(id);
        result.ShouldNotBeNull();
        result.Id.ShouldBe(id);
        result.Title.ShouldBe(title);
        result.Slug.ShouldBe("test-1");
        const string editedTitle = "Test 2";
        var editedContentType = new ContentType(id, editedTitle);
        await service.Edit(editedContentType);

        var editedResult = await service.GetById(id);
        editedResult.ShouldNotBeNull();
        editedResult.Title.ShouldBe(editedTitle);
        editedResult.Slug.ShouldBe("test-2");
    }
    [Fact]
    public async Task Should_NotAllowDuplicateSlugOn_Update()
    {
        var service = _serviceProvider.GetRequiredService<ContentTypeService>();
        var id = Guid.NewGuid();
        var title = "Test 1";
        var contentType = new ContentType(id, title);
        await service.Create(contentType);
        var title2 = "Test 2";
        var id2 = Guid.NewGuid();
        var contentType2 = new ContentType(id2, title);
        contentType2.SetSlug("test-1");
        await service.Create(contentType2).ShouldThrowAsync<ApplicationException>();

    }
    [Fact]
    public async Task Should_GetAll()
    {
        var service = _serviceProvider.GetRequiredService<ContentTypeService>();

        const int count = 10;
        var ids = Enumerable.Range(1, count).Select(_ => Guid.NewGuid()).ToList();
        var index = 1;
        foreach (var id in ids)
        {
            var title = $"Test {index++}";
            var contentType = new ContentType(id, title);
            await service.Create(contentType);

        }
        var result = await service.GetAll();
        result.ShouldNotBeNull();
        result.Count().ShouldBe(count);
        result.All(x => ids.Contains(x.Id)).ShouldBeTrue();
    }
    [Fact]
    public async Task Should_GetById()
    {
        var service = _serviceProvider.GetRequiredService<ContentTypeService>();
        var id = Guid.NewGuid();
        var title = "Test 1";
        var contentType = new ContentType(id, title);
        await service.Create(contentType);
        var result = await service.GetById(id);
        result.ShouldNotBeNull();
        result.Id.ShouldBe(id);
        result.Title.ShouldBe(title);
        result.Slug.ShouldBe("test-1");
    }
    [Fact]
    public async Task Should_GetBySlug()
    {
        var service = _serviceProvider.GetRequiredService<ContentTypeService>();
        var id = Guid.NewGuid();
        var title = "Test 1";
        var slug = "test-1";
        var contentType = new ContentType(id, title);
        contentType.SetSlug(slug);
        await service.Create(contentType);
        var result = await service.GetBySlug(slug);
        result.ShouldNotBeNull();
        result.Id.ShouldBe(id);
        result.Title.ShouldBe(title);
        result.Slug.ShouldBe("test-1");
    }

}
