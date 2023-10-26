using FluentCMS.Entities;
using LiteDB;

namespace FluentCMS.Repository.LiteDb.Tests.Entities;

internal class AuditedDummyEntity : IAuditEntity
{
    [BsonId]
    public Guid Id { get; set; }
    public string DummyField { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = default;
    public string LastUpdatedBy { get; set; } = "";
    public DateTime LastUpdatedAt { get; set; } = default;

    protected AuditedDummyEntity() { }
    public AuditedDummyEntity(Guid id)
    {
        Id = id;
    }
}
