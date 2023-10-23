using FluentCMS.Core.Entities;

namespace FluentCMS.Repository.LiteDb.Tests.Entities
{
    internal class DummyEntity : IEntity
    {
        public Guid Id { get; set; }
        public string DummyField { get; set; } = "";
        public DummyEntity(Guid id)
        {
            Id = id;
        }
        protected DummyEntity() { }
    }
}