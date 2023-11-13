using FluentCMS.Tests.DummyEntities;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.Tests.Repositories.MongoDb;
public class MongoDb_GenericRepository_Tests : Base_MongoDb_Repository_Tests<DummyEntity>
{
    public MongoDb_GenericRepository_Tests():base()
    {
    }

    //Create
    [Fact]
    public async Task Should_Create()
    {
        var repository = GetRepository();
        var entity = new DummyEntity
        {
            DummyField = "TestField"
        };
        var createdEntity = await repository.Create(entity);
        createdEntity.ShouldNotBeNull();
        createdEntity.DummyField.ShouldBe(entity.DummyField);
        createdEntity.Id.ShouldNotBe(default);
    }
    //Update
    [Fact]
    public async Task Should_Update()
    {
        var repository = GetRepository();
        var entity = new DummyEntity
        {
            DummyField = "TestField"
        };
        var createdEntity = await repository.Create(entity);
        createdEntity.ShouldNotBeNull();
        createdEntity.DummyField = "UpdatedField";
        createdEntity.Id.ShouldNotBe(default);
        var updatedEntity = await repository.Update(createdEntity);
        updatedEntity.ShouldNotBeNull();
        updatedEntity.DummyField.ShouldBe("UpdatedField");
        updatedEntity.Id.ShouldBe(createdEntity.Id);
    }
    //Delete
    [Fact]
    public async Task Should_Delete()
    {
        //create
        var repository = GetRepository();
        var entity = new DummyEntity
        {
            DummyField = "TestField"
        };
        var createdEntity = await repository.Create(entity);
        createdEntity.ShouldNotBeNull();
        createdEntity.Id.ShouldNotBe(default);
        //delete
        var deletedEntity = await repository.Delete(createdEntity.Id);
        deletedEntity.ShouldNotBeNull();
        deletedEntity.Id.ShouldBe(createdEntity.Id);
        //verify deleted
        var allEntities = await repository.GetAll();
        allEntities.Count().ShouldBe(0);
    }
    //Get
    [Fact]
    public async Task Should_Get()
    {
        //create
        var repository = GetRepository();
        var entity = new DummyEntity
        {
            DummyField = "TestField"
        };
        var createdEntity = await repository.Create(entity);
        createdEntity.ShouldNotBeNull();
        createdEntity.Id.ShouldNotBe(default);
        //get
        var retrievedEntity = await repository.GetById(createdEntity.Id);
        retrievedEntity.ShouldNotBeNull();
        retrievedEntity.Id.ShouldBe(createdEntity.Id);
        retrievedEntity.DummyField.ShouldBe(createdEntity.DummyField);
    }
    //GetAll
    [Fact]
    public async Task Should_GetAll()
    {
        //create
        var repository = GetRepository();
        var entity = new DummyEntity
        {
            DummyField = "TestField"
        };
        var createdEntity = await repository.Create(entity);
        createdEntity.ShouldNotBeNull();
        createdEntity.Id.ShouldNotBe(default);
        //get
        var allEntities = await repository.GetAll();
        allEntities.Count().ShouldBe(1);
    }

}
