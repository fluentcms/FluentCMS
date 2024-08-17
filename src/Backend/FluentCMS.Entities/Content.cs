using FluentCMS.Entities.Base;

namespace FluentCMS.Entities;

public class Content : AuditableEntity
{
    public Guid TypeId { get; set; }
    public Dictionary<string, object?> Data { get; set; } = [];
}
