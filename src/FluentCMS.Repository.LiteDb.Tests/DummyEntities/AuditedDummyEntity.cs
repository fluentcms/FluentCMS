using FluentCMS.Entities;
using LiteDB;

namespace FluentCMS.Repository.LiteDb.Tests.Entities;

internal class AuditedDummyEntity : IAuditEntity
{
    [BsonId]
    public Guid Id { get; set; }
    public string DummyField { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = default;
    public string LastUpdatedBy { get; set; } = string.Empty;
    public DateTime LastUpdatedAt { get; set; } = default;

    public AuditedDummyEntity() { }
}
