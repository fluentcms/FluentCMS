using FluentCMS.Entities;
using System.Net.Http.Json;
using FluentCMS.Api.Models;
using Shouldly;
using Microsoft.Extensions.DependencyInjection;
using FluentCMS.Repositories;

namespace FluentCMS.E2eTests.ContentType;
public class ContentType_Tests : BaseE2eTest
{
    // POST
    // /api/ContentType/Create
    [Fact]
    public async Task Should_Create()
    {
        // arrange
        var createRequest = new Entities.ContentType()
        {
            Title = "Test Content Type",
            Description = "This is a dummy content-type for testing",
            Name = "test-content-type"
        };
        // act
        var response = await HttpClient.PostAsJsonAsync("/api/ContentType/Create", createRequest);

        // assert
        response.IsSuccessStatusCode.ShouldBeTrue();

        var created = await response.Content.ReadFromJsonAsync<ApiResult<Entities.ContentType>>();
        created.ShouldNotBeNull();
        created?.Errors.ShouldBeEmpty();

        // assert properties
        created?.Data?.Id.ShouldNotBe(default);
        created?.Data?.Title.ShouldBe("Test Content Type");
        created?.Data?.Description.ShouldBe("This is a dummy content-type for testing");
        created?.Data?.Name.ShouldBe("test-content-type");
    }

    // DELETE
    // /api/ContentType/Delete/{id}
    [Fact]
    public async Task Should_Delete()
    {
        // arrange
        // we don't need to call Create Api Again, because it will throw unrelated exceptions if there is something wrong with create logic, which is Create Test's responsibility to test!
        var contentToBeDeleted = await PrepareAnEntity();

        // act
        var response = await HttpClient.DeleteAsync($"/api/ContentType/Delete/{contentToBeDeleted.Id}");

        // assert status code
        response.IsSuccessStatusCode.ShouldBeTrue();

        // assert responseBody
        var responseBody = await response.Content.ReadFromJsonAsync<ApiResult<bool>>();
        responseBody?.Errors.ShouldBeEmpty();
        responseBody.ShouldNotBeNull();
        responseBody.Data.ShouldBeTrue();
    }

    // PUT
    // /api/ContentType/DeleteFie1d/{id}/{name}
    [Fact]
    public async Task Should_DeleteField()
    {
        var contentType = await PrepareAnEntity();

        // delete first field from contentType
        var fieldToBeDeleted = contentType.Fields.First();
        var response = await HttpClient.PutAsync($"/api/ContentType/DeleteField/{contentType.Id}/{fieldToBeDeleted.Name}", null);

        // assert response
        response.IsSuccessStatusCode.ShouldBeTrue();

        // assert responseBody
        var responseBody = await response.Content.ReadFromJsonAsync<ApiResult<Entities.ContentType>>();
        responseBody?.Errors.ShouldBeEmpty();
        responseBody.ShouldNotBeNull();
        responseBody.Data.ShouldNotBeNull();
        responseBody.Data.Fields.Count().ShouldBe(1);
    }

    // GET
    // /api/ContentType/GetA11
    [Fact]
    public async Task Should_GetAll()
    {
        // add some dummy data
        var dummyData = new List<Entities.ContentType>();
        var count = 10;
        foreach (var _ in Enumerable.Range(1, count))
        {
            dummyData.Add(await PrepareAnEntity());
        }

        // act
        var response = await HttpClient.GetAsync("/api/ContentType/GetAll");

        // assert status code
        response.IsSuccessStatusCode.ShouldBeTrue();
        // assert responseBody
        var responseBody = await response.Content.ReadFromJsonAsync<ApiResult<List<Entities.ContentType>>>();
        responseBody.ShouldNotBeNull();
        responseBody!.Errors.ShouldBeEmpty();
        responseBody!.Data.ShouldNotBeNull();
        responseBody.Data.Count().ShouldBe(count);

        // check that all ids are in the response
        dummyData.Select(x => x.Id).All(x => responseBody.Data.Any(y => y.Id == x)).ShouldBeTrue();
    }

    // GET
    // /api/ContentType/GetById/{id}
    [Fact]
    public async Task Should_GetById()
    {
        // add dummy data
        var dummyData = await PrepareAnEntity();

        // try get dummy data
        var response = await HttpClient.GetAsync($"/api/ContentType/GetById/{dummyData.Id}");

        // assert status code
        response.IsSuccessStatusCode.ShouldBeTrue();

        // assert responseBody
        var responseBody = await response.Content.ReadFromJsonAsync<ApiResult<Entities.ContentType>>();
        responseBody.ShouldNotBeNull();
        responseBody!.Errors.ShouldBeEmpty();
        responseBody!.Data.ShouldNotBeNull();
        responseBody.Data.Id.ShouldBe(dummyData.Id);
    }

    // GET
    // /api/ContentType/GetByName
    [Fact]
    public async Task Should_GetByName() {
        // add dummy data
        var dummyData = await PrepareAnEntity();

        // try get dummy data
        var response = await HttpClient.GetAsync($"/api/ContentType/GetByName?name={dummyData.Name}");

        // assert status code
        response.IsSuccessStatusCode.ShouldBeTrue();

        // assert responseBody
        var responseBody = await response.Content.ReadFromJsonAsync<ApiResult<Entities.ContentType>>();
        responseBody.ShouldNotBeNull();
        responseBody!.Errors.ShouldBeEmpty();
        responseBody!.Data.ShouldNotBeNull();
        responseBody.Data.Id.ShouldBe(dummyData.Id);
        responseBody.Data.Name.ShouldBe(dummyData.Name);
    }

    // PUT
    // /api/ContentType/SetField/{id}
    [Fact]
    public async Task Should_SetField() {
        // add dummy data
        var dummyData = await PrepareAnEntity();

        // try edit first field
        var firstField = dummyData.Fields.First();
        // edit first field
        firstField.DefaultValue = "Updated value";
        firstField.Title = "Updated Title";
        firstField.Label = "Updated Label";
        firstField.Placeholder = "Updated Placeholder";
        firstField.Hint = "Updated Hint";
        firstField.Description = "Updated Description";
        var response = await HttpClient.PutAsJsonAsync($"/api/ContentType/SetField/{dummyData.Id}",firstField);

        // assert status code
        response.IsSuccessStatusCode.ShouldBeTrue();

        // assert responseBody
        var responseBody = await response.Content.ReadFromJsonAsync<ApiResult<Entities.ContentType>>();
        responseBody.ShouldNotBeNull();
        responseBody!.Errors.ShouldBeEmpty();
        responseBody!.Data.ShouldNotBeNull();
        responseBody.Data.Id.ShouldBe(dummyData.Id);
        var field = responseBody.Data.Fields.First(x=>x.Name == firstField.Name);
        field.DefaultValue.ShouldBe("Updated value");
        field.Title.ShouldBe("Updated Title");
        field.Label.ShouldBe("Updated Label");
        field.Placeholder.ShouldBe("Updated Placeholder");
        field.Hint.ShouldBe("Updated Hint");
        field.Description.ShouldBe("Updated Description");

    }

    // PUT
    // /api/ContentType/Update
    [Fact]
    public async Task Should_Update() {
        // add dummy data
        var dummyData = await PrepareAnEntity();

        // edit dummy data
        dummyData.Title = "Updated Title";
        dummyData.Description = "Updated Description";

        // try edit
        var response = await HttpClient.PutAsJsonAsync($"/api/ContentType/Update", dummyData);

        // assert status code
        response.IsSuccessStatusCode.ShouldBeTrue();

        // assert responseBody
        var responseBody = await response.Content.ReadFromJsonAsync<ApiResult<Entities.ContentType>>();
        responseBody.ShouldNotBeNull();
        responseBody!.Errors.ShouldBeEmpty();
        responseBody!.Data.ShouldNotBeNull();
        responseBody.Data.Id.ShouldBe(dummyData.Id);
        responseBody.Data.Title.ShouldBe("Updated Title");
        responseBody.Data.Description.ShouldBe("Updated Description");

        // check update date
        responseBody.Data.LastUpdatedAt.ShouldBeGreaterThan(dummyData.LastUpdatedAt);
    }

    // this method will use Repository to create an Entity
    private async Task<Entities.ContentType> PrepareAnEntity()
    {
        // prepare a dummy entity to be created
        var contentType = new Entities.ContentType()
        {
            Title = "Test Content Type",
            Description = "This is a dummy content-type for testing",
            Name = "test-content-type",
            Fields =
            {
                new ContentTypeField()
                {
                    Name = "test-field",
                    Title = "Test Field",
                    DefaultValue = "test",
                    Label = "Test",
                    Description = "This is a description for Test Field",
                    IsRequired = true,
                    Hint="this is a hint for test field",
                    Placeholder = "this is a placeholder for test field",
                },
                new ContentTypeField()
                {

                    Name = "test-field-2",
                    Title = "Test Field 2",
                    DefaultValue = "test2",
                    Label = "Test 2",
                    Description = "This is a description for Test Field 2",
                    IsRequired = false,
                    Hint = "this is a hint for test field 2",
                    Placeholder = "this is a placeholder for test field 2",
                }
            }
        };
        // create scope
        var scope = WebUi.Services.CreateScope();

        // add entity to database
        var repository = scope.ServiceProvider.GetRequiredService<IContentTypeRepository>();
        contentType = await repository.Create(contentType);

        // return created entity
        return contentType!;
    }
}
