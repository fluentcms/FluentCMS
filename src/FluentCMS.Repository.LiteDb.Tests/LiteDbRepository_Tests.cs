
using FluentCMS.Repository.Abstractions;
using FluentCMS.Repository.LiteDb.Tests.Entities;
using FluentCMS.Repository.LiteDb.Tests.TestContext;
using Newtonsoft.Json;
using Shouldly;
using System.Net.Http.Json;
using System.Runtime.InteropServices;

namespace FluentCMS.Repository.LiteDb.Tests
{
    public class LiteDbRepository_Tests
    {

        [Fact]
        public async Task Should_Create()
        {
            LiteDbTestContext context;
            var repository = GetInstance(out context);
            var guid = Guid.NewGuid();
            var entity = new DummyEntity(guid);
            await repository.Create(entity);
            var dbEntity = await repository.GetLiteCollection().FindByIdAsync(guid);
            dbEntity.ShouldNotBeNull();
            dbEntity.Id.ShouldBe(guid);
        }
        [Fact]
        public async Task Should_Update()
        {
            LiteDbTestContext context;
            var repository = GetInstance(out context);
            var guid = Guid.NewGuid();
            var entity = new DummyEntity(guid);
            await repository.GetLiteCollection().InsertAsync(entity);
            // Serialize & Deserialize the entity to break reference (deep-copy)
            // todo: can utilize Prototype Pattern to avoid this mess
            var deepCopy = JsonConvert.DeserializeObject<DummyEntity>(JsonConvert.SerializeObject(entity)) ?? throw new InvalidOperationException("This should not happen");
            deepCopy.DummyField = "Updated!";
            await repository.Update(deepCopy);
            var dbEntity = await repository.GetLiteCollection().FindByIdAsync(guid);
            dbEntity.ShouldNotBeNull();
            dbEntity.Id.ShouldBe(guid);
            dbEntity.DummyField.ShouldBe("Updated!");
        }
        [Fact]
        public async Task Should_Delete()
        {
            LiteDbTestContext context;
            var repository = GetInstance(out context);
            var guid = Guid.NewGuid();
            var entity = new DummyEntity(guid);
            await repository.GetLiteCollection().InsertAsync(entity);
            await repository.Delete(guid);
            var dbEntities = await repository.GetLiteCollection().FindAllAsync();
            dbEntities.ShouldNotBeNull();
            dbEntities.ShouldBeEmpty();
        }
        [Fact]
        public async Task Should_GetById()
        {
            LiteDbTestContext context;
            var repository = GetInstance(out context);
            var guid = Guid.NewGuid();
            var entity = new DummyEntity(guid);
            await repository.GetLiteCollection().InsertAsync(entity);
            var dbEntity = await repository.GetById(guid);
            dbEntity.ShouldNotBeNull();
            dbEntity.Id.ShouldBe(guid);
        }
        [Fact]
        public async Task Should_GetByIds()
        {
            LiteDbTestContext context;
            var repository = GetInstance(out context);
            const int count = 10;
            // generate N guids and entities
            var guids = Enumerable.Range(1, count).Select(_ => Guid.NewGuid()).ToList();
            var entities = guids.Select(x => new DummyEntity(x)).ToList();
            await repository.GetLiteCollection().InsertBulkAsync(entities);
            var dbEntities = (await repository.GetByIds(guids)).ToList();
            dbEntities.ShouldNotBeNull();
            dbEntities.Count().ShouldBe(count);
            dbEntities.Select(x => x.Id).ShouldAllBe(x => guids.Contains(x));
        }
        [Fact]
        public async Task Should_GetAll()
        {
            LiteDbTestContext context;
            var repository = GetInstance(out context);
            const int count = 10;
            // generate N guids and entities
            var guids = Enumerable.Range(1, count).Select(_ => Guid.NewGuid()).ToList();
            var entities = guids.Select(x => new DummyEntity(x)).ToList();
            await repository.GetLiteCollection().InsertBulkAsync(entities);
            var dbEntities = (await repository.GetAll()).ToList();
            dbEntities.ShouldNotBeNull();
            dbEntities.Count().ShouldBe(count);
            dbEntities.Select(x => x.Id).ShouldAllBe(x => guids.Contains(x));
        }
        [Fact]
        public async Task Should_GetAll_With_Filter()
        {
            LiteDbTestContext context;
            var repository = GetInstance(out context);
            const int count = 10;
            // generate N guids and entities
            var guids = Enumerable.Range(1, count).Select(_ => Guid.NewGuid()).ToList();
            var entities = guids.Select(x => new DummyEntity(x)).ToList();
            await repository.GetLiteCollection().InsertBulkAsync(entities);
            var firstGuid = guids.First();
            var dbEntities = (await repository.GetAll(x=>x.Id == firstGuid)).ToList();
            dbEntities.ShouldNotBeNull();
            dbEntities.Count().ShouldBe(1);
            dbEntities.All(x=>x.Id == firstGuid).ShouldBeTrue();
        }
        private LiteDbTestRepository<DummyEntity> GetInstance([Optional] out LiteDbTestContext liteDbContext)
        {
            liteDbContext = new LiteDbTestContext();
            return new LiteDbTestRepository<DummyEntity>(liteDbContext);
        }
    }
}