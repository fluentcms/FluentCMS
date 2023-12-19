using FluentCMS.Entities;
using FluentCMS.Repositories.MongoDB;
using FluentCMS.Web.UI.ApiClients;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace FluentCMS.E2eTests.ContentE2e;

public class Content_Tests : BaseE2eTest<IContentClient, ContentClient>
{
    [Fact]
    public async Task Should_Create_Content()
    {
        // setup dummy content-type
        var contentType = await SetupContentType();

        var content = new ContentCreateRequest() { };

        var response = await Client.CreateAsync(contentType.Name!, content);

        response.Errors.ShouldBeEmpty();
        response.Data.ShouldNotBeNull();
        response.Data.Id.ShouldNotBe(default);
    }


    [Fact]
    public async Task Should_Delete_Content()
    {
        var contentType = await SetupContentType();
        var siteId = Guid.Empty;
        var content = await SetupContent(contentType, siteId , new Dictionary<string, object>() {});

        var response = await Client.DeleteAsync(contentType.Name, content.Id);
        response.Errors.ShouldBeEmpty();
        response.Data.ShouldBeTrue();

        // check if content is deleted
        Client.GetByIdAsync(contentType.Name, content.Id).Result.Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Should_GetAll_Content()
    {
        var contentType = await SetupContentType();
        var siteId = Guid.Empty;
        var count = 10;
        var content = new List<Web.UI.ApiClients.Content>();
        for (int i = 0; i < count; i++)
        {
            var createdContent = await SetupContent(contentType, siteId, new Dictionary<string, object>() { });
            content.Add(createdContent);
        }

        var response = await Client.GetAllAsync(contentType.Name, siteId);
        response.Errors.ShouldBeEmpty();
        response.Data.ShouldNotBeNull();
        response.Data.Count.ShouldBe(count);
        response.Data.Select(x => x.Id).All(x => content.Select(x => x.Id).Contains(x)).ShouldBeTrue();
    }

    [Fact]
    public async Task Should_GetById_Content()
    {
        var contentType = await SetupContentType();
        var siteId = Guid.Empty;
        var content = await SetupContent(contentType, siteId, new Dictionary<string, object>() { });

        var response = await Client.GetByIdAsync(contentType.Name, content.Id);
        response.Errors.ShouldBeEmpty();
        response.Data.ShouldNotBeNull();
    }

    // PUT
    // /api/Content/{contentType}/Update
    // Updates an existing content entity in the system.
    private async Task<Web.UI.ApiClients.Content> SetupContent(Entities.ContentType contentType, Guid siteId, Dictionary<string, object> values)
    {
        // create a dummy content
        var content = new ContentCreateRequest()
        {
            SiteId = siteId,
            Value = values!
        };

        var response = await Client.CreateAsync(contentType.Name!, content);
        return response.Data;

    }

    private async Task<Entities.ContentType> SetupContentType()
    {
        var scope = WebUi.Services.CreateScope();
        var contentTypeRepository = scope.ServiceProvider.GetRequiredService<ContentTypeRepository>();
        var dummyContentType = new Entities.ContentType() { Name = "Dummy", Title = "Dummy" };
        dummyContentType = (await contentTypeRepository.Create(dummyContentType));
        return dummyContentType!;

    }
}
