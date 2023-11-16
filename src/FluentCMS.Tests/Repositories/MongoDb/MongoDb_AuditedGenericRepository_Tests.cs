using FluentCMS.Tests.DummyEntities;
using Shouldly;

namespace FluentCMS.Tests.Repositories.MongoDb;
public class MongoDb_AuditedGenericRepository_Tests : Base_MongoDb_Repository_Tests<AuditedDummyEntity>
{
    //Create
    [Fact]
    public async Task Should_Create()
    {
        //create
        var repository = GetRepository();
        var entity = new AuditedDummyEntity
        {
            DummyField = "TestField"
        };
        var createdEntity = await repository.Create(entity);
        createdEntity.ShouldNotBeNull();
        createdEntity.DummyField.ShouldBe("TestField");
        createdEntity.Id.ShouldNotBe(default);
        createdEntity.CreatedAt.ShouldNotBe(default);
        createdEntity.LastUpdatedAt.ShouldBe(default);
    }
    //Update
    [Fact]
    public async Task Should_Update()
    {
        //create
        var repository = GetRepository();
        var entity = new AuditedDummyEntity
        {
            DummyField = "TestField"
        };
        var createdEntity = await repository.Create(entity);
        createdEntity.ShouldNotBeNull();
        createdEntity.DummyField.ShouldBe("TestField");
        createdEntity.Id.ShouldNotBe(default);
        createdEntity.CreatedAt.ShouldNotBe(default);
        createdEntity.LastUpdatedAt.ShouldBe(default);
        //update
        createdEntity.DummyField = "UpdatedField";
        var updatedEntity = await repository.Update(createdEntity);
        updatedEntity.ShouldNotBeNull();
        updatedEntity.DummyField.ShouldBe("UpdatedField");
        updatedEntity.Id.ShouldBe(createdEntity.Id);
        updatedEntity.CreatedAt.ShouldBe(createdEntity.CreatedAt);
        updatedEntity.LastUpdatedAt.ShouldNotBe(default);
    }
}
