using FluentCMS.Core.Entities;
using LiteDB;

namespace FluentCMS.Repository.LiteDb.Tests.Entities
{
    internal class DummyEntity : IEntity
    {
        [BsonId]
        public Guid Id { get; set; }
        public string DummyField { get; set; } = "";
        public DummyEntity(Guid id)
        {
            Id = id;
        }
        protected DummyEntity() { }
    }
    internal class AuditedDummyEntity : IAuditEntity
    {
        [BsonId]
        public Guid Id { get; set; }
        public string DummyField { get; set; } = "";
        public string CreatedBy { get; set; } = "";
        public DateTime CreatedAt { get; set; } = default;
        public string LastUpdatedBy { get; set; } = "";
        public DateTime LastUpdatedAt { get; set; } = default;

        public AuditedDummyEntity(Guid id)
        {
            Id = id;
        }
        protected AuditedDummyEntity() { }
    }
}