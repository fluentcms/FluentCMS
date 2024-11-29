﻿namespace FluentCMS.Entities;

public interface IAuditableEntity : IEntity
{
    string CreatedBy { get; set; }
    DateTime CreatedAt { get; set; }
    string? ModifiedBy { get; set; }
    DateTime? ModifiedAt { get; set; }
}

public abstract class AuditableEntity : Entity, IAuditableEntity
{
    public string CreatedBy { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }
}
