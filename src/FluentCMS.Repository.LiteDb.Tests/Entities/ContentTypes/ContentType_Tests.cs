using FluentCMS.Entities.ContentTypes;
using FluentCMS.Repositories;
using FluentCMS.Services;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace FluentCMS.Repository.LiteDb.Tests.Entities.ContentTypes;
public class ContentType_Tests
{
    readonly IServiceProvider _serviceProvider;

    public ContentType_Tests()
    {
        var services = new ServiceCollection();
        services
            .AddApplicationServices()
            .AddLiteDbInMemoryRepository();
        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Should_Create()
    {
        var service = _serviceProvider.GetRequiredService<IContentTypeService>();
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
        var service = _serviceProvider.GetRequiredService<IContentTypeService>();
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
        var all = await service.Search();
        all.Count().ShouldBe(0);
    }

    [Fact]
    public async Task Should_Update()
    {
        var service = _serviceProvider.GetRequiredService<IContentTypeService>();
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
        var service = _serviceProvider.GetRequiredService<IContentTypeService>();
        var id = Guid.NewGuid();
        var title = "Test 1";
        var contentType = new ContentType(id, title);
        await service.Create(contentType);
        var title2 = "Test 2";
        var id2 = Guid.NewGuid();
        var contentType2 = new ContentType(id2, title2);
        contentType2.SetSlug("test-1");
        await service.Create(contentType2).ShouldThrowAsync<ApplicationException>();
    }

    [Fact]
    public async Task Should_GetAll()
    {
        var service = _serviceProvider.GetRequiredService<IContentTypeService>();

        const int count = 10;
        var ids = Enumerable.Range(1, count).Select(_ => Guid.NewGuid()).ToList();
        var index = 1;
        foreach (var id in ids)
        {
            var title = $"Test {index++}";
            var contentType = new ContentType(id, title);
            await service.Create(contentType);
        }
        var result = await service.Search();
        result.ShouldNotBeNull();
        result.Count().ShouldBe(count);
        result.All(x => ids.Contains(x.Id)).ShouldBeTrue();
    }

    [Fact]
    public async Task Should_GetById()
    {
        var service = _serviceProvider.GetRequiredService<IContentTypeService>();
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
        var service = _serviceProvider.GetRequiredService<IContentTypeService>();
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

    [Fact]
    public async Task Should_AddContentTypeField()
    {
        var service = _serviceProvider.GetRequiredService<IContentTypeService>();
        var id = Guid.NewGuid();
        var title = "Test 1";
        var contentType = new ContentType(id, title);
        ContentTypeField field = new(
            title: "field1",
            fieldType: FieldType.Text,
            options: new Dictionary<string, string>() { { "option1", "option1Value" } },
            label: "label1",
            description: "description 1",
            hidden: true,
            defaultValue: "field1DefaultValue");
        contentType.AddContentTypeField(field);
        await service.Create(contentType);
        var result = await service.GetById(id);
        result.ShouldNotBeNull();
        result.Id.ShouldBe(id);
        result.Title.ShouldBe(title);
        result.Slug.ShouldBe("test-1");
        result.ContentTypeFields.ShouldNotBeEmpty();
        result.ContentTypeFields.Count.ShouldBe(1);
        result.ContentTypeFields.First().Title.ShouldBe("field1");
        result.ContentTypeFields.First().FieldType.ShouldBe(FieldType.Text);
        result.ContentTypeFields.First().Description.ShouldBe("description 1");
        result.ContentTypeFields.First().Label.ShouldBe("label1");
        result.ContentTypeFields.First().Hidden.ShouldBe(true);
        result.ContentTypeFields.First().DefaultValue.ShouldBe("field1DefaultValue");
        result.ContentTypeFields.First().Options["option1"].ShouldBe("option1Value");
    }

    [Fact]
    public async Task Should_NotAddDuplicateContentTypeField()
    {
        var service = _serviceProvider.GetRequiredService<IContentTypeService>();
        var id = Guid.NewGuid();
        var title = "Test 1";
        var contentType = new ContentType(id, title);
        ContentTypeField field = new("field1",
            FieldType.Text,
            new Dictionary<string, string>() { { "option1", "option1Value" } });
        contentType.AddContentTypeField(field);
        ContentTypeField field2 = new("field1",
            FieldType.Text,
            new Dictionary<string, string>() { });
        await Task.Run(() =>
        {
            contentType.AddContentTypeField(field2);
        }).ShouldThrowAsync<ApplicationException>();

    }

    [Fact]
    public void Should_RemoveContentTypeField()
    {
        var service = _serviceProvider.GetRequiredService<IContentTypeService>();
        var id = Guid.NewGuid();
        var title = "Test 1";
        var contentType = new ContentType(id, title);
        ContentTypeField field = new ContentTypeField("field1",
                                                      FieldType.Text,
                                                      new Dictionary<string, string>() { { "option1", "option1Value" } });
        contentType.AddContentTypeField(field);
        contentType.RemoveContentTypeField(contentType.ContentTypeFields.First());
        contentType.ContentTypeFields.ShouldBeEmpty();
    }
}
