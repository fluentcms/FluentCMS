using FluentCMS.Entities;
using LiteDB;

namespace FluentCMS.Repository.LiteDb.Tests.Entities;

internal class DummyEntity : IEntity
{
    [BsonId]
    public Guid Id { get; set; }
    public string DummyField { get; set; } = "";

    
    public DummyEntity(){}
}
