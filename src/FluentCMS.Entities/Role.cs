﻿namespace FluentCMS.Entities;

public class Role : AuditableEntity
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public ICollection<Policy> Policies { get; set; } = [];
}
