using FluentCMS.Repositories.LiteDb;
using FluentCMS.Repository.LiteDb.Tests.Entities;
using Newtonsoft.Json;
using Shouldly;

namespace FluentCMS.Repository.LiteDb.Tests;
public class LiteDbAuditRepository_Tests
{
    [Fact]
    public async Task Should_Create()
    {
        var utcNow = DateTime.UtcNow;
        var repository = GetInstance();
        var entity = new AuditedDummyEntity();
        entity = await repository.Create(entity);
        var dbEntity = await repository.GetById(entity.Id);
        dbEntity.ShouldNotBeNull();
        dbEntity.Id.ShouldBe(entity.Id);
        dbEntity.CreatedAt.ShouldNotBe(default);
        dbEntity.CreatedAt.ShouldBeGreaterThan(utcNow);
    }

    [Fact]
    public async Task Should_Update()
    {
        var utcNow = DateTime.UtcNow;
        var repository = GetInstance();
        var entity = new AuditedDummyEntity();
        entity = await repository.Create(entity);
        // Serialize & Deserialize the entity to break reference (deep-copy)
        // todo: can utilize Prototype Pattern to avoid this mess
        var deepCopy = JsonConvert.DeserializeObject<AuditedDummyEntity>(JsonConvert.SerializeObject(entity)) ?? throw new InvalidOperationException("This should not happen");
        deepCopy.DummyField = "Updated!";
        await repository.Update(deepCopy);
        var dbEntity = await repository.GetById(entity.Id);
        dbEntity.ShouldNotBeNull();
        dbEntity.Id.ShouldBe(entity.Id);
        dbEntity.DummyField.ShouldBe("Updated!");
        dbEntity.LastUpdatedAt.ShouldNotBe(default);
        dbEntity.LastUpdatedAt.ShouldBeGreaterThan(utcNow);
    }

    private static LiteDbGenericRepository<AuditedDummyEntity> GetInstance()
    {
        var liteDbContext = new LiteDbContext(new LiteDbOptions());
        return new LiteDbGenericRepository<AuditedDummyEntity>(liteDbContext);
    }
}