
using FluentCMS.Repository.Abstractions;
using FluentCMS.Repository.LiteDb.Tests.Entities;
using FluentCMS.Repository.LiteDb.Tests.TestContext;
using Newtonsoft.Json;
using Shouldly;
using System.Net.Http.Json;
using System.Runtime.InteropServices;

namespace FluentCMS.Repository.LiteDb.Tests
{
    public class LiteDbAuditRepository_Tests
    {

        [Fact]
        public async Task Should_Create()
        {
            var utcNow = DateTime.UtcNow;
            LiteDbTestContext context;
            var repository = GetInstance(out context);
            var guid = Guid.NewGuid();
            var entity = new AuditedDummyEntity(guid);
            await repository.Create(entity);
            var dbEntity = await repository.GetLiteCollection().FindByIdAsync(guid);
            dbEntity.ShouldNotBeNull();
            dbEntity.Id.ShouldBe(guid);
            dbEntity.CreatedAt.ShouldNotBe(default);
            dbEntity.CreatedAt.ShouldBeGreaterThan(utcNow);
        }
        [Fact]
        public async Task Should_Update()
        {
            var utcNow = DateTime.UtcNow;
            LiteDbTestContext context;
            var repository = GetInstance(out context);
            var guid = Guid.NewGuid();
            var entity = new AuditedDummyEntity(guid);
            await repository.GetLiteCollection().InsertAsync(entity);
            // Serialize & Deserialize the entity to break reference (deep-copy)
            // todo: can utilize Prototype Pattern to avoid this mess
            var deepCopy = JsonConvert.DeserializeObject<AuditedDummyEntity>(JsonConvert.SerializeObject(entity)) ?? throw new InvalidOperationException("This should not happen");
            deepCopy.DummyField = "Updated!";
            await repository.Update(deepCopy);
            var dbEntity = await repository.GetLiteCollection().FindByIdAsync(guid);
            dbEntity.ShouldNotBeNull();
            dbEntity.Id.ShouldBe(guid);
            dbEntity.DummyField.ShouldBe("Updated!");
            dbEntity.LastUpdatedAt.ShouldNotBe(default);
            dbEntity.LastUpdatedAt.ShouldBeGreaterThan(utcNow);
        }

        private LiteDbTestRepository<AuditedDummyEntity> GetInstance([Optional] out LiteDbTestContext liteDbContext)
        {
            liteDbContext = new LiteDbTestContext();
            return new LiteDbTestRepository<AuditedDummyEntity>(liteDbContext);
        }
    }
}