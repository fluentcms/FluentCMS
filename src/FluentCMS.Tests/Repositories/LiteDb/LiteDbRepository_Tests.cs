using FluentCMS.Repositories.LiteDb;
using FluentCMS.Tests.DummyEntities;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Shouldly;

namespace FluentCMS.Tests.Repositories.LiteDb;
public class LiteDbRepository_Tests
{
    [Fact]
    public async Task Should_Create()
    {
        var repository = GetInstance();
        var entity = new DummyEntity();
        await repository.Create(entity);
        var dbEntities = await repository.GetAll();
        dbEntities.Count().ShouldBe(1);
    }

    [Fact]
    public async Task Should_Update()
    {
        var repository = GetInstance();
        var entity = new DummyEntity();
        entity = await repository.Create(entity);
        // Serialize & Deserialize the entity to break reference (deep-copy)
        // todo: can utilize Prototype Pattern to avoid this mess
        var deepCopy = JsonConvert.DeserializeObject<DummyEntity>(JsonConvert.SerializeObject(entity)) ?? throw new InvalidOperationException("This should not happen");
        deepCopy.DummyField = "Updated!";
        await repository.Update(deepCopy);
        var dbEntity = await repository.GetById(deepCopy.Id);
        dbEntity.ShouldNotBeNull();
        dbEntity.Id.ShouldBe(deepCopy.Id);
        dbEntity.DummyField.ShouldBe("Updated!");
    }

    [Fact]
    public async Task Should_Delete()
    {
        var repository = GetInstance();
        var entity = new DummyEntity();
        entity = await repository.Create(entity);
        await repository.Delete(entity.Id);
        var dbEntities = await repository.GetAll();
        dbEntities.ShouldNotBeNull();
        dbEntities.ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_GetById()
    {
        var repository = GetInstance();
        var entity = new DummyEntity();
        entity = await repository.Create(entity);
        var dbEntity = await repository.GetById(entity.Id);
        dbEntity.ShouldNotBeNull();
        dbEntity.Id.ShouldBe(entity.Id);
    }

    [Fact]
    public async Task Should_GetByIds()
    {
        var repository = GetInstance();
        const int count = 10;
        // generate N guids and entities
        var guids = new List<Guid>();
        var entities = Enumerable.Range(1, count).Select(_ => new DummyEntity()).ToList();
        // todo:investigate feasibility of Implementing a CreateMany Method
        foreach (var entity in entities)
        {
            var saved = await repository.Create(entity);
            guids.Add(saved.Id);
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
        var guids = new List<Guid>();
        var entities = Enumerable.Range(1, count).Select(_ => new DummyEntity()).ToList();
        foreach (var entity in entities)
        {
            var saved = await repository.Create(entity);
            guids.Add(saved.Id);
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
        var guids = new List<Guid>();
        var entities = Enumerable.Range(1, count).Select(_ => new DummyEntity()).ToList();
        foreach (var entity in entities)
        {
            var saved = await repository.Create(entity);
            guids.Add(saved.Id);
        }
        var firstGuid = guids.First();
        var dbEntities = (await repository.GetAll()).Where(x => x.Id == firstGuid).ToList();
        dbEntities.ShouldNotBeNull();
        dbEntities.Count.ShouldBe(1);
        dbEntities.All(x => x.Id == firstGuid).ShouldBeTrue();
    }

    private static LiteDbGenericRepository<DummyEntity> GetInstance()
    {
        var liteDbContext = new LiteDbContext(Options.Create(new LiteDbOptions()));
        return new LiteDbGenericRepository<DummyEntity>(liteDbContext);
    }
}
