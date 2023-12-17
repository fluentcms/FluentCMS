using Shouldly;
using Microsoft.Extensions.DependencyInjection;
using FluentCMS.Repositories;
using FluentCMS.Web.UI.ApiClients;
using ContentType = FluentCMS.Web.UI.ApiClients.ContentType;

namespace FluentCMS.E2eTests.ContentTypeE2e;
public class ContentType_Tests : BaseE2eTest<IContentTypeClient, ContentTypeClient>
{
    // POST
    // /api/ContentType/Create
    [Fact]
    public async Task Should_Create()
    {
        // arrange
        var createRequest = new ContentType()
        {
            Title = "Test Content Type",
            Description = "This is a dummy content-type for testing",
            Name = "test-content-type"
        };
        // act
        var response = await Client.CreateAsync(createRequest);

        // assert
        response.ShouldNotBeNull();
        response?.Errors.ShouldBeEmpty();

        // assert properties
        response?.Data?.Id.ShouldNotBe(default);
        response?.Data?.Title.ShouldBe("Test Content Type");
        response?.Data?.Description.ShouldBe("This is a dummy content-type for testing");
        response?.Data?.Name.ShouldBe("test-content-type");
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
        var response = await Client.DeleteAsync(contentToBeDeleted.Id);

        // assert responseBody
        response?.Errors.ShouldBeEmpty();
        response.ShouldNotBeNull();
        response.Data.ShouldBeTrue();
    }

    // PUT
    // /api/ContentType/DeleteFie1d/{id}/{name}
    [Fact]
    public async Task Should_DeleteField()
    {
        var contentType = await PrepareAnEntity();

        // delete first field from contentType
        var fieldToBeDeleted = contentType.Fields.First();
        var response = await Client.DeleteFieldAsync(contentType.Id, fieldToBeDeleted.Name);

        // assert responseBody
        response?.Errors.ShouldBeEmpty();
        response.ShouldNotBeNull();
        response.Data.ShouldNotBeNull();
        response.Data.Fields!.Count().ShouldBe(1);
    }

    // GET
    // /api/ContentType/GetA11
    [Fact]
    public async Task Should_GetAll()
    {
        // add some dummy data
        var dummyData = new List<ContentType>();
        var count = 10;
        foreach (var _ in Enumerable.Range(1, count))
        {
            dummyData.Add(await PrepareAnEntity());
        }

        // act
        var response = await Client.GetAllAsync();

        // assert responseBody
        response.ShouldNotBeNull();
        response!.Errors.ShouldBeEmpty();
        response!.Data.ShouldNotBeNull();
        response.Data.Count().ShouldBe(count);

        // check that all ids are in the response
        dummyData.Select(x => x.Id).All(x => response.Data.Any(y => y.Id == x)).ShouldBeTrue();
    }

    // GET
    // /api/ContentType/GetById/{id}
    [Fact]
    public async Task Should_GetById()
    {
        // add dummy data
        var dummyData = await PrepareAnEntity();

        // try get dummy data
        var response = await Client.GetByIdAsync(dummyData.Id);

        // assert responseBody
        response.ShouldNotBeNull();
        response!.Errors.ShouldBeEmpty();
        response!.Data.ShouldNotBeNull();
        response.Data.Id.ShouldBe(dummyData.Id);
    }

    // GET
    // /api/ContentType/GetByName
    [Fact]
    public async Task Should_GetByName()
    {
        // add dummy data
        var dummyData = await PrepareAnEntity();

        // try get dummy data
        var response = await Client.GetByNameAsync(dummyData.Name);

        // assert responseBody
        response.ShouldNotBeNull();
        response!.Errors.ShouldBeEmpty();
        response!.Data.ShouldNotBeNull();
        response.Data.Id.ShouldBe(dummyData.Id);
        response.Data.Name.ShouldBe(dummyData.Name);
    }

    // PUT
    // /api/ContentType/SetField/{id}
    [Fact]
    public async Task Should_SetField()
    {
        // add dummy data
        var dummyData = await PrepareAnEntity();

        // try edit first field
        var firstField = dummyData.Fields!.First();
        // edit first field
        firstField.DefaultValue = "Updated value";
        firstField.Title = "Updated Title";
        firstField.Label = "Updated Label";
        firstField.Placeholder = "Updated Placeholder";
        firstField.Hint = "Updated Hint";
        firstField.Description = "Updated Description";
        var response = await Client.SetFieldAsync(dummyData.Id, firstField);

        // assert responseBody
        response.ShouldNotBeNull();
        response!.Errors.ShouldBeEmpty();
        response!.Data.ShouldNotBeNull();
        response.Data.Id.ShouldBe(dummyData.Id);
        var field = response.Data.Fields.First(x => x.Name == firstField.Name);
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
    public async Task Should_Update()
    {
        // add dummy data
        var dummyData = await PrepareAnEntity();

        // edit dummy data
        dummyData.Title = "Updated Title";
        dummyData.Description = "Updated Description";

        // try edit
        var response = await Client.UpdateAsync(dummyData);

        // assert responseBody
        response.ShouldNotBeNull();
        response!.Errors.ShouldBeEmpty();
        response!.Data.ShouldNotBeNull();
        response.Data.Id.ShouldBe(dummyData.Id);
        response.Data.Title.ShouldBe("Updated Title");
        response.Data.Description.ShouldBe("Updated Description");

        // check update date
        response.Data.LastUpdatedAt.ShouldBeGreaterThan(dummyData.LastUpdatedAt);
    }

    // this method will use Repository to create an Entity
    private async Task<ContentType> PrepareAnEntity()
    {
        // prepare a dummy entity to be created
        var contentType = new Entities.ContentType()
        {
            Title = "Test Content Type",
            Description = "This is a dummy content-type for testing",
            Name = "test-content-type",
            Fields =
            {
                new Entities.ContentTypeField()
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
                new Entities.ContentTypeField()
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
        contentType = await repository.Create(contentType) ?? throw new Exception("Could not Create");

        // return created entity
        // Map to Client Dto
        // TODO: find a better solution for this
        return new ContentType
        {
            Name = contentType.Name,
            Id = contentType.Id,
            Title = contentType.Title,
            Description = contentType.Description,
            Fields = contentType.Fields.Select(x => new Web.UI.ApiClients.ContentTypeField()
            {
                DefaultValue = x.DefaultValue,
                Description = x.Description,
                Hint = x.Hint,
                Placeholder = x.Placeholder,
                IsRequired = x.IsRequired,
                Label = x.Label,
                Name = x.Name,
                Title = x.Title,
            }).ToList(),
            LastUpdatedAt = contentType.LastUpdatedAt,
            CreatedAt = contentType.CreatedAt,
            CreatedBy = contentType.CreatedBy,
            LastUpdatedBy = contentType.LastUpdatedBy
        };
    }
}
