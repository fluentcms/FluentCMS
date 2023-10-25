using FluentCMS.Repository.LiteDb.Tests.Entities;
using Newtonsoft.Json;
using Shouldly;

namespace FluentCMS.Repository.LiteDb.Tests;
public class LiteDbRepository_Tests
{
    [Fact]
    public async Task Should_Create()
    {
        var repository = GetInstance();
        var guid = Guid.NewGuid();
        var entity = new DummyEntity(guid);
        await repository.Create(entity);
        var dbEntity = await repository.GetById(guid);
        dbEntity.ShouldNotBeNull();
        dbEntity.Id.ShouldBe(guid);
    }

    [Fact]
    public async Task Should_Update()
    {
        var repository = GetInstance();
        var guid = Guid.NewGuid();
        var entity = new DummyEntity(guid);
        await repository.Create(entity);
        // Serialize & Deserialize the entity to break reference (deep-copy)
        // todo: can utilize Prototype Pattern to avoid this mess
        var deepCopy = JsonConvert.DeserializeObject<DummyEntity>(JsonConvert.SerializeObject(entity)) ?? throw new InvalidOperationException("This should not happen");
        deepCopy.DummyField = "Updated!";
        await repository.Update(deepCopy);
        var dbEntity = await repository.GetById(guid);
        dbEntity.ShouldNotBeNull();
        dbEntity.Id.ShouldBe(guid);
        dbEntity.DummyField.ShouldBe("Updated!");
    }

    [Fact]
    public async Task Should_Delete()
    {
        var repository = GetInstance();
        var guid = Guid.NewGuid();
        var entity = new DummyEntity(guid);
        await repository.Create(entity);
        await repository.Delete(guid);
        var dbEntities = await repository.GetAll();
        dbEntities.ShouldNotBeNull();
        dbEntities.ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_GetById()
    {
        var repository = GetInstance();
        var guid = Guid.NewGuid();
        var entity = new DummyEntity(guid);
        await repository.Create(entity);
        var dbEntity = await repository.GetById(guid);
        dbEntity.ShouldNotBeNull();
        dbEntity.Id.ShouldBe(guid);
    }

    [Fact]
    public async Task Should_GetByIds()
    {
        var repository = GetInstance();
        const int count = 10;
        // generate N guids and entities
        var guids = Enumerable.Range(1, count).Select(_ => Guid.NewGuid()).ToList();
        var entities = guids.Select(x => new DummyEntity(x)).ToList();
        // todo:investigate feasibility of Implementing a CreateMany Method
        foreach (var entity in entities)
        {
            await repository.Create(entity);
        }

        var dbEntities = (await repository.GetByIds(guids)).ToList();
        dbEntities.ShouldNotBeNull();
        dbEntities.Count.ShouldBe(count);
        dbEntities.Select(x => x.Id).ShouldAllBe(x => guids.Contains(x));
    }

    [Fact]
    public async Task Should_GetAll()
    {
        var repository = GetInstance();
        const int count = 10;
        // generate N guids and entities
        var guids = Enumerable.Range(1, count).Select(_ => Guid.NewGuid()).ToList();
        var entities = guids.Select(x => new DummyEntity(x)).ToList();
        foreach (var entity in entities)
        {
            await repository.Create(entity);
        }
        var dbEntities = (await repository.GetAll()).ToList();
        dbEntities.ShouldNotBeNull();
        dbEntities.Count.ShouldBe(count);
        dbEntities.Select(x => x.Id).ShouldAllBe(x => guids.Contains(x));
    }

    [Fact]
    public async Task Should_GetAll_With_Filter()
    {
        var repository = GetInstance();
        const int count = 10;
        // generate N guids and entities
        var guids = Enumerable.Range(1, count).Select(_ => Guid.NewGuid()).ToList();
        var entities = guids.Select(x => new DummyEntity(x)).ToList();
        foreach (var entity in entities)
        {
            await repository.Create(entity);
        }
        var firstGuid = guids.First();
        var dbEntities = (await repository.GetAll(x => x.Id == firstGuid)).ToList();
        dbEntities.ShouldNotBeNull();
        dbEntities.Count.ShouldBe(1);
        dbEntities.All(x => x.Id == firstGuid).ShouldBeTrue();
    }

    private static LiteDbGenericRepository<DummyEntity> GetInstance()
    {
        var liteDbContext = new LiteDbContext(
            new LiteDbOptionsBuilder().UseInMemory().Build());
        return new LiteDbGenericRepository<DummyEntity>(liteDbContext);
    }
}